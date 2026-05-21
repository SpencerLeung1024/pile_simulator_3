using System;
using System.Collections.Generic;
using System.Linq;

// A volume represents something that contains matter
// The matter may be made up of numerous species, existing as gas, liquid, and solid phases
public class Volume : Inventory<SpeciesPhaseResource>
{
    // Resources: the species in phases in this volume, and their amounts in mol
    // Volume: m^3
    public double T; // K
    public double P; // Pa
    
    // Derived quantities:
    // Mass: kg
    // UsedVolume: m^3, slighly different meaning in Volume: the volume taken up by condensed phases only
    // FreeVolume: m^3, has a getter: Volume - UsedVolume
    private double U; // J, internal energy of the system
    // H = U + PV, enthalpy of the system
    // G = H - TS, Gibbs free energy of the system
    private ulong bitmask;
    private double[] vec_lambda;
    private double[] vec_n; // [n_gas, n_liquid, n_solid] (enum Phase order)

    protected override void DeriveQuantities()
    {
        Mass = 0.0;
        UsedVolume = 0.0;
        U = 0.0;
        foreach (SpeciesPhaseResource resource in Resources)
        {
            double n = resource.n;
            double v = resource.SpeciesPhase.EquationOfState.Getv(T, P);
            Mass += resource.SpeciesPhase.Species.MolarMass * n;
            if (resource.SpeciesPhase.Phase != Phase.Gas)
            {
                UsedVolume += v * n;
            }
            U += resource.SpeciesPhase.EquationOfState.GetU(T, v) * n;
        }
    }

    private double GetGasVolume()
    {
        double gasVolume = 0.0;
        foreach (SpeciesPhaseResource resource in Resources)
        {
            if (resource.SpeciesPhase.Phase == Phase.Gas)
            {
                double v = resource.SpeciesPhase.EquationOfState.Getv(T, P);
                gasVolume += v * resource.n;
            }
        }
        return gasVolume;
    }

    private Dictionary<Element, double> Dissociate()
    {
        Dictionary<Element, double> freeElements = new Dictionary<Element, double>();
        foreach (SpeciesPhaseResource resource in Resources)
        {
            Species species = resource.SpeciesPhase.Species;
            if (T > species.DissociationTemperature)
            {
                double k = Math.Exp(-species.DissociationActivationEnergy / (Constants.R * T)); // Arrhenius equation k = Ae^(-E_a / RT) with A = 1 / frame
                double n_dissociated = resource.n * k;
                resource.n -= n_dissociated;
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
        return freeElements;
    }

    private void SolveReactions()
    {
        
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

        List<Element> elementList = freeElements.Keys.ToList();
        ulong newBitmask = FormulaTable.GetViewBitmask(elementList);
        if (newBitmask != bitmask)
        {
            // Invalidate last frame's solution
            bitmask = newBitmask;
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

            SolveReactions();
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
