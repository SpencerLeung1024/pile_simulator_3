using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

// Public for BoxSim UI and other UI, but unable to modify the volume's contents
public class ResourceDisplayEntry
{
    public SpeciesPhase SpeciesPhase;
    public double n;
    public double ResourceMass;
    public double ResourceVolume;
}

// A volume represents something that contains matter
// The matter may be made up of numerous species, existing as gas, liquid, and solid phases
public class Volume : Inventory<SpeciesPhaseResource>
{
    const bool VERBOSE = false;

    // Resources: the species in phases in this volume, and their amounts in mol
    // Volume: m^3
    public double T; // K
    public double P; // Pa
    public double UTarget; // J, conserved when guessing T
    public bool spark; // If a spark exists in the volume, dissociation temperatures are ignored and dissociation has a minimum of Constants.DissociationThreshold per frame
    
    // Derived quantities:
    // Mass: kg
    // UsedVolume: m^3, slighly different meaning in Volume: the volume taken up by condensed phases only
    // FreeVolume: m^3, has a getter: Volume - UsedVolume
    public double C_v; // J / K, heat capacity at constant volume of the system
    public double U; // J, internal energy of the system
    public double S; // J / K, entropy of the system
    // H = U + PV, enthalpy of the system
    // G = H - TS, Gibbs free energy of the system
    // Publicing U and S allows the BoxSim UI to calculate and show thermodynamic variables
    public Vector<double> all_vec_N = Vector<double>.Build.Dense(3); // [N_gas, N_liquid, N_solid] (enum Phase order) of everything in Resources
    public Vector<double> all_vec_V = Vector<double>.Build.Dense(3); // [V_gas, V_liquid, V_solid] of everything in Resources

    // Internal solver variables:
    private Dictionary<SpeciesPhase, SpeciesPhaseResource> speciesPhaseToResource;
    private Dictionary<Species, List<SpeciesPhaseResource>> speciesToResources;
    private Dictionary<Element, double> freeElements; // Can be negative
    private ulong bitmask;
    private Vector<double> vec_lambda;
    private Vector<double> vec_N = Vector<double>.Build.Dense(3); // [N_gas, N_liquid, N_solid] of only newly recombined species phases
    //private Vector<double> vec_V = Vector<double>.Build.Dense(3); // [V_gas, V_liquid, V_solid] of only newly recombined species phases
    // Not used

    // Constructor for an empty volume
    public Volume(double volume)
    {
        Resources = new List<SpeciesPhaseResource>();
        Volume = volume;
        //T = Constants.Tmin;
        //P = Constants.Pmin;
        // The minimum values cause massive instabilities
        T = Constants.NISTNormalTemperature;
        P = Constants.bar;
        speciesToResources = new Dictionary<Species, List<SpeciesPhaseResource>>();
        speciesPhaseToResource = new Dictionary<SpeciesPhase, SpeciesPhaseResource>();
        freeElements = new Dictionary<Element, double>();
        for (int m = 0; m < 3; m++)
        {
            all_vec_N[m] = Constants.N_mMin;
            all_vec_V[m] = Constants.V_mMin;
            vec_N[m] = Constants.N_mMin; // Avoid zero values that break the solver
        }
    }

    protected override void DeriveQuantities()
    {
        // Mass, vec_V, UsedVolume, C_v, U, S
        Mass = 0.0;
        for (int m = 0; m < 3; m++)
        {
            all_vec_N[m] = 0.0;
            all_vec_V[m] = 0.0;
        }
        UsedVolume = 0.0;
        C_v = 0.0;
        U = 0.0;
        S = 0.0;
        foreach (SpeciesPhaseResource resource in Resources)
        {
            double n = resource.n;
            double v = resource.SpeciesPhase.EquationOfState.Getv(T, P);
            Mass += resource.SpeciesPhase.Species.MolarMass * n;
            int phaseAsIndex = (int)resource.SpeciesPhase.Phase;
            all_vec_N[phaseAsIndex] += n;
            all_vec_V[phaseAsIndex] += v * n;
            C_v += resource.SpeciesPhase.EquationOfState.Getc_v(T, v) * n;
            U += resource.SpeciesPhase.EquationOfState.GetU(T, v) * n;
            S += resource.SpeciesPhase.HeatCapacityFunction.GetS(T) * n;
        }
        for (int m = 1; m < 3; m++) // Skip Phase.Gas
        {
            UsedVolume += all_vec_V[m];
        }
    }

    // TODO: Cache this if multiple calls happen in the same frame, but remember to invalidate the cache
    public List<ResourceDisplayEntry> GetInfo()
    {
        List<ResourceDisplayEntry> info = new List<ResourceDisplayEntry>();
        foreach (SpeciesPhaseResource resource in Resources)
        {
            double v = resource.SpeciesPhase.EquationOfState.Getv(T, P);
            info.Add(new ResourceDisplayEntry
            {
                SpeciesPhase = resource.SpeciesPhase,
                n = resource.n,
                ResourceMass = resource.SpeciesPhase.Species.MolarMass * resource.n,
                ResourceVolume = v * resource.n
            });
        }
        return info;
    }

    private void FullyDissociateAndRemove(SpeciesPhaseResource resource)
    {
        foreach ((Element element, uint count) in resource.SpeciesPhase.Species.Formula)
        {
            if (!freeElements.ContainsKey(element))
            {
                freeElements[element] = 0.0;
            }
            freeElements[element] += count * resource.n;
        }
        Resources.Remove(resource);
    }

    private void Dissociate()
    {
        // We may need to remove resources if they were fully dissociated, so we need a backward for loop
        for (int j = Resources.Count - 1; j >= 0; j--)
        {
            SpeciesPhaseResource resource = Resources[j];
            Species species = resource.SpeciesPhase.Species;
            if (T > species.DissociationTemperature || spark)
            {
                double k = Math.Exp(-species.DissociationActivationEnergy / (Constants.R * T)); // Arrhenius equation k = Ae^(-E_a / RT) with A = 1 / frame
                if (spark)
                {
                    k = Math.Max(k, Constants.DissociationThreshold);
                }
                double n_dissociated = resource.n * k;
                if (resource.n - n_dissociated < Constants.n_jMin)
                {
                    n_dissociated = resource.n; // Fully dissociate if it would go below the minimum threshold
                    Resources.RemoveAt(j); // We keep a reference to species so it's ok
                    // Don't use FullyDissociateAndRemove or you will double count the freed elements below
                }
                else
                {
                    resource.n -= n_dissociated;
                }
                foreach ((Element element, uint count) in species.Formula)
                {
                    if (!freeElements.ContainsKey(element))
                    {
                        freeElements[element] = 0.0;
                    }
                    freeElements[element] += count * n_dissociated;
                }
            }
        }
    }

    private void RebuildIndexes() // Not BuildIndexes like the static classes because Volume.Resources changes every frame
    {
        // speciesToResources, speciesPhaseToResource, bitmask
        speciesToResources.Clear();
        speciesPhaseToResource.Clear();
        HashSet<Element> existingElements = new HashSet<Element>();
        foreach (SpeciesPhaseResource resource in Resources)
        {
            SpeciesPhase speciesPhase = resource.SpeciesPhase;
            Species species = speciesPhase.Species;
            if (!speciesToResources.ContainsKey(species))
            {
                speciesToResources[species] = new List<SpeciesPhaseResource>();
            }
            speciesToResources[species].Add(resource);
            speciesPhaseToResource[speciesPhase] = resource;
            existingElements.UnionWith(species.Formula.Keys);
        }
        // Include free elements
        existingElements.UnionWith(freeElements.Keys);
        ulong newBitmask = FormulaTable.GetViewBitmask(existingElements.ToList());
        // Invalidate vec_lambda if bitmask changed
        if (newBitmask != bitmask)
        {
            bitmask = newBitmask;
            vec_lambda = Vector<double>.Build.Dense(existingElements.Count);
            for (int i = 0; i < vec_lambda.Count; i++)
            {
                vec_lambda[i] = 0.0; // Restart with all elements having zero element potential
            }
        }
    }

    private void SolveReactionsGas()
    {
        // Following `docs/chemistry/solver_debug_2`
        // This gas-only version takes viewElements, viewSpecies, and view
        // Then it selects only the columns of view corresponding to gas species

        // Differences compared to the proper Element Potential Method:

        // The problem that NASA Chemical Equilibrium with Applications solves is:
        // Given element amounts and the ability to make any positive amount of species, minimize the Gibbs free energy of the system
        // Meanwhile, Pile Simulator 3 only dissociates a fraction of moles each frame, and tries to solve:
        // Given free element amounts and existing species amounts, choose new species amounts that minimize the Gibbs free energy of the system
        // n_j should be understood as += existing species amounts

        // Note that what the STANJAN PDF calls "species", I have used classes called "SpeciesPhase".
        // Variable names are still "species" though

        // If we have less than n_jMin moles of each free element, it's impossible to make any realizable amount of species, so skip
        if (freeElements.Count == 0 || freeElements.Values.Max() < Constants.n_jMin)
        {
            return;
        }

        Element[] viewElements; // i = an element, a = num elements, elements are in order of increasing Z
        SpeciesPhase[] multiPhaseViewSpecies; // j = a species, s = num species, unordered, but consistent with the order of their addition to AllSpeciesPhases
        Matrix<double> multiPhaseView; // view[i, j] = n_ij = count of element i in species j
        // The nice thing about Vector<T> and Matrix<T> is that they are initialized with zeros
        FormulaTable.GetView(bitmask, out viewElements, out multiPhaseViewSpecies, out multiPhaseView);

        // New in SolveReactionsGas: view chopping
        List<SpeciesPhase> viewSpeciesList = new List<SpeciesPhase>();
        List< Vector<double> > jSticks = new List< Vector<double> >();
        for (int multiPhasej = 0; multiPhasej < multiPhaseViewSpecies.Length; multiPhasej++)
        {
            SpeciesPhase species = multiPhaseViewSpecies[multiPhasej];
            if (species.Phase == Phase.Gas)
            {
                viewSpeciesList.Add(species);
                jSticks.Add(multiPhaseView.Column(multiPhasej));
            }
        }
        // Fix as array and matrix
        SpeciesPhase[] viewSpecies = viewSpeciesList.ToArray();
        Matrix<double> view = Matrix<double>.Build.DenseOfColumnVectors(jSticks);
        int a = viewElements.Length;
        int s = viewSpecies.Length;

        // GPT-5.5: Pre-solve vec_lambda so the dominant species has x_j close to 1 and J doesn't blow up
        // I have no idea how this works
        Vector<double> initialMu = Vector<double>.Build.Dense(s);
        for (int j = 0; j < s; j++)
        {
            initialMu[j] = viewSpecies[j].Getmu(T, P, 1.0);
        }
        List<int> selectedSpecies = new List<int>();
        List<Vector<double>> orthonormalColumns = new List<Vector<double>>();
        foreach (int j in Enumerable.Range(0, s).OrderBy(j => initialMu[j]))
        {
            Vector<double> residual = view.Column(j).Clone();
            foreach (Vector<double> q in orthonormalColumns)
            {
                residual -= q * residual.DotProduct(q);
            }

            double norm = residual.L2Norm();
            if (norm <= 1e-9)
            {
                continue;
            }

            selectedSpecies.Add(j);
            orthonormalColumns.Add(residual / norm);
            if (selectedSpecies.Count == a)
            {
                break;
            }
        }
        if (selectedSpecies.Count == a)
        {
            Matrix<double> A_init = Matrix<double>.Build.Dense(a, a);
            Vector<double> b_init = Vector<double>.Build.Dense(a);
            for (int row = 0; row < a; row++)
            {
                int j = selectedSpecies[row];
                for (int i = 0; i < a; i++)
                {
                    A_init[row, i] = view[i, j];
                }
                b_init[row] = initialMu[j] / (Constants.R * T);
            }
            vec_lambda = A_init.Solve(b_init);
        }

        // We need to declare these outside the loop
        Vector<double> vec_mu = Vector<double>.Build.Dense(s);
        Vector<double> vec_x;
        Vector<double> vec_n;

        // Moved out of the loop because I realized mu_j depends only on T and P, which are held constant during SolveReactions
        
        // `docs/chemistry/solver/opus_4_7.md`:
        // mu_j is a non-linear function so there's no way to optimize it other than calling it for each species
        // Calculate mu_j of every species
        for (int j = 0; j < s; j++)
        {
            vec_mu[j] = viewSpecies[j].Getmu(T, P, 1.0);
            // mu_j = chemical potential of species j
            // Use the pure species chemical potential (mole fraction x_j = 1)
        }

        if (VERBOSE)
        {
            GD.Print($"vec_mu = [{string.Join(", ", vec_mu)}]");
        }

        for (int reactionStep = 0; reactionStep < Constants.MaxReactionSteps; reactionStep++)
        {
            if (VERBOSE)
            {
                GD.Print($"reactionStep = {reactionStep}");
                GD.Print("Now with 67% fewer phases!");
            }

            // But the logic for x_j is the same for all species, and can be vectorized
            vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
            // x_j = mole fraction of species j in its phase
            // Look how clean this is

            if (VERBOSE)
            {
                GD.Print($"vec_lambda = [{string.Join(", ", vec_lambda)}]");
                GD.Print($"vec_x = [{string.Join(", ", vec_x)}]");
            }

            // Precompute n_j = N_m * x_j (moles of species j = total moles in phase m * mole fraction of species j in its phase)
            vec_n = Vector<double>.Build.Dense(s); // n_j = moles of species j
            Matrix<double> X_phase = Matrix<double>.Build.Dense(s, 1); // X_phase[j, m] = x_j if species j is in phase m, otherwise 0
            for (int j = 0; j < s; j++)
            {
                int phase = (int)viewSpecies[j].Phase;
                vec_n[j] = vec_N[phase] * vec_x[j];
                X_phase[j, phase] = vec_x[j];
            }

            // Build vec_p
            // p_i = mol of free element i entering SolveReactions, should all be consumed by newly formed species
            // Annoyingly the STANJAN PDF uses p_i = mol of element i but p = num phases
            Vector<double> vec_p = Vector<double>.Build.Dense(a);
            for (int i = 0; i < a; i++)
            {
                Element element = viewElements[i];
                freeElements.TryGetValue(element, out double p_i);
                if (p_i != default)
                {
                    vec_p[i] = p_i;
                }
            }

            if (VERBOSE)
            {
                GD.Print($"vec_n = [{string.Join(", ", vec_n)}]");
                GD.Print($"vec_p = [{string.Join(", ", vec_p)}]");
            }

            // J @ Δx = -F
            // Ax = b

            // Refer to:
            // `docs/chemistry/dual_problem/gpt_5_5.md`
            // `docs/chemistry/solver/opus_4_7.md`

            // Build F
            // We can calculate all element balance residuals at once:
            Vector<double> vec_H = view * vec_n - vec_p;
            // We can calculate all phase normalization residuals by summing each column of X_phase:
            Vector<double> vec_Z = X_phase.ColumnSums();
            // Stuff into F
            Vector<double>vec_F = Vector<double>.Build.DenseOfEnumerable(vec_H.Concat(vec_Z - 1.0));

            // Build J
            Matrix<double> J = Matrix<double>.Build.Dense(a + 1, a + 1);

            // Build quadrant Q
            // Q_ik = sum over j of N_m * n_ij * n_kj * x_j
            // Q = view @ diag(vec_n) @ view.T
            Matrix<double> viewWithvec_n = Matrix<double>.Build.Dense(a, s);
            for (int i = 0; i < a; i++)
            {
                viewWithvec_n.SetRow(i, view.Row(i).PointwiseMultiply(vec_n));
            }
            Matrix<double> Q = viewWithvec_n * view.Transpose(); // (a, s) * (s, a) = (a, a)
            // Build quadrant D
            // D_im = sum over j in phase m of n_ij * x_j
            // Now that we have X_phase, we have an easy way of selecting only species j in phase m
            // D = view @ X_phase
            Matrix<double> D = view * X_phase; // (a, s) * (s, p) = (a, p)
            // D.T has the same information as D
            // We don't need to build quadrant 0 since values are initialized to zero
            // Stuff into J
            J.SetSubMatrix(0, a, 0, a, Q);
            J.SetSubMatrix(0, a, a, 1, D);
            J.SetSubMatrix(a, 1, 0, a, D.Transpose());

            if (VERBOSE)
            {
                GD.Print($"vec_F = [{string.Join(", ", vec_F)}]");

                GD.Print("J = ");
                for (int i = 0; i < J.RowCount; i++)
                {
                    GD.Print($"[{string.Join(", ", J.Row(i))}]");
                }
            }

            // Solve
            //Vector<double> delta_x = J.Solve(-vec_F);
            // SVD solving is more stable than the default LU when J is near singular
            Vector<double> delta_x = J.Svd().Solve(-vec_F);

            if (VERBOSE)
            {
                GD.Print($"delta_x = [{string.Join(", ", delta_x)}]");
            }

            double damping = 1.0;
            // Figure out the damping needed to not have any lambda_i jump more than Constants.LambdaMaxJump
            for (int i = 0; i < a; i++)
            {
                if (Math.Abs(delta_x[i]) > Constants.LambdaMaxJump)
                {
                    damping = Math.Min(damping, Constants.LambdaMaxJump / Math.Abs(delta_x[i]));
                }
            }
            // Figure out the damping needed to not have N_gas go negative
            double delta_N_gas = delta_x[a]; // Last entry
            double N_gasAboveMin = vec_N[0] - Constants.N_mMin;
            if (Math.Sign(delta_N_gas) == -1) // Decreasing
            {
                damping = Math.Min(damping, Math.Abs(N_gasAboveMin / delta_N_gas));
            }
            delta_x *= damping;
            if (VERBOSE)
            {
                GD.Print($"damped delta_x = [{string.Join(", ", delta_x)}]");
            }

            // Apply Δx
            vec_lambda += delta_x.SubVector(0, a);
            vec_N[0] += delta_x[a];

            // Early exit conditions
            if (vec_H.PointwiseAbs().Maximum() < Constants.H_iTolerance
                && vec_Z.PointwiseAbs().Maximum() < Constants.Z_mTolerance)
            {
                break;
            }
        }

        // Get and apply += n_j
        vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
        vec_n = Vector<double>.Build.Dense(s);
        for (int j = 0; j < s; j++)
        {
            int phase = (int)viewSpecies[j].Phase;
            vec_n[j] = vec_N[phase] * vec_x[j];

            // Don't create resources with miniscule amounts
            if (vec_n[j] < Constants.n_jMin)
            {
                continue;
            }
            // Figure out which resource this species corresponds to
            SpeciesPhase speciesPhase = viewSpecies[j];
            speciesPhaseToResource.TryGetValue(speciesPhase, out SpeciesPhaseResource resource);
            if (resource != null)
            {
                resource.n += vec_n[j];
            }
            else
            {
                // This species didn't exist before, so we need to make a new resource for it
                resource = new SpeciesPhaseResource()
                {
                    SpeciesPhase = speciesPhase,
                    n = vec_n[j]
                };
                Resources.Add(resource); // Not the public MaybeAdd, so does not call DeriveQuantities
            }
            // freeElements bookkeeping
            foreach ((Element element, uint count) in speciesPhase.Species.Formula)
            {
                if (!freeElements.ContainsKey(element))
                {
                    // If this element didn't exist before but (somehow) the solver used it, bookkeep with a negative amount
                    freeElements[element] = 0.0;
                }
                freeElements[element] -= count * vec_n[j];
            }
        }
    }

    private void SolveUT() // At constant U, Newton iteration on T, calls DeriveQuantities at each step
    {
        for (int UTStep = 0; UTStep < Constants.MaxUTSteps; UTStep++)
        {
            DeriveQuantities();
            double UError = U - UTarget;
            double relUError = UError / Math.Abs(UTarget);
            if (Math.Abs(relUError) < Constants.UTolerance)
            {
                break;
            }
            // Newton step: solve f(T) = U(T) - UTarget = 0
            // f'(T) = dU/dT = C_v
            // ΔT = -f(T) / f'(T) = -(U - UTarget) / C_v
            double deltaT = -UError / C_v;
            deltaT = Math.Clamp(deltaT, -Constants.MaxDeltaT, Constants.MaxDeltaT);
            T += deltaT;
            if (T < Constants.Tmin)
            {
                T = Constants.Tmin;
            }
        }
    }

    private void SolveVP() // At constant V, Newton iteration on P, calls DeriveQuantities at each step
    {
        for (int VPStep = 0; VPStep < Constants.MaxVPSteps; VPStep++)
        {
            DeriveQuantities();
            double VError = all_vec_V.Sum() - Volume;
            double relVError = VError / Volume;
            if (Math.Abs(relVError) < Constants.VTolerance)
            {
                break;
            }
            // Newton step: V(P) ≈ V * P_old / P (ideal gas)
            // dV/dP = -V/P, so ΔP = -VError * P (the proportional step)
            // Clamp to prevent wild swings with cubic EOS
            double deltaP = relVError * P;
            deltaP = Math.Clamp(deltaP, -Constants.MaxDeltaP, Constants.MaxDeltaP);
            P += deltaP;
            if (P < Constants.Pmin)
            {
                P = Constants.Pmin;
            }
        }
    }

    private void SolvePhases()
    {
        // Fugacity f_j = fugacity coefficient φ_j * P_j is only one part of the equilibrium
        // We should compare chemical potential μ_j = μ_j° + RT ln(f_j / P°) where μ_j° is the standard chemical potential at 1 bar
        // mu_std (T = 293 K, P = 1 bar, x_j = 1) includes H(293 K) which is the standard enthalpy of formation

        foreach((Species species, List<SpeciesPhaseResource> realResources) in speciesToResources)
        {
            // Create fictional phases with 0 moles in case we need to move moles to them
            // They will only be added to Resources if they actually received anything

            // All phases of this species
            List<SpeciesPhase> fakeSpeciesPhases = species.Phases.Except(realResources.Select(r => r.SpeciesPhase)).ToList();
            List<SpeciesPhaseResource> fakeResources = new List<SpeciesPhaseResource>();

            List<SpeciesPhaseResource> phaseResources = new List<SpeciesPhaseResource>();
            phaseResources.AddRange(realResources);
            foreach (SpeciesPhase speciesPhase in fakeSpeciesPhases)
            {
                SpeciesPhaseResource fakeResource = new SpeciesPhaseResource()
                {
                    SpeciesPhase = speciesPhase,
                    n = 0.0
                };
                phaseResources.Add(fakeResource);
                fakeResources.Add(fakeResource); // We will need this later, to either add it to Resources or let it be garbage collected
            }

            int numPhases = phaseResources.Count;
            // This species has no defined phase changes
            if (numPhases < 2)
            {
                continue;
            }

            // うん。今日から私たちはμ'sだ！
            List<double> mus = new List<double>(); // Needs to be a list so we can do IndexOf
            SpeciesPhaseResource gasResource = null; // May be null
            for (int j = 0; j < numPhases; j++)
            {
                SpeciesPhaseResource resource = phaseResources[j];
                if (resource.SpeciesPhase.Phase == Phase.Gas)
                {
                    gasResource = resource;
                    // Use the gas formula: G°_gas(T) + RT ln(φ_gas * P_gas / P°)
                    double P_partial = P * (resource.n / all_vec_N[0]); // Assuming gases contribute pressure proportionally
                    mus.Add(resource.SpeciesPhase.Getmu(
                        T,
                        P_partial,
                        1.0
                    ));
                }
                else
                {
                    // Use the condensed formula G°_cond(T) + v_cond*(P - P°)
                    double v = resource.SpeciesPhase.EquationOfState.Getv(T, P);
                    double poyntingCorrection = v * (P - Constants.bar);
                    mus.Add(resource.SpeciesPhase.Getmu(
                        T,
                        0.0,
                        1.0
                    ) - poyntingCorrection);
                }
            }

            GD.Print($"Species {species.Name}: mus = [{string.Join(", ", mus)}], n = [{string.Join(", ", phaseResources.Select(r => r.n))}]");

            // Algorithm:
            // Every frame, move Constants.PhaseDamping fraction of moles from the highest mu to the lowest mu
            // If either src or dst is a gas, calculate P_sat and use n_sat as a bound on moles to move
            // Otherwise, between condensed phases, mu does not change with partial pressure so move a fixed fraction

            /*
            int srcj = mus.IndexOf(mus.Max());
            int dstj = mus.IndexOf(mus.Min());
            SpeciesPhaseResource srcResource = phaseResources[srcj];
            SpeciesPhaseResource dstResource = phaseResources[dstj];
            if (srcResource == gasResource || dstResource == gasResource)
            {
            */
            if (gasResource != null)
            {
                // Case 1: src or dst is gas
                // Well actually the other path is broken so just select the mu with the largest absolute difference from gas
                int gasj = phaseResources.IndexOf(gasResource);
                double mu_gas = mus[gasj];
                List<double> absDiffmus = new List<double>();
                for (int j = 0; j < numPhases; j++)
                {
                    absDiffmus.Add(Math.Abs(mus[j] - mu_gas));
                }
                int condj = absDiffmus.IndexOf(absDiffmus.Max());
                //int condj = srcResource == gasResource ? dstj : srcj;
                //int gasj = srcResource == gasResource ? srcj : dstj;
                SpeciesPhaseResource condResource = phaseResources[condj];
                // We used mu at current conditions to determine src and dst
                // But the exact formula for P_sat requires mu at standard conditions
                double[] mu_stds = new double[numPhases];
                for (int j = 0; j < numPhases; j++)
                {
                    mu_stds[j] = phaseResources[j].SpeciesPhase.Getmu(
                        Constants.NISTNormalTemperature,
                        Constants.bar,
                        1.0
                    );
                }
                double mu_std_cond = mu_stds[condj];
                double mu_std_gas = mu_stds[gasj];
                GD.Print($"mu_std_cond = {mu_std_cond}, mu_std_gas = {mu_std_gas}");
                GD.Print($"n_cond = {condResource.n}, n_gas = {gasResource.n}");
                double P_sat = Constants.bar * Math.Exp((mu_std_cond - mu_std_gas) / (Constants.R * T));
                double V_gas = Math.Max(all_vec_V[0], Constants.V_mMin);
                double n_gas_sat = P_sat * V_gas / (Constants.R * T);
                double gas_wants_n = n_gas_sat - gasResource.n;
                double n_to_move = 0.0;
                GD.Print($"P_sat = {P_sat}, n_gas_sat = {n_gas_sat}, gas_wants_n = {gas_wants_n}");
                if (gas_wants_n < 0.0)
                {
                    // Case 1a: gas -> cond
                    n_to_move = -Math.Min(-gas_wants_n, gasResource.n);
                }
                else
                {
                    // Case 1b: cond -> gas
                    n_to_move = Math.Min(gas_wants_n, condResource.n);
                }
                if (n_to_move != 0.0)
                {
                    n_to_move *= Constants.PhaseDamping;
                    GD.Print($"{condResource.SpeciesPhase.Phase} <-> Gas: moving {n_to_move} moles");
                    condResource.n -= n_to_move;
                    gasResource.n += n_to_move;
                }
            }
            // This path is bugged right now
            /*
            else
            {
                // Case 2: cond -> cond
                // Just move a fixed fraction of src
                double n_to_move = srcResource.n * Constants.PhaseDamping;
                GD.Print($"{srcResource.SpeciesPhase.Phase} <-> {dstResource.SpeciesPhase.Phase}: moving {n_to_move} moles");
                srcResource.n -= n_to_move;
                dstResource.n += n_to_move;
            }
            */

            // If any real resources ended up below the minimum amount, remove them
            // Backwards for
            for (int j = realResources.Count - 1; j >= 0; j--)
            {
                if (realResources[j].n < Constants.n_jMin)
                {
                    // Return its elements to freeElements
                    FullyDissociateAndRemove(realResources[j]);
                    realResources.RemoveAt(j);
                }
            }

            // If any fake resources ended up with real amounts, add them to Resources
            foreach (SpeciesPhaseResource fakeResource in fakeResources)
            {
                if (fakeResource.n > 0.0)
                {
                    // But only if enough exists
                    if (fakeResource.n > Constants.n_jMin)
                    {
                        Resources.Add(fakeResource);
                    }
                    else
                    {
                        FullyDissociateAndRemove(fakeResource);
                    }
                }
            }
        }
    }

    public void Solve()
    {
        Dissociate();
        RebuildIndexes();
        DeriveQuantities();
        SolveReactionsGas();
        SolveUT();
        SolveVP();
        RebuildIndexes();
        SolvePhases();
    }

    // Volume is a thermodynamic simulation, so putting things in the box requires work
    // A full theory of pumps is needed before those methods can be implemented
    public override bool CanAdd(SpeciesPhaseResource resource)
    {
        return false; // Stub: pumps not implemented yet
    }

    // public override bool MaybeAdd(SpeciesPhaseResource resource)
    // {
    //     return false; // Stub: pumps not implemented yet
    // }

    // Temporary methods so BoxSim works
    // There's nothing more permanent than a temporary solution
    // We assume whatever is driving BoxSim has infinite pump power
    public override bool MaybeAdd(SpeciesPhaseResource resource)
    {
        // Figure out if a resource of this species phase already exists
        if (speciesPhaseToResource.TryGetValue(resource.SpeciesPhase, out SpeciesPhaseResource existingResource))
        {
            existingResource.n += resource.n;
        }
        else
        {
            Resources.Add(resource);
        }
        RebuildIndexes();
        DeriveQuantities();
        return true;
    }

    public void AssignUAtTP(double T, double P)
    {
        // Calculates internal energy at the given conditions
        this.T = T;
        this.P = P;
        DeriveQuantities();
        UTarget = U;
    }

    public void Clear()
    {
        Resources.Clear();
        UTarget = 0.0;
        RebuildIndexes();
        DeriveQuantities();
    }
}