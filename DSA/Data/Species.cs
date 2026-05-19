using System;
using System.Collections.Generic;
using System.Linq;

public class Species
{
    public string Name; // "Water", "Carbon Dioxide", etc.
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
    public string Name; // "Graphite", "Diamond", etc. for solids, otherwise PhaseEnum

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
    public static List<Species> List = new List<Species>();
    public static Dictionary<string, Species> nameToSpecies = new Dictionary<string, Species>();

    public static Species ByName(string name)
    {
        if (!nameToSpecies.TryGetValue(name, out var species))
            throw new KeyNotFoundException($"Species '{name}' not found in AllSpecies");
        return species;
    }

    public static bool TryGetSpecies(string name, out Species species)
    {
        return nameToSpecies.TryGetValue(name, out species);
    }
}

public static class AllSpeciesPhases
{
    public static List<SpeciesPhase> List = new List<SpeciesPhase>();
    public static Dictionary<string, SpeciesPhase> nameToPhase = new Dictionary<string, SpeciesPhase>();

    public static SpeciesPhase ByName(string name)
    {
        if (!nameToPhase.TryGetValue(name, out var phase))
            throw new KeyNotFoundException($"SpeciesPhase '{name}' not found in AllSpeciesPhases");
        return phase;
    }

    public static bool TryGetPhase(string name, out SpeciesPhase phase)
    {
        return nameToPhase.TryGetValue(name, out phase);
    }

    public static List<SpeciesPhase> GetSubset(params string[] names)
    {
        var result = new List<SpeciesPhase>();
        foreach (string name in names)
        {
            if (nameToPhase.TryGetValue(name, out var phase))
                result.Add(phase);
            else if (AllSpecies.nameToSpecies.TryGetValue(name, out var species))
                result.AddRange(species.Phases);
            else
                throw new KeyNotFoundException($"Neither Species nor SpeciesPhase found for '{name}'");
        }
        return result;
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
        int s = AllSpecies.List.Count; // The PDF uses s = num species
        table = new uint[a, s];
        for (int j = 0; j < s; j++)
        {
            Species species = AllSpecies.List[j];
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
            viewSpecies = AllSpecies.List.ToArray();
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
                int table_s = AllSpecies.List.Count;
                List<Species> viewSpeciesList = new List<Species>();
                // We don't know how many species will be in the view, so use a list of lists to build it before converting to an array
                // But we *do* know how many elements will be in each row
                // So the *outer* structure needs to be a list, and it must be species
                List< uint[] > viewListTransposed = new List<uint[]>(); // viewListTransposed[species][element]
                
                // Go through every species
                for (int table_j = 0; table_j < table_s; table_j++)
                {
                    Species species = AllSpecies.List[table_j];
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
