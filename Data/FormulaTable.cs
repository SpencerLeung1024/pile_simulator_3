using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;

// Refactored out of Species.cs because this does a lot of processing
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

    // Tables filtered by phase. Right now only used by the interim Volume.SolveReactionsGas
    public static SpeciesPhase[] gasSpecies; // The species in the gas table, in the same order as the columns of gasTable
    public static Matrix<double> gasTable; // Same format as table, but only for gas species
    public static Dictionary<ulong, (Element[], SpeciesPhase[], Matrix<double>)> gasViewCache = new Dictionary<ulong, (Element[], SpeciesPhase[], Matrix<double>)>();
    private static readonly object gasViewCacheLock = new object();

    // Pulls data from Elements, AllSpecies and AllSpeciesPhases. Make sure those are fully filled in before calling this
    public static void Initialize()
    {
        int a = Elements.list.Length; // The PDF uses a = num elements
        int s = AllSpeciesPhases.list.Count; // The PDF uses s = num species
        table = Matrix<double>.Build.Dense(a, s); // All cells of the matrix will be initialized to zero
        List<SpeciesPhase> gasSpeciesList = new List<SpeciesPhase>();
        List< Vector<double> > gasTableCols = new List< Vector<double> >();
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
            if (speciesPhase.Phase == Phase.Gas)
            {
                gasSpeciesList.Add(speciesPhase);
                gasTableCols.Add(table.Column(j));
            }
        }
        gasSpecies = gasSpeciesList.ToArray();
        gasTable = Matrix<double>.Build.DenseOfColumnVectors(gasTableCols);
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

                    // Our algorithm, starting from table (table_a, table_s):
                    // 1. Get j (species indices) that contain:
                    //   a. At least one element in the bitmask (no empty columns)
                    //   b. No elements outside the bitmask (no spurious heavy metal oxides)
                    // 2. Create a tempMatrix (table_a, view_s) with only those columns
                    // 3. Throw out rows (elements) that are not in the bitmask
                    // 4. Make the view (view_a, view_s)

                    // 0. Filter tools
                    ulong notBitmask = ~bitmask;
                    Vector<double> vec_bitmask = Vector<double>.Build.Dense(Elements.list.Length); // Needs to be the length of all elements so PointwiseMultiply works, but 65+ will be zero
                    Vector<double> vec_notBitmask = Vector<double>.Build.Dense(Elements.list.Length);
                    for (int i = 0; i < 64; i++)
                    {
                        bool elementInBitmask = (bitmask & (1UL << i)) != 0;
                        vec_bitmask[i] = elementInBitmask ? 1.0 : 0.0;
                        vec_notBitmask[i] = !elementInBitmask ? 1.0 : 0.0;
                        if (elementInBitmask)
                        {
                            viewElementsList.Add(Elements.list[i]);
                        }
                    }
                    for (int i = 64; i < Elements.list.Length; i++)
                    {
                        // These elements can't be in the bitmask, so they go in notBitmask
                        // Eliminates Ta (73), W (74), Pb (82), U (92) oxides from simple element scenarios
                        vec_notBitmask[i] = 1.0;
                    }

                    // 1.
                    List<int> jList = new List<int>();
                    List< Vector<double> > tempMatrixCols = new List< Vector<double> >(); // We will need this later
                    for (int table_j = 0; table_j < table.ColumnCount; table_j++)
                    {
                        Vector<double> col = table.Column(table_j);
                        Vector<double> maskedCol = col.PointwiseMultiply(vec_bitmask);
                        Vector<double> notMaskedCol = col.PointwiseMultiply(vec_notBitmask);

                        if (maskedCol.Sum() > 0.0 && notMaskedCol.Sum() == 0.0)
                        {
                            jList.Add(table_j);
                            viewSpeciesList.Add(AllSpeciesPhases.list[table_j]);
                            tempMatrixCols.Add(col);
                        }
                    }

                    // 2.
                    Matrix<double> tempMatrix = Matrix<double>.Build.DenseOfColumnVectors(tempMatrixCols);

                    // 3.
                    List< Vector<double> > viewRowsList = new List< Vector<double> >();
                    for (int table_i = 0; table_i < 64; table_i++)
                    {
                        if ((bitmask & (1UL << table_i)) != 0)
                        {
                            viewRowsList.Add(tempMatrix.Row(table_i));
                        }
                    }

                    // 4.
                    view = Matrix<double>.Build.DenseOfRowVectors(viewRowsList);

                    // Fix the lists as arrays
                    viewElements = viewElementsList.ToArray();
                    viewSpecies = viewSpeciesList.ToArray();

                    // Cache the view
                    viewCache[bitmask] = (viewElements, viewSpecies, view);
                } // End lock
            }
        }
    }

    public static void GetGasView(ulong bitmask, out Element[] gasViewElements, out SpeciesPhase[] gasViewSpecies, out Matrix<double> gasView)
    {
        if (bitmask == 0)
        {
            // Can't represent this view in the bitmask, so return the full table
            gasViewElements = Elements.list;
            gasViewSpecies = gasSpecies;
            gasView = gasTable;
        }
        else
        {
            gasViewCache.TryGetValue(bitmask, out (Element[], SpeciesPhase[], Matrix<double>) cachedView);
            if (cachedView != default)
            {
                gasViewElements = cachedView.Item1;
                gasViewSpecies = cachedView.Item2;
                gasView = cachedView.Item3;
            }
            else
            {
                // Create the view and cache it
                lock (gasViewCacheLock)
                {
                    // Dependent on the all phases view
                    GetView(bitmask, out Element[] viewElements, out SpeciesPhase[] viewSpecies, out Matrix<double> view);

                    // Elements is the same
                    List<SpeciesPhase> gasViewSpeciesList = new List<SpeciesPhase>();
                    List< Vector<double> > gasTempMatrixCols = new List< Vector<double> >();

                    for (int view_j = 0; view_j < view.ColumnCount; view_j++)
                    {
                        SpeciesPhase speciesPhase = viewSpecies[view_j];
                        if (speciesPhase.Phase == Phase.Gas)
                        {
                            gasViewSpeciesList.Add(speciesPhase);
                            gasTempMatrixCols.Add(view.Column(view_j));
                        }
                    }

                    gasViewElements = viewElements;
                    gasViewSpecies = gasViewSpeciesList.ToArray();
                    gasView = Matrix<double>.Build.DenseOfColumnVectors(gasTempMatrixCols);

                    // Cache the view
                    gasViewCache[bitmask] = (gasViewElements, gasViewSpecies, gasView);
                }
            }
        }
    }
}
