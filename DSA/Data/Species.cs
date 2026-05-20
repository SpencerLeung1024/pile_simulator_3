using System;
using System.Collections.Generic;
using System.Linq;

public class Species
{
    public string Name; // For maximum compatibility with our current data source (thermo.inp), this is just the raw entry with any phase information stripped
    // "H2O" for water, "CO2" for carbon dioxide, etc.
    // TODO: Add a separate dictionary for the "usual" name of a species, like "water" and "carbon dioxide"
    public Dictionary<Element, uint> Formula; // {H: 2, O: 1} for water
    public double omega; // acentric factor, used in Soave-Redlich-Kwong and Peng-Robinson equations of state
    public List<SpeciesPhase> Phases;

    // Derived quantities
    //public double MolarMass { get { return Formula.Sum(kv => kv.Key.A_r * kv.Value); } } // kg / mol
    public double MolarMass; // Doing a sum every time is expensive so determine this value at construction

    private void DeriveQuantities()
    {
        MolarMass = Formula.Sum(kv => kv.Key.A_r * kv.Value);
    }

    // Private the default constructor to prohibit creating species without calculating derived quantities
    private Species() {}

    // Constructor
    public Species(string Name, Dictionary<Element, uint> Formula, double omega, List<SpeciesPhase> Phases)
    {
        this.Name = Name;
        this.Formula = Formula;
        this.omega = omega;
        this.Phases = Phases;
        DeriveQuantities();
    }
}

public enum Phase
{
    Gas, // 0, supercritical fluid also uses this
    Liquid, // 1
    Solid // 2, does not fully specify the phase since allotropes exist
}

public class SpeciesPhase
{
    public Species Species;
    public Phase Phase;
    public string Name; // "graphite", "diamond", etc. for solids, otherwise PhaseEnum
    // UI should show contents of a Volume as:
    // - "H2O (gas)" for gaseous water
    // - "H2O (liquid)" for liquid water
    // - "H2O (ice)" for solid water ("ice" comes from parsing thermo.inp)
    // - "C (gas)" for gaseous carbon
    // - "C (graphite)" for graphite

    /*
    public float StandardEnthalpyOfFormation; // J / mol, gas phase, at whatever the standard conditions are
    public float StandardEntropy; // J / (K * mol), gas phase, at whatever the standard conditions are
    public float StandardTemperature; // K, used to know where the standard enthalpy of formation and standard entropy are measured
    public float StandardPressure; // Pa, used similarly
    */
    // All three heat capacity functions: NASA7, NASA9, and Shomate, are forms in which standard enthalpy of formation and standard entropy are already included
    // NASA9.GetH(T) *is* molar enthalpy at temperature T (well after I multiply by RT but my code does that)
    // etc.
    // ConstantHeatCapacityFunction still needs StandardEnthalpyOfFormation, StandardEntropy, and StandardTemperature though

    public HeatCapacityFunction HeatCapacityFunction;
    // Provides:
    /*
    - c_p(T), J / (K * mol), molar heat capacity at constant pressure
    - H(T), J / mol, molar enthalpy
    - S(T), J / (K * mol), molar entropy
    */
    public EquationOfState EquationOfState;
    // Provides:
    /*
    - U(T, v), J / mol, molar internal energy
    - P(T, v), Pa, pressure
    - v(T, P), m^3 / mol, molar volume
    */

    // Chemical potential, μ, is what is minimized in a system
    // It has units of J / mol
    // How much energy do you get from increasing the system's amount of this species by 1 mol?
    public double Getmu(double T, double P, double n_j, double n_in_phase, double V_gas)
    {
        // Pile Simulator 3 mostly follows the formulas for an ideal gas and ideal solution
        // mu_j = gibbs_j + RT ln(n_j / n_in_phase) + RT ln (partial_pressure_j / standard_pressure)
        // x_j = n_j / n_in_phase, the mole fraction
        // mu_j = gibbs_term + mixing_term + pressure_term
        // Only gases have the pressure term

        // But, we don't use partial_pressure_j
        // We use fugacity coefficient phi_j * partial_pressure_j
        // The fugacity coefficient is a non-linearity from cubic equations of state
        // mu no longer represents an ideal solution, but we can get cooler effects

        // molar volume
        double v_gas = V_gas / n_j;
        // G = H - TS
        double gibbs_term = HeatCapacityFunction.GetH(T) - T * HeatCapacityFunction.GetS(T);
        double mixing_term = Constants.R * T * Math.Log(n_j / n_in_phase);
        double phi = Math.Exp(EquationOfState.GetLogphi(T, P, v_gas));
        // partial_pressure_j here is calculated with the ideal gas law
        double partial_pressure_j = Constants.R * T / v_gas;
        double pressure_term = 0.0;
        if (Phase == Phase.Gas)
        {
            pressure_term = Constants.R * T * Math.Log(phi * partial_pressure_j / Constants.bar);
        }
        return gibbs_term + mixing_term + pressure_term;
    }
}

// An amount of a species in a phase
// Originally just called "Resource". It turns out that Godot already has a "Resource". Also I need to disambiguate between species phase resources and nuclide resources
public class SpeciesPhaseResource
{
    //public Species Species;
    //public Phase Phase;
    // Normalize. SpeciesPhase contains both Species and Phase, so don't store them separately (avoid potential for inconsistency)
    public SpeciesPhase SpeciesPhase;
    public float n; // mol
    // These are all the fields of Resource. It has no methods. All chemistry logic is handled by Volume
}

public static class AllSpecies
{
    public static List<Species> list = new List<Species>();
    public static Dictionary<string, Species> nameToSpecies = new Dictionary<string, Species>();

    private static void BuildIndexes()
    {
        foreach (Species species in list)
        {
            nameToSpecies[species.Name] = species;
        }
    }

    // Remember to call this somewhere when the game starts
    public static void Initialize()
    {
        // 2026-05-19: Our only data source is the NASA Glenn Coefficients at `DSA/Data/thermo.inp`
        string path = "DSA/Data/thermo.inp";
        // subset is a list of baseName
        // See GetBaseName and BuildSpecies in NASA9Loader for details
        List<string> subset = new List<string>()
        {
            "H2", // "H2" diatomic hydrogen (line 5682), "H2(L)" liquid hydrogen (line 15625). Will not include "H" monoatomic hydrogen (line 5372)
            "C", // "C" gaseous carbon (line 1954), "C(gr)" graphite (line 15466)
            "O2", // "O2" diatomic oxygen (line 8018), "O2(L)" liquid oxygen (line 15766)
            "CH4", // "CH4" methane (line 2521), "CH4(L)" liquid methane (line 15506)
            "H2O", // "H2O" gaseous water (line 5755), "H2O(L)" liquid water (line 12481), "H2O(cr)" ice (line 12476)
            "CO2" // "CO2" carbon dioxide (line 2701)
            // Note that I have left out "CO" carbon monoxide (line 2623). At high temperatures, CO is a significant part of the system
            // We can always add that later, or use subset = null to load everything
        };
        NASA9Loader.Load(path, subset);
        BuildIndexes();
    }

    public static Species ByName(string name)
    {
        return nameToSpecies[name];
    }
}

public static class AllSpeciesPhases
{
    public static List<SpeciesPhase> list = new List<SpeciesPhase>();
    public static Dictionary<string, SpeciesPhase> nameToPhase = new Dictionary<string, SpeciesPhase>();

    // nameToPhase is built by NASA9Loader
    // It contains the raw names from thermo.inp, like "H2O(L)", "C(gr)", etc.
    // There is a rationale behind it, but it is not the same as Pile Simulator 3

    public static SpeciesPhase ByName(string name)
    {
        return nameToPhase[name];
    }
}

public static class FormulaTable
{
    // table[element, species] = n_ij as seen in the STANJAN PDF, where i = element and j = species
    //public static uint[,] table = new uint[,]();
    public static uint[,] table; // We don't know how many species and elements there will be

    // Create a view of the table containing only certain elements and species using those elements
    // ulong is a bitmask of elements
    // The tuple contains:
    // - the elements, as used in the view's rows
    // - the species, as used in the view's columns
    // - the view itself, as a 2D array of uints
    // See GetViewBitmask and GetView below
    public static Dictionary<ulong, (Element[], Species[], uint[,])> viewCache = new Dictionary<ulong, (Element[], Species[], uint[,])>();

    // Pulls data from Elements, AllSpecies and AllSpeciesPhases. Make sure those are fully filled in before calling this
    public static void Initialize()
    {
        int a = Elements.list.Length; // The PDF uses a = num elements
        int s = AllSpecies.list.Count; // The PDF uses s = num species
        table = new uint[a, s];
        for (int j = 0; j < s; j++)
        {
            Species species = AllSpecies.list[j];
            for (int i = 0; i < a; i++)
            {
                Element element = Elements.list[i];
                if (species.Formula.ContainsKey(element))
                {
                    table[i, j] = species.Formula[element];
                }
                else
                {
                    table[i, j] = 0;
                }
            }
        }
    }

    public static ulong GetViewBitmask(List<Element> elements)
    {
        // Create a bitmask of the elements in the view
        // The bitmask is a ulong, so it can only represent up to 64 elements
        // Will return ulong 0 if terbium (Z=65) or anything above is included, in which case you must use the full table
        // If only there were an Int128...
        ulong bitmask = 0;
        foreach (Element element in elements)
        {
            int index = (int)element.Z - 1; // Z starts at 1 for hydrogen, but bitmask starts at 0, so subtract 1
            if (index >= 64)
            {
                // Can't represent this element in the bitmask, so return 0 to indicate that the full table should be used
                return 0;
            }
            else
            {
                bitmask |= (1UL << index);
            }
        }
        return bitmask;
    }

    // Transforms the formula from a dictionary to a uint array in the order expected by a view
    public static uint[] GetElementCountsFromFormula(Element[] viewElements, Dictionary<Element, uint> formula)
    {
        int view_a = viewElements.Length;
        uint[] elementCounts = new uint[view_a];
        // Initialize to 0
        for (int view_i = 0; view_i < view_a; view_i++)
        {
            elementCounts[view_i] = 0;
        }
        foreach (KeyValuePair<Element, uint> kv in formula)
        {
            Element element = kv.Key;
            uint count = kv.Value;
            int view_i = viewElements.IndexOf(element);
            if (view_i != -1)
            {
                elementCounts[view_i] = count;
            }
            // Otherwise this element is not in the view, so it should be ignored
        }
        return elementCounts;
    }

    public static void GetView(ulong bitmask, out Element[] viewElements, out Species[] viewSpecies, out uint[,] view)
    {
        if (bitmask == 0)
        {
            // Can't represent this view in the bitmask, so return the full table
            viewElements = Elements.list;
            viewSpecies = AllSpecies.list.ToArray();
            view = table;
        }
        else
        {
            viewCache.TryGetValue(bitmask, out (Element[], Species[], uint[,]) cachedView);
            if (cachedView != default)
            {
                viewElements = cachedView.Item1;
                viewSpecies = cachedView.Item2;
                view = cachedView.Item3;
            }
            else
            {
                // Create the view and cache it

                // Build the view's list of elements
                // Note that view[0] may be different from Elements.list[0]
                // If there is no hydrogen (bitmask & (1UL << 0) == 0), then view[0] will be the first element for which the bitmask has a bit, which may be helium (Z=2), lithium (Z=3), etc.
                // This lets us operate on smaller tables and matrices
                List<Element> viewElementsList = new List<Element>();
                for (int table_i = 0; table_i < 64; table_i++)
                {
                    // Get this element's bit from the bitmask
                    if ((bitmask & (1UL << table_i)) != 0)
                    {
                        viewElementsList.Add(Elements.list[table_i]);
                    }
                }
                // Fix as array
                viewElements = viewElementsList.ToArray();
                
                int table_a = Elements.list.Length;
                int table_s = AllSpecies.list.Count;
                List<Species> viewSpeciesList = new List<Species>();
                // We don't know how many species will be in the view, so use a list before converting to an array
                // But we *do* know how many elements the view wants
                // So the *outer* structure needs to be a list, and it must be species
                List< uint[] > viewListTransposed = new List<uint[]>(); // viewListTransposed[species][element]
                
                // Go through every species
                for (int table_j = 0; table_j < table_s; table_j++)
                {
                    Species species = AllSpecies.list[table_j];
                    Dictionary<Element, uint> formula = species.Formula;
                    uint[] elementCounts = GetElementCountsFromFormula(viewElements, formula);
                    // Only add if any relevant elements are non-zero
                    if (elementCounts.Any(count => count > 0))
                    {
                        viewSpeciesList.Add(species);
                        viewListTransposed.Add(elementCounts);
                    }
                }
                // Fix as array
                viewSpecies = viewSpeciesList.ToArray();
                
                // Convert the viewListTransposed to a 2D array
                int view_s = viewListTransposed.Count;
                int view_a = viewElements.Length;
                view = new uint[view_a, view_s];
                for (int view_i = 0; view_i < view_a; view_i++)
                {
                    for (int view_j = 0; view_j < view_s; view_j++)
                    {
                        view[view_i, view_j] = viewListTransposed[view_j][view_i];
                    }
                }
                
                // Put in the cache
                viewCache[bitmask] = (viewElements, viewSpecies, view);
            }
        }
    }
}
