using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

public class Species
{
    public string Name; // For maximum compatibility with our current data source (thermo.inp), this is just the raw entry with any phase information stripped
    // "H2O" for water, "CO2" for carbon dioxide, etc.
    // TODO: Add a separate dictionary for the "usual" name of a species, like "water" and "carbon dioxide"
    public Dictionary<Element, uint> Formula; // {H: 2, O: 1} for water
    public double omega; // acentric factor, used in Soave-Redlich-Kwong and Peng-Robinson equations of state
    public double DissociationTemperature; // K, unscientific, used in Pile Simulator 3's reaction simulation
    // Protects certain species like diamond which would spontaneously convert to graphite at standard conditions
    // Prevents things from combusting at standard conditions
    public List<SpeciesPhase> Phases;

    // Derived quantities
    //public double MolarMass { get { return Formula.Sum(kv => kv.Key.A_r * 1e-3 * kv.Value); } } // kg / mol
    public double MolarMass; // Doing a sum every time is expensive so determine this value at construction
    public double DissociationActivationEnergy; // J / mol, unscientific, used in an Arrhenius equation

    private void DeriveQuantities()
    {
        MolarMass = Formula.Sum(kv => kv.Key.A_r * 1e-3 * kv.Value);
        DissociationActivationEnergy = -Math.Log(Constants.DissociationThreshold) * Constants.R * DissociationTemperature;
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

    // After review, it turns out that "-μ_j / RT" in the Element Potential Method actually refers to the *pure species* chemical potential
    // x_j = 1 and there is no mixing
    // I have chosen to retain a single Getmu with x_j, but Volume.SolveReactions passes x_j = 1
    // This takes the path with no mixing term

    // …ようこそ。アヴェ μ_jィカの世界へ
    public double Getmu(double T, double P, double x_j)
    {
        // Pile Simulator 3 mostly follows the formulas for an ideal gas and ideal solution
        // mu_j = gibbs_j + RT ln(n_j / n_in_phase) + RT ln (P / standard_P)
        // x_j = n_j / n_in_phase, the mole fraction
        // mu_j = gibbs_term + mixing_term + pressure_term
        // Only gases have the pressure term

        // But, we don't use partial_pressure_j
        // We use fugacity coefficient phi_j * partial_pressure_j
        // The fugacity coefficient is a non-linearity from cubic equations of state
        // mu no longer represents an ideal solution, but we can get cooler effects

        // G = H - TS
        double gibbs_term = HeatCapacityFunction.GetH(T) - T * HeatCapacityFunction.GetS(T);
        double mixing_term = 0.0;
        if (x_j < 1.0)
        {
            mixing_term = Constants.R * T * Math.Log(x_j);
        }
        double pressure_term = 0.0;
        if (Phase == Phase.Gas)
        {
            double v = EquationOfState.Getv(T, P);
            double phi = Math.Exp(EquationOfState.GetLogphi(T, P, v));
            pressure_term = Constants.R * T * Math.Log(phi * P / Constants.bar);
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
    public double n; // mol
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
        string path = "Data/thermo.inp";
        // subset is a list of baseName
        // See GetBaseName and BuildSpecies in NASA9Loader for details
        List<string> subset = new List<string>()
        {
            "H2", // "H2" diatomic hydrogen (line 5682), "H2(L)" liquid hydrogen (line 15625). Will not include "H" monoatomic hydrogen (line 5372)
            "C", // "C" gaseous carbon (line 1954), "C(gr)" graphite (line 15466)
            "O2", // "O2" diatomic oxygen (line 8018), "O2(L)" liquid oxygen (line 15766)
            "CH4", // "CH4" methane (line 2521), "CH4(L)" liquid methane (line 15506)
            "H2O", // "H2O" gaseous water (line 5755), "H2O(L)" liquid water (line 12481), "H2O(cr)" ice (line 12476)
            "CO2", // "CO2" carbon dioxide (line 2701)

            // Note that I have left out "CO" carbon monoxide (line 2623). At high temperatures, CO is a significant part of the system
            // We can always add that later, or use subset = null to load everything

            // Oh, I think I know why the solver breaks
            // Between C(gr) and H2O(cr), H2O(cr) is far better preferred
            // Meanwhile, H2O(L) is the only liquid species
            // In a system with effectively zero C(gr), liquid and solid are both entirely H2O, J is near singular, and the solver breaks

            // Add a substance that is know to prefer a liquid phase, and has no solid phase in the dataset
            "C2H5OH", // "C2H5OH" gaseous ethanol (line 3153), "C2H5OH(L)" liquid ethanol (line 15532)

            // Nope, J didn't budge. H2O > C2H5OH
            // Maybe try something to suck up the oxygen into a solid
            //"Fe", // "Fe" gaseous iron (line 4844), "Fe(L)" liquid iron (line 12206), "Fe(a)" below Lambda, a above Lambda, c, d, (line 12180 - 12201)
            //"Fe2O3" // "Fe2O3(cr)" hematite (line 12306 and 12314), below and above Curie
            // There's some weirdness with how Fe and Fe2O3 are loaded. I can't reliably get their species phases, and elemental iron doesn't show up in free elements

            // Next solution: modify thermo.inp to ban H2O(cr)
            // Line 12476 now refers to oxygen dihydride (OH2(cr)), which totally exists
        };
        NASA9Loader.Load(path, subset);

        // But thermo.inp does not have critical temperature, pressure, or molar volume
        // It assumes everything is an ideal gas and condensed phases have zero volume
        // We need to make up an equation of state for all phases
        AllSpeciesPhases.MakeUpEquationsOfState();

        // We also need to give every species a dissociation temperature
        // Placeholder: 600 K (327 C) for everything
        foreach (Species species in list)
        {
            species.DissociationTemperature = 600.0;
            // *Someone* made DeriveQuantities private so we have to manually set DissociationActivationEnergy
            species.DissociationActivationEnergy = -Math.Log(Constants.DissociationThreshold) * Constants.R * species.DissociationTemperature;
        }

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

    public static void MakeUpEquationsOfState()
    {
        foreach(SpeciesPhase speciesPhase in list)
        {
            if (speciesPhase.Phase == Phase.Gas)
            {
                speciesPhase.EquationOfState = new IdealGasEquation()
                {
                    SpeciesPhase = speciesPhase
                };
            }
            else
            {
                speciesPhase.EquationOfState = new IncompressiblePhaseEquation()
                {
                    SpeciesPhase = speciesPhase,
                    v = 0.0
                };
            }
        }
    }

    public static SpeciesPhase ByName(string name)
    {
        return nameToPhase[name];
    }
}

public static class FormulaTable
{
    // table[element, species] = n_ij as seen in the STANJAN PDF, where i = element and j = species
    //public static uint[,] table = new uint[,]();
    //public static uint[,] table; // We don't know how many species and elements there will be
    public static Matrix<double> table; // Use Math.NET Numerics so we can do matrix vector operations

    // Create a view of the table containing only certain elements and species using those elements
    // ulong is a bitmask of elements
    // The tuple contains:
    // - the elements, as used in the view's rows
    // - the species, as used in the view's columns
    // - the view itself, as a matrix
    // See GetViewBitmask and GetView below
    public static Dictionary<ulong, (Element[], SpeciesPhase[], Matrix<double>)> viewCache = new Dictionary<ulong, (Element[], SpeciesPhase[], Matrix<double>)>();
    private static readonly object viewCacheLock = new object(); // Prevent multiple threads from trying to make a new entry and put it in viewCache

    // Pulls data from Elements, AllSpecies and AllSpeciesPhases. Make sure those are fully filled in before calling this
    public static void Initialize()
    {
        int a = Elements.list.Length; // The PDF uses a = num elements
        int s = AllSpeciesPhases.list.Count; // The PDF uses s = num species
        table = Matrix<double>.Build.Dense(a, s); // All cells of the matrix will be initialized to zero
        // n_ij only has values of uint, but operations with mole fractions require <double> and <double>
        for (int j = 0; j < s; j++)
        {
            SpeciesPhase speciesPhase = AllSpeciesPhases.list[j];
            Species species = speciesPhase.Species;
            foreach ((Element element, uint count) in species.Formula)
            {
                int i = (int)element.Z - 1; // hydrogen.Z = 1 but i should be 0
                table[i, j] = (double)count;
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
            int i = (int)element.Z - 1;
            if (i >= 64)
            {
                // Can't represent this element in the bitmask, so return 0 to indicate that the full table should be used
                return 0;
            }
            else
            {
                bitmask |= (1UL << i);
            }
        }
        return bitmask;
    }

    public static void GetView(ulong bitmask, out Element[] viewElements, out SpeciesPhase[] viewSpecies, out Matrix<double> view)
    {
        if (bitmask == 0)
        {
            // Can't represent this view in the bitmask, so return the full table
            viewElements = Elements.list;
            viewSpecies = AllSpeciesPhases.list.ToArray();
            view = table;
        }
        else
        {
            viewCache.TryGetValue(bitmask, out (Element[], SpeciesPhase[], Matrix<double>) cachedView);
            if (cachedView != default)
            {
                viewElements = cachedView.Item1;
                viewSpecies = cachedView.Item2;
                view = cachedView.Item3;
            }
            else
            {
                // Create the view and cache it
                lock (viewCacheLock)
                {
                    List<Element> viewElementsList = new List<Element>();
                    List<SpeciesPhase> viewSpeciesList = new List<SpeciesPhase>();
                    // Fill these in as we discover i and j below

                    // "Only for contiguous ranges. Math.NET's SubMatrix(rowStart, rowCount, colStart, colCount) creates a view over contiguous rows/columns. For the arbitrary non-contiguous selection you need (bitmask-based), there's no built-in view." - DeepSeek V4 Pro

                    // Math.NET DenseMatrix uses column-major storage (Fortran order).
                    // If I really wanted to be fast I would transpose the table before getting element rows, but it probably doesn't matter in the end
                    // table and view should remain [i, j] though. The heavy work in Volume.SolveReactions hits different i of the same j more often than different j of the same i, so column-major is better overall

                    // Our algorithm will be:
                    // 1. Keep only rows (elements) that are in the bitmask
                    // 2. Keep only columns (species) that have non-zero counts

                    // 1.
                    List< Vector<double> > viewRowsList = new List< Vector<double> >(); // viewRowsList[i] = row i of the view, as a vector
                    for (int table_i = 0; table_i < 64; table_i++)
                    {
                        // Get this element's bit from the bitmask
                        if ((bitmask & (1UL << table_i)) != 0)
                        {
                            viewElementsList.Add(Elements.list[table_i]);
                            viewRowsList.Add(table.Row(table_i));
                        }
                    }

                    Matrix<double> tempMatrix = Matrix<double>.Build.DenseOfRowVectors(viewRowsList);

                    // 2.
                    List< Vector<double> > viewColsList = new List< Vector<double> >(); // viewColsList[j] = column j of the view, as a vector
                    for (int table_j = 0; table_j < AllSpeciesPhases.list.Count; table_j++)
                    {
                        Vector<double> col = tempMatrix.Column(table_j);
                        // "Sum() is a native Math.NET method, SIMD-accelerated, and avoids LINQ delegate overhead."
                        // IEnumerable.Any has early exit but the overhead probably makes it worse in this case
                        if (col.Sum() > 0.0) // double Vector<double>.Sum()
                        //if (col.Max() > 0.0) // (extension) double IEnumerable<double>.Max()
                        //if (col.Any(value => value > 0.0)) // (extension) bool IEnumerable<double>.Any<double>(Func<double, bool> predicate)
                        {
                            viewSpeciesList.Add(AllSpeciesPhases.list[table_j]);
                            viewColsList.Add(col);
                        }
                    }

                    // Build the view
                    view = Matrix<double>.Build.DenseOfColumnVectors(viewColsList);

                    // Fix the lists as arrays
                    viewElements = viewElementsList.ToArray();
                    viewSpecies = viewSpeciesList.ToArray();

                    // Cache the view
                    viewCache[bitmask] = (viewElements, viewSpecies, view);
                } // End lock
            }
        }
    }
}
