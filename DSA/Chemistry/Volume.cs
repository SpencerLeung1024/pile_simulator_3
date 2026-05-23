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

    private void SolveReactions()
    {
        // This (Element Potential Method) determines what the free elements should recombine into
        // The problem that NASA Chemical Equilibrium with Applications solves is:
        // Given element amounts and the ability to make any positive amount of species, minimize the Gibbs free energy of the system
        // But Pile Simulator 3 only dissociates a fraction of moles each frame, so the problem we're trying to solve is:
        // Given free element amounts and existing species amounts, choose new species amounts that minimize the Gibbs free energy of the system
        // The n_j produced by SolveReactions should be understood as += existing species amounts

        // Note that what the STANJAN PDF calls "species", I have used classes called "SpeciesPhase".
        // Variable names are still "species" though

        // If we have less than n_jMin moles of each free element, it's impossible to make any realizable amount of species, so skip
        if (freeElements.Count == 0 || freeElements.Values.Max() < Constants.n_jMin)
        {
            return;
        }

        Element[] viewElements; // i = an element, a = num elements, elements are in order of increasing Z
        SpeciesPhase[] viewSpecies; // j = a species, s = num species, unordered, but consistent with the order of their addition to AllSpeciesPhases
        Matrix<double> view; // view[i, j] = n_ij = count of element i in species j
        // The nice thing about Vector<T> and Matrix<T> is that they are initialized with zeros
        FormulaTable.GetView(bitmask, out viewElements, out viewSpecies, out view);
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
            // I think liquids and solids mess this up
            if (viewSpecies[j].Phase != Phase.Gas)
            {
                continue;
            }

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
                GD.Print($"vec_N = [{string.Join(", ", vec_N)}]");
            }
            // Use last frame's vec_N as the initial guess

            // No matter what I try, I can't stabilize the solver so long as liquids and solids exist
            // GPT-5.5 sidestepped the issue by using an activePhases[m] and only ever solving gas
            // Maybe I'll have to revisit liquids and solids another time
            bool[] active_m = new bool[3]
            {
                true, // Phase.Gas
                false, // Phase.Liquid
                false // Phase.Solid
            };
            int num_active = active_m.Count(b => b);

            // But the logic for x_j is the same for all species, and can be vectorized
            vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
            // x_j = mole fraction of species j in its phase
            // Look how clean this is

            if (VERBOSE)
            {
                GD.Print($"vec_lambda = [{string.Join(", ", vec_lambda)}]");
                GD.Print($"vec_x = [{string.Join(", ", vec_x)}]");
            }

            // Normalize by phase
            /*
            for (int m = 0; m < 3; m++)
            {
                double myN_m = 0.0;
                for (int j = 0; j < s; j++)
                {
                    if ((int)viewSpecies[j].Phase == m)
                    {
                        myN_m += vec_x[j];
                    }
                }
                for (int j = 0; j < s; j++)
                {
                    if ((int)viewSpecies[j].Phase == m)
                    {
                        vec_x[j] /= myN_m;
                    }
                }
            }
            GD.Print($"normalized vec_x = [{string.Join(", ", vec_x)}]");
            */

            // Precompute n_j = N_m * x_j (moles of species j = total moles in phase m * mole fraction of species j in its phase)
            // This is in the matrix product for quadrant Q
            // It is also our output
            // We also need a matrix X_phase (s species by p phases) for quadrant D
            // X_phase[j, m] = x_j if species j is in phase m, otherwise 0
            // You could technically make the latter by doing a matrix-vector product (OneHotPhases @ vec_x)
            // But you're gonna have to loop through j anyways
            vec_n = Vector<double>.Build.Dense(s); // n_j = moles of species j
            Matrix<double> X_phase = Matrix<double>.Build.Dense(s, 3); // X_phase[j, m] = x_j if species j is in phase m, otherwise 0
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
            Matrix<double> J = Matrix<double>.Build.Dense(a + 3, a + 3);
            // Build quadrant Q
            // Q_ik = sum over j of N_m * n_ij * n_kj * x_j
            // Q = view @ diag(vec_n) @ view.T
            // The library might not realize that diag(vec_n) is a diagonal matrix and try to do rectangle @ square @ rectangle.T, which would be wasteful
            // So we will fold diag(vec_n) into a matrix ourselves
            // Unfortunately there isn't a way to do (a, s) * s = (a, s)
            // We will have to assemble it row by row
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
            J.SetSubMatrix(0, a, a, 3, D);
            J.SetSubMatrix(a, 3, 0, a, D.Transpose());

            // Patch J to disable inactive phases
            /*
            for (int m = 0; m < 3; m++)
            {
                if (!active_n[m])
                {
                    // Set row a+m to 0 (element potentials have no effect on N_m)
                    // Set col a+m to 0 (N_m has no effect on element potentials)
                    // Set J[a+m, a+m] to 1 (avoid singularity)
                    J.SetRow(a + m, Vector<double>.Build.Dense(a + 3)); // New vector of zeros
                    J.SetColumn(a + m, Vector<double>.Build.Dense(a + 3));
                    J[a + m, a + m] = 1.0;
                }
            }
            */

            // Nope, patching with 1s is not enough. The solver will try to put like -200000 on N_solid and 1e-10 on everything else, so no change occurs
            Matrix<double> minorJ = Matrix<double>.Build.Dense(a + num_active, a + num_active);
            minorJ.SetSubMatrix(0, a, 0, a, Q);
            int minorCol = a;
            for (int Dcol = 0; Dcol < 3; Dcol++)
            {
                if (active_m[Dcol])
                {
                    Vector<double> extractedCol = Vector<double>.Build.Dense(a + num_active);
                    // We need a vector of length a + num_active for SetColumn and SetRow, even though the last bit will be zeros
                    for (int i = 0; i < a; i++)
                    {
                        extractedCol[i] = D[i, Dcol];
                    }
                    minorJ.SetColumn(minorCol, extractedCol);
                    minorJ.SetRow(minorCol, extractedCol);
                    minorCol++;
                }
            }

            Vector<double> minorvec_F = Vector<double>.Build.Dense(a + num_active);
            for (int i = 0; i < a; i++)
            {
                minorvec_F[i] = vec_F[i];
            }
            int minorFIndex = a;
            for (int m = 0; m < 3; m++)
            {
                if (active_m[m])
                {
                    minorvec_F[minorFIndex] = vec_F[a + m];
                    minorFIndex++;
                }
            }

            if (VERBOSE)
            {
                GD.Print($"minorvec_F = [{string.Join(", ", minorvec_F)}]");

                GD.Print("minorJ = ");
                for (int i = 0; i < minorJ.RowCount; i++)
                {
                    GD.Print($"[{string.Join(", ", minorJ.Row(i))}]");
                }
            }

            // Solve
            //Vector<double> delta_x = J.Solve(-vec_F);
            // SVD solving is more stable than the default LU when J is near singular
            Vector<double> minordelta_x = minorJ.Svd().Solve(-minorvec_F);

            // Expand the solved x back to a + 3
            Vector<double> delta_x = Vector<double>.Build.Dense(a + 3);
            for (int i = 0; i < a; i++)
            {
                delta_x[i] = minordelta_x[i];
            }
            int minorxIndex = a;
            for (int m = 0; m < 3; m++)
            {
                if (active_m[m])
                {
                    delta_x[a + m] = minordelta_x[minorxIndex];
                    minorxIndex++;
                }
            }

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
            // Figure out the damping needed to not have any N_m go negative
            for (int m = 0; m < 3; m++)
            {
                if (!active_m[m])
                {
                    continue;
                }
                double delta_N_mMaxDecrease = vec_N[m] - Constants.N_mMin;
                if (Math.Sign(delta_x[a + m]) == -1)
                {
                    damping = Math.Min(damping, Math.Abs(delta_N_mMaxDecrease / delta_x[a + m]));
                }
            }
            delta_x *= damping;
            if (VERBOSE)
            {
                GD.Print($"damped delta_x = [{string.Join(", ", delta_x)}]");
            }

            // Apply Δx
            vec_lambda += delta_x.SubVector(0, a);
            for (int m = 0; m < 3; m++)
            {
                if (active_m[m])
                {
                    vec_N[m] += delta_x[a + m];
                }
            }

            // Early exit conditions
            if (vec_H.PointwiseAbs().Maximum() < Constants.H_iTolerance
                && vec_Z.PointwiseAbs().Maximum() < Constants.Z_mTolerance)
            {
                break;
            }
        }

        // Get += n_j
        vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
        vec_n = Vector<double>.Build.Dense(s);
        for (int j = 0; j < s; j++)
        {
            int phase = (int)viewSpecies[j].Phase;
            vec_n[j] = vec_N[phase] * vec_x[j];
        }

        // Apply += n_j
        for (int j = 0; j < s; j++)
        {
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
        // At equilibrium, the fugacity of a species is equal across all phases it occupies
        // For a gas: f_j^gas = φ_j^gas * P_j where P_j = n_j * R * T / V_gas is the partial pressure under the immiscible-gas assumption
        // For a pure condensed phase: f_j^cond = φ_j^cond * P (system pressure, no mixing)
        // Setting f_gas = f_cond gives the equilibrium gas moles n_j^gas,eq
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

            // This species has no defined phase changes
            if (phaseResources.Count == 1)
            {
                continue;
            }
            
            SpeciesPhaseResource gasResource = phaseResources.Find(r => r.SpeciesPhase.Phase == Phase.Gas);
            if (gasResource == null)
            {
                // No gas phase for this species; saturation vapor pressure cannot be determined
                // move toward lowest-fugacity condensed phase
                // TODO: ban the case of diamond -> graphite
                // SolvePhases bypasses Species.DissociationTemperature
                double[] logphis = phaseResources.Select(r => r.SpeciesPhase.EquationOfState.GetLogphi(T, P, r.SpeciesPhase.EquationOfState.Getv(T, P))).ToArray();
                double minLogphi = logphis.Min();
                int indexOfMin = logphis.ToList().IndexOf(minLogphi);
                for (int i = 0; i < phaseResources.Count; i++)
                {
                    if (i != indexOfMin)
                    {
                        double n_total = phaseResources[i].n + phaseResources[indexOfMin].n;
                        double n_target = phaseResources[i].n * Constants.PhaseDamping; // Damped movement
                        phaseResources[i].n -= n_target;
                        phaseResources[indexOfMin].n += n_target;
                    }
                }
            }
            else
            {
                double n_gas = gasResource.n;
                double V_gas = all_vec_V[0];
                if (V_gas <= 0.0)
                {
                    V_gas = 1e-6; // Avoid division by zero
                }

                foreach (SpeciesPhaseResource condResource in phaseResources)
                {
                    if (condResource == gasResource) continue;

                    double n_cond = condResource.n;
                    double n_total = n_gas + n_cond;
                    if (n_total < Constants.n_jMin) continue;

                    // Condensed phase: pure, so x_j = 1. f_cond = φ_cond * P
                    double phi_cond = Math.Exp(condResource.SpeciesPhase.EquationOfState.GetLogphi(T, P, condResource.SpeciesPhase.EquationOfState.Getv(T, P)));

                    // Gas phase: partial pressure P_j = n_gas * R * T / V_gas, f_gas = φ_gas * P_j
                    // For ideal gas, φ=1. For cubic EOS, use partial pressure to compute φ.
                    double v_gas = V_gas / Math.Max(n_gas, Constants.n_jMin);
                    double P_gas = gasResource.SpeciesPhase.EquationOfState.GetP(T, v_gas);
                    double phi_gas = Math.Exp(gasResource.SpeciesPhase.EquationOfState.GetLogphi(T, P_gas, v_gas));

                    // f_gas = φ_gas * P_gas
                    // f_cond = φ_cond * P
                    // Equilibrium: φ_gas * (n_gas_eq * R * T / V_gas) = φ_cond * P
                    // For ideal gas (φ_gas = 1, P_gas = n*RT/V_gas): n_gas_eq = φ_cond * P * V_gas / (R * T)
                    double fug_gas = phi_gas * P_gas;
                    double fug_cond = phi_cond * P;

                    // Solve φ_gas * (n_gas_eq * R * T / V_gas) = φ_cond * P
                    // n_gas_eq = (φ_cond / φ_gas) * P * V_gas / (R * T)
                    double n_gas_eq = (phi_cond / phi_gas) * P * V_gas / (Constants.R * T);
                    n_gas_eq = Math.Clamp(n_gas_eq, 0.0, n_total);

                    // Damped movement toward equilibrium
                    double n_gas_target = n_gas + Constants.PhaseDamping * (n_gas_eq - n_gas);
                    n_gas_target = Math.Clamp(n_gas_target, 0.0, n_total);

                    gasResource.n = n_gas_target;
                    condResource.n = n_total - n_gas_target;

                    // One condensed phase per species per SolvePhases call
                    // If both solid and liquid are trying to equilibrate with gas, the first loop will use the entire calculated n_gas_target
                    break;
                }
            }

            // If any fake resources ended up with real amounts, add them to Resources
            foreach (SpeciesPhaseResource fakeResource in fakeResources)
            {
                if (fakeResource.n > 0.0)
                {
                    Resources.Add(fakeResource);
                }
            }
        }
    }

    public void Solve()
    {
        Dissociate();
        RebuildIndexes();
        DeriveQuantities();
        SolveReactions();
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

    public void Clear()
    {
        Resources.Clear();
        UTarget = 0.0;
        RebuildIndexes();
        DeriveQuantities();
    }
}
