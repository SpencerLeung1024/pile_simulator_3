using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

// A volume represents something that contains matter
// The matter may be made up of numerous species, existing as gas, liquid, and solid phases
public class Volume : Inventory<SpeciesPhaseResource>
{
    // Resources: the species in phases in this volume, and their amounts in mol
    // Volume: m^3
    public double T; // K
    public double P; // Pa
    public double UTarget; // J, conserved when guessing T
    
    // Derived quantities:
    // Mass: kg
    // UsedVolume: m^3, slighly different meaning in Volume: the volume taken up by condensed phases only
    // FreeVolume: m^3, has a getter: Volume - UsedVolume
    public double U; // J, internal energy of the system
    public double S; // J / K, entropy of the system
    // H = U + PV, enthalpy of the system
    // G = H - TS, Gibbs free energy of the system
    // Publicing U and S allows the BoxSim UI to calculate and show thermodynamic variables

    // Internal solver variables:
    private Dictionary<SpeciesPhase, SpeciesPhaseResource> speciesPhaseToResource;
    private Dictionary<Species, List<SpeciesPhaseResource>> speciesToResources;
    private Dictionary<Element, double> freeElements; // Can be negative
    private ulong bitmask;
    private Vector<double> vec_lambda;
    private Vector<double> vec_N = Vector<double>.Build.Dense(3); // [N_gas, N_liquid, N_solid] (enum Phase order)
    private Vector<double> vec_V = Vector<double>.Build.Dense(3); // [V_gas, V_liquid, V_solid]

    protected override void DeriveQuantities()
    {
        // Mass, vec_V, UsedVolume, U, S
        Mass = 0.0;
        for (int m = 0; m < 3; m++)
        {
            vec_V[m] = 0.0;
        }
        UsedVolume = 0.0;
        U = 0.0;
        S = 0.0;
        foreach (SpeciesPhaseResource resource in Resources)
        {
            double n = resource.n;
            double v = resource.SpeciesPhase.EquationOfState.Getv(T, P);
            Mass += resource.SpeciesPhase.Species.MolarMass * n;
            int phaseAsIndex = (int)resource.SpeciesPhase.Phase;
            vec_V[phaseAsIndex] += v * n;
            U += resource.SpeciesPhase.EquationOfState.GetU(T, v) * n;
            S += resource.SpeciesPhase.HeatCapacityFunction.GetS(T) * n;
        }
        for (int m = 1; m < 3; m++) // Skip Phase.Gas
        {
            UsedVolume += vec_V[m];
        }
    }

    private void Dissociate()
    {
        // We may need to remove resources if they were fully dissociated, so we need a backward for loop
        for (int j = Resources.Count - 1; j >= 0; j--)
        {
            SpeciesPhaseResource resource = Resources[j];
            Species species = resource.SpeciesPhase.Species;
            if (T > species.DissociationTemperature)
            {
                double k = Math.Exp(-species.DissociationActivationEnergy / (Constants.R * T)); // Arrhenius equation k = Ae^(-E_a / RT) with A = 1 / frame
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
                speciesToResources[species] = new List<SpeciesPhaseResource>()
                {
                    resource
                };
            }
            speciesPhaseToResource[speciesPhase] = resource;
            existingElements.UnionWith(species.Formula.Keys);
        }
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

        Element[] viewElements; // i = an element, a = num elements, elements are in order of increasing Z
        SpeciesPhase[] viewSpecies; // j = a species, s = num species, unordered, but consistent with the order of their addition to AllSpeciesPhases
        Matrix<Double> view; // view[i, j] = n_ij = count of element i in species j
        // The nice thing about Vector<T> and Matrix<T> is that they are initialized with zeros
        FormulaTable.GetView(bitmask, out viewElements, out viewSpecies, out view);
        int a = viewElements.Length;
        int s = viewSpecies.Length;

        // We need to declare these outside the loop
        Vector<double> vec_mu;
        Vector<double> vec_x;
        Vector<double> vec_n;

        for (int reactionStep = 0; reactionStep < Constants.MaxReactionSteps; reactionStep++)
        {
            // `docs/chemistry/solver/opus_4_7.md`:
            // mu_j is a non-linear function so there's no way to optimize it other than calling it for each species
            // But the logic for x_j is the same for all species, and can be vectorized

            vec_mu = Vector<double>.Build.Dense(s); // m_j = chemical potential of species j
            // Calculate mu_j of every species
            for (int j = 0; j < s; j++)
            {
                vec_mu[j] = viewSpecies[j].Getmu(T, P, 1.0); // Use the pure species chemical potential (mole fraction x_j = 1)
            }
            vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
            // x_j = mole fraction of species j in its phase
            // Look how clean this is

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
            // We will have to do our own broadcast of vec_n to do pointwise multiplication (a, s) * (a, s) = (a, s)
            Matrix<double> broadcastedvec_n = Matrix<double>.Build.DenseOfRows(Enumerable.Repeat(vec_n, a)); // (a, s)
            Matrix<double> viewWithvec_n = view.PointwiseMultiply(broadcastedvec_n); // (a, s) * (a, s) = (a, s)
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

            // Solve
            Vector<double> delta_x = J.Solve(-vec_F);

            // Apply Δx
            vec_lambda += delta_x.SubVector(0, a);
            vec_N += delta_x.SubVector(a, 3);

            // Clamp N_m
            for (int m = 0; m < 3; m++)
            {
                if (vec_N[m] < Constants.N_mMin)
                {
                    vec_N[m] = Constants.N_mMin;
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
        vec_mu = Vector<double>.Build.Dense(s);
        for (int j = 0; j < s; j++)
        {
            vec_mu[j] = viewSpecies[j].Getmu(T, P, 1.0);
        }
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

    private void SolveUT() // At constant U, guess T that conserves U, calls DeriveQuantities at each step
    {
        for (int UTStep = 0; UTStep < Constants.MaxUTSteps; UTStep++)
        {
            DeriveQuantities();
            double UError = (U - UTarget) / UTarget;
            if (Math.Abs(UError) < Constants.UTolerance)
            {
                break; // Consider the system solved
            }
            else
            {
                // If U is too high (positive UError), we need to increase T
                // Vice versa if U is too low (negative UError)
                T *= 1.0 + UError;
                // U depends non-linearly on T
                // dU = TdS - PdV, and dV = 0 in a sealed box
                // A better solution requires integration
                // Clamp T
                if (T < Constants.Tmin)
                {
                    T = Constants.Tmin;
                }
            }
        }
    }

    private void SolveVP() // At constant V, guess P that conserves V, calls DeriveQuantities at each step
    {
        for (int VPStep = 0; VPStep < Constants.MaxVPSteps; VPStep++)
        {
            DeriveQuantities();
            double VError = (vec_V.Sum() - Volume) / Volume;
            if (Math.Abs(VError) < Constants.VTolerance)
            {
                break; // Consider the system solved
            }
            else
            {
                // If V is too high (positive VError), we need to increase P
                // Vice versa if V is too low (negative VError)
                P *= 1.0 + VError;
                // V depends non-linearly on P (unless you have only ideal gases)
                // A better solution requires integration
                // Clamp P
                if (P < Constants.Pmin)
                {
                    P = Constants.Pmin;
                }
            }
        }
    }

    private void SolvePhases()
    {
        // At equilibrium, fugacity of each species phase is the same for all phases of that species
        foreach((Species species, List<SpeciesPhaseResource> resources) in speciesToResources)
        {
            // If there is only a single phase, skip
            if (resources.Count == 1)
            {
                continue;
            }
            double[] logphis = resources.Select(resource => resource.SpeciesPhase.EquationOfState.GetLogphi(T, P, resource.SpeciesPhase.EquationOfState.Getv(T, P))).ToArray();
            // Get the index of the phase with the lowest fugacity
            int indexOfMin = logphis.IndexOf(logphis.Min());
            // Move moles to the phase of lowest fugacity
            // Use the relative fugacity difference as a multiplier
            for (int indexOfSrc = 0; indexOfSrc < resources.Count; indexOfSrc++)
            {
                if (resources[indexOfSrc].SpeciesPhase.Phase == Phase.Solid
                    && resources[indexOfMin].SpeciesPhase.Phase == Phase.Solid)
                {
                    // solid -> solid is banned
                    // SolvePhases bypasses DissociationTemperature and diamond -> graphite shouldn't happen
                    continue;
                }
                if (indexOfSrc != indexOfMin)
                {
                    double ratio = (logphis[indexOfSrc] - logphis[indexOfMin]) / logphis[indexOfMin];
                    double n_moved = ratio * resources[indexOfSrc].n;
                    resources[indexOfSrc].n -= n_moved;
                    resources[indexOfMin].n += n_moved;
                }
            }
        }
    }

    private void Solve()
    {
        Dissociate();
        RebuildIndexes();
        SolveReactions();
        SolveUT();
        SolveVP();
        RebuildIndexes();
        SolvePhases();
    }

    // TODO: MaybeAdd and MaybeMerge
    // Volume is a thermodynamic simulation, so putting things in the box requires work
    // A full theory of pumps is needed before those methods can be implemented
}
