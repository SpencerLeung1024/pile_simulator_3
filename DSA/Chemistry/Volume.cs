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
    private double[] vec_lambda;
    private double[] vec_N = new double[3]; // [N_gas, N_liquid, N_solid] (enum Phase order)
    private double[] vec_V = new double[3]; // [V_gas, V_liquid, V_solid]

    protected override void DeriveQuantities()
    {
        // Mass, vec_V, UsedVolume, U, S
        Mass = 0.0;
        for (int m = 0; m < 3; m++)
        {
            vec_V[m] = 0.0;
        }
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
        UsedVolume = vec_V[(int)Phase.Liquid] + vec_V[(int)Phase.Solid];
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
            vec_lambda = new double[existingElements.Count];
            for (int i = 0; i < vec_lambda.Length; i++)
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

        Element[] viewElements; // i = an element, a = num elements, elements are in order of increasing Z
        SpeciesPhase[] viewSpecies; // j = a species, s = num species, unordered, but consistent with the order of their addition to AllSpeciesPhases
        uint[,] view; // view[i, j] = n_ij = count of element i in species j
        FormulaTable.GetView(bitmask, out viewElements, out viewSpecies, out view);
        int a = viewElements.Length;
        int s = viewSpecies.Length;
    }

    private void SolveReactions(Dictionary<Element, double> freeElements)
    {
        Element[] viewElements; // i = an element, a = num elements, elements are in order of increasing Z
        SpeciesPhase[] viewSpecies; // j = a species, s = num species, unordered, but consistent with the order of their addition to AllSpeciesPhases
        Matrix<double> view; // view[i, j] = n_ij = count of element i in species j
        FormulaTable.GetView(bitmask, out viewElements, out viewSpecies, out view);
        int a = viewElements.Length;
        int s = viewSpecies.Length;

        // Turn the free elements dict into a vector
        double[] vec_p = new double[a]; // p_i = mol of element i available for reactions
        for (int i = 0; i < a; i++)
        {
            Element element = viewElements[i];
            freeElements.TryGetValue(element, out double p_i);
            if (p_i != default)
            {
                vec_p[i] = p_i;
            }
            else
            {
                vec_p[i] = 0.0;
            }
        }

        // Include any existing moles of each species when calculating chemical potential
        double[] vec_moles_existing = new double[s];
        foreach (SpeciesPhaseResource resource in Resources)
        {
            int j = Array.IndexOf(viewSpecies, resource.SpeciesPhase);
            if (j != -1)
            {
                vec_moles_existing[j] = resource.n;
            }
        }

        double[] vec_x = new double[s]; // x_j = mole fraction of species j in its phase
        int[] speciesToPhase = new int[s];
        for (int j = 0; j < s; j++)
        {
            int phase = (int)viewSpecies[j].Phase;
            speciesToPhase[j] = phase;
            double mu_j = viewSpecies[j].Getmu(T, P, vec_moles_existing[j] / vec_n[phase]);
            double sum = 0.0;
            for (int i = 0; i < a; i++)
            {
                sum += vec_lambda[i] * view[i,j];
            }
            // (2.9) in the STANJAN PDF
            vec_x[j] = Math.Exp((-mu_j / (Constants.R * T) + sum));
        }

        // J @ Δx = -F
        // Ax = b

        // Build F
        double[] vec_F = new double[a + 3];
        // Calculate element balance residuals
        for (int i = 0; i < a; i++)
        {
            double H_i = 0.0;
            for (int j = 0; j < s; j++)
            {
                double N_j = vec_n[speciesToPhase[j]]; // Total moles in phase m
                uint n_ij = view[i, j]; // Count of element i in species j
                double x_j = vec_x[j]; // Mole fraction of species j in its phase
                H_i += N_j * n_ij * x_j;
            }
            double p_i = vec_p[i]; // Moles of element i available for reactions
            H_i -= p_i;
            vec_F[i] = H_i;
        }
        // Calculate phase normalization residuals
        double[] vec_Z = new double[3];
        for (int j = 0; j < s; j++)
        {
            int phase = speciesToPhase[j];
            vec_Z[phase] += vec_x[j];
        }
        for (int m = 0; m < 3; m++)
        {
            vec_F[a + m] = vec_Z[m] - 1.0;
        }

        // Build J
        // Refer to `docs/chemistry/dual_problem/gpt_5_5.md`
        double[,] J = new double[a + 3, a + 3];
        // Build quadrant Q
        for (int i = 0; i < a; i++)
        {
            for (int k = 0; k < a; k++) // k is another element
            {
                double Q_ik = 0.0;
                for (int j = 0; j < s; j++)
                {
                    double N_m = vec_n[speciesToPhase[j]];
                    uint n_ij = view[i, j];
                    uint n_kj = view[k, j];
                    double x_j = vec_x[j];
                    Q_ik += N_m * n_ij * n_kj * x_j;
                }
                J[i, k] = Q_ik;
            }
        }
        // Build quadrant D
        for (int i = 0; i < a; i++)
        {
            double[] vec_D_i = new double[3]; // This is a row vector
            // It's faster if we go through all j once, incrementing D_ij correspondingly
            for (int j = 0; j < s; j++)
            {
                int m = speciesToPhase[j];
                uint n_ij = view[i, j];
                double x_j = vec_x[j];
                vec_D_i[m] += n_ij * x_j;
            }
            for (int m = 0; m < 3; m++)
            {
                J[i, a + m] = vec_D_i[m];
                // Also build quadrant D.T
                J[a + m, i] = vec_D_i[m];
            }
        }
        // Build quadrant 0
        for (int m1 = 0; m1 < 3; m1++)
        {
            for (int m2 = 0; m2 < 3; m2++)
            {
                J[m1, m2] = 0.0;
            }
        }

        // Build Δx
        double[] delta_x = new double[a + 3];

        // Solve
        // TODO

        // Apply Δx
        for (int i = 0; i < a; i++)
        {
            vec_lambda[i] += delta_x[i];
        }
        for (int m = 0; m < 3; m++)
        {
            vec_n[m] += delta_x[a + m];
        }

        // Recalculate x_j
        for (int j = 0; j < s; j++)
        {
            int phase = speciesToPhase[j];
            double mu_j = viewSpecies[j].Getmu(T, P, vec_moles_existing[j] / vec_n[phase]);
            double sum = 0.0;
            for (int i = 0; i < a; i++)
            {
                sum += vec_lambda[i] * view[i,j];
            }
            vec_x[j] = Math.Exp((-mu_j / (Constants.R * T) + sum));
        }

        // Restore moles of each species
        for (int j = 0; j < s; j++)
        {
            int phase = speciesToPhase[j];
            double N_m = vec_n[phase];
            double x_j = vec_x[j];
            double n_j = N_m * x_j;
            // Figure out which resource this species corresponds to
            SpeciesPhase speciesPhase = viewSpecies[j];
            foreach (SpeciesPhaseResource resource in Resources.Where(r => r.SpeciesPhase == speciesPhase)) // Should run 0 or 1 times
            {
                resource.n = n_j;
            }
        }
    }

    private void SolvePhases()
    {
        // At equilibrium, fugacity of each species phase is the same for all phases of that species
        foreach(var speciesGroup in Resources.GroupBy(resource => resource.SpeciesPhase.Species))
        {
            // If there is only a single phase, skip
            if (speciesGroup.Count() == 1)
            {
                continue;
            }
            // interface System.Linq.IGrouping is cool but hard to understand, so fix as array
            SpeciesPhaseResource[] phases = speciesGroup.ToArray();
            double[] logphis = phases.Select(phase => phase.SpeciesPhase.EquationOfState.GetLogphi(T, P, phase.SpeciesPhase.EquationOfState.Getv(T, P))).ToArray();
            // Get the index of the phase with the lowest fugacity
            int indexOfMin = logphis.IndexOf(logphis.Min());
            // Move moles to the phase of lowest fugacity
            // Use the relative fugacity difference as a multiplier
            for (int indexOfSrc = 0; indexOfSrc < phases.Length; indexOfSrc++)
            {
                if (indexOfSrc != indexOfMin)
                {
                    double ratio = (logphis[indexOfSrc] - logphis[indexOfMin]) / logphis[indexOfMin];
                    double n_moved = ratio * phases[indexOfSrc].n;
                    phases[indexOfSrc].n -= n_moved;
                    phases[indexOfMin].n += n_moved;
                }
            }
        }
    }

    private void ApplyHeat(double deltaU)
    {
        // https://en.wikipedia.org/wiki/Fundamental_thermodynamic_relation
        // dU = TdS - PdV

        // https://en.wikipedia.org/wiki/First_law_of_thermodynamics
        // ΔU = Q - W
        // W = PΔV
        // The system is constant volume, so ΔV = 0, so W = 0
        // All Q is dumped into ΔU
        // All dU goes into TdS

        // The problem is that entropy S changes with temperature T
        // The final temperature requires integrating
    }

    public void Solve()
    {
        double MassEntry = Mass;
        double UEntry = U;

        Dictionary<Element, double> freeElements = Dissociate();

        List<Element> elementList = freeElements.Keys.ToList(); // Unordered
        ulong newBitmask = FormulaTable.GetViewBitmask(elementList); // ulong is ordered from gadolinium (bit 63) to hydrogen (bit 0)
        if (newBitmask != bitmask)
        {
            // Invalidate last frame's solution
            bitmask = newBitmask;
            vec_lambda = new double[elementList.Count];
            for (int i = 0; i < vec_lambda.Length; i++)
            {
                vec_lambda[i] = 0.0; // Restart with all elements having zero element potential
            }
            for (int p = 0; p < vec_n.Length; p++)
            {
                vec_n[p] = 42.0; // Restart with all phases having 42 moles
            }
        }

        for (uint step = 0; step < Constants.MaxSteps; step++)
        {
            double Ustart = U;

            SolveReactions(freeElements);
            SolvePhases();
            DeriveQuantities();

            double Uend = U;
            ApplyHeat(Ustart - Uend); // Conservation of energy
            
            // UsedVolume is the volume of condensed phases: liquid and solid
            // Calculate volume taken up by gases at pressure P
            double gasVolume = GetGasVolume();
            double newVolume = UsedVolume + gasVolume;
            // If new volume is too big, increase pressure, and vice versa
            P *= (newVolume - Volume) / Volume;
            if (Math.Abs(Mass - MassEntry) / MassEntry < Constants.ConservationOfMassTolerance)
            {
                break; // Consider the system solved
            }
        }

        double MassExit = Mass;
        double UExit = U;

        // Import GD.Print to display the error
    }

    // TODO: MaybeAdd and MaybeMerge
    // Volume is a thermodynamic simulation, so putting things in the box requires work
    // A full theory of pumps is needed before those methods can be implemented
}
