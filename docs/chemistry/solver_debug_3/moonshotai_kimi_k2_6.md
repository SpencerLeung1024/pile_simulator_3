# Council response: moonshotai/kimi-k2.6

Date: 2026-05-23

Reviewed code/docs:
- `DSA/Chemistry/Volume.cs` (full file, lines 1-793)
- `DSA/Chemistry/EquationsOfState.cs`
- `Data/Species.cs` (full file, lines 1-401)
- `Data/Constants.cs`
- `docs/chemistry/solver_debug_3/boxsim.txt`
- `docs/chemistry/solver_debug_3/output.txt`
- `docs/chemistry/solver_debug_2/*` (all model responses)
- `docs/chemistry/solver_debug/deepseek_v4_pro/epm_nan_fix_journey.md`
- `docs/chemistry/solver_debug/gpt_5_5/water_overproduction_fix.md`
- `docs/chemistry/dual_problem/*`

**This response intentionally does not agree with the previous Council.** All four prior reviewers (GPT-5.5, Opus 4.7, DeepSeek V4 Pro, and my previous self) analyzed the 6-species subset case in `solver_debug_2` and reached a convergent set of conclusions about missing CO, broken `SolvePhases`, and gas-only minoring. Those conclusions are correct for the subset case. **They are irrelevant to `solver_debug_3`.**

The `solver_debug_3` problem is fundamentally different. When you load all 682 species (or all 1612 species phases), the solver does not merely "struggle to converge" or "produce slightly wrong carbon amounts." It generates **10^84 moles of aluminum hydrogen difluoride** and a system mass of **10^296 kg**. This is not a convergence issue. It is a **structural ill-posing of the equilibrium problem** that the previous analysis completely missed because those models were not asked to look at the full-database case.

I will offer **four genuinely different approaches** to diagnosing and fixing this. Each approach answers your three questions from a different angle. They are not mutually exclusive; you can mix them.

---

## The Common Diagnosis All Four Approaches Share

Before the four approaches, here is the single bug that none of the previous reviewers identified.

### The View Filter Bug (`Data/Species.cs`, `FormulaTable.GetView`, lines 321-400)

When `NASA9Loader.Load(path, null)` loads the full database and `BoxSim` starts with only CH4 and O2, `RebuildIndexes()` correctly identifies that only three elements are present: C, H, O. It builds a bitmask for {C, H, O} and calls `FormulaTable.GetView`.

Inside `GetView` (line 379):
```csharp
if (col.Sum() > 0.0)
{
    viewSpeciesList.Add(AllSpeciesPhases.list[table_j]);
    viewColsList.Add(col);
}
```

`col` is the stoichiometry column **with respect to the selected elements only**. For a species like `UO2`, the full formula is {U: 1, O: 2}. In the reduced view with only {C, H, O}, the column becomes [0, 0, 2] — uranium has been silently dropped. `col.Sum() > 0` is **true** because oxygen is present. Therefore `UO2` enters the view.

The solver now sees `UO2` as a valid species containing **only oxygen**. It has no knowledge of uranium. The element balance equation for oxygen is:
```
H_O = sum_j n_Oj * n_j - p_O = 0
```
`UO2` contributes `2 * n_UO2` to this balance. If `UO2`'s NASA9 Gibbs energy is very negative (which it is, because UO2 is a very stable oxide), the solver tries to put **all available oxygen into UO2** because it thinks UO2 is just an exotic way to store oxygen atoms with no uranium cost.

The same happens for `ALHCL2` (contains H), `Be3O3` (contains O), `Na2SO4` (contains O), `SiHCL3` (contains H), and so on. The boxsim.txt output shows exactly this: every impossible species contains **at least one atom from the {C, H, O} set** and any number of invisible heavy atoms.

This is why:
- Mass explodes to 9.75E+296 kg (10^84 mol of heavy species)
- `vec_x` spans 87 orders of magnitude (3.7E-44 to 2.7E+43)
- The solver cannot be "damped" or "initialized better" to fix this: the problem is literally missing conservation constraints for 115 elements

**The one-line fix:** `GetView` must only include a species if **all** of its constituent elements are in the selected element set. Replace `col.Sum() > 0.0` with a check that the original full-column sum equals the reduced-column sum (meaning no atoms were dropped).

With this fix alone, the solver will only see species composed of C, H, and/or O. From the full database, that is probably a few hundred species instead of 682, but it is still far more than the 6-species subset.

Now, the four approaches for handling the actual full-database equilibrium.

---

## Approach 1: The Structural / Mathematical Fix — Fix the Filter, Then Precondition the Hell Out of It

### Q1: Where is the error coming from? How to stabilize?

**Primary cause:** The view-filter bug above. **Secondary cause:** Even with only C/H/O species, you now have ~200-300 species competing for 3 elements. The chemical potential range across these species at 1000 K is still ~500-1000 kJ/mol, giving exponents of ~50-100. Your `log_xMax = 100` clamp barely contains this. When 200 species simultaneously push against the clamp, the Jacobian becomes a random matrix and SVD truncates everything.

**Stabilization strategy:**

1. **Fix the view filter** as described above. This is non-negotiable.

2. **Add species pre-culling before Newton.** After computing `vec_mu` for all view species, do not send all of them into the Newton loop. Compute the "elemental price" of each species:
   ```
   price_j = mu_j / (sum of atoms in species j)
   ```
   Sort species by `price_j`. Keep only the 30-50 species with the lowest price. The others are thermodynamically irrelevant at this T/P. This is not a hack — it is the physical realization that at equilibrium, 95% of species in a large database have negligible mole fractions.

3. **Use a Levenberg-Marquardt solver instead of raw Newton.** Replace `J.Svd().Solve(-F)` with:
   ```
   (J^T J + lambda_diag * I) delta = -J^T F
   ```
   where `lambda_diag` starts large (steepest descent) and shrinks as you converge. This handles the near-singularity of the saddle-point system far better than pure SVD truncation, especially when species have wildly different scales.

4. **Solve for `ln(N_gas)` instead of `N_gas`.** The phase normalization residual becomes nonlinear in `ln(N)`, but `N` can never go negative and the search space is better conditioned.

### Q2: How to choose a good subset programmatically?

You don't need to read all 1612 phases. Use **two-pass greedy selection**:

- **Pass 1 (necessary species):** Include all elemental species (C(g), H2, O2), all species already present in `Resources`, and all species whose `mu_j` is within `50 * RT` of the minimum `mu_j` in the view. This typically gives 10-30 species.
- **Pass 2 (combinatoric coverage):** For each pair of elements, include the 3 most stable diatomic/molecular species (CO, CO2, H2O, CH4, etc.). This ensures the stoichiometry matrix has full rank and captures major reaction pathways.

This is O(S log S) and requires no human curation.

### Q3: T_c, P_c, v_c source

Use **CoolProp** (http://www.coolprop.org/). It is open-source (MIT license), has a C# wrapper, and exposes critical properties programmatically:
```csharp
AbstractState s = AbstractState.factory("HEOS", "Water");
double Tc = s.T_critical();  // K
double Pc = s.p_critical();  // Pa
```
CoolProp covers ~150 common fluids. For exotic species not in CoolProp, fall back to the **Joback method** (group contribution) using the atom connectivity from your `Species.Formula` to estimate T_c and P_c from molecular structure. The Joback equations are simple arithmetic on functional groups and can be implemented in ~50 lines.

---

## Approach 2: The Physical Chemistry Fix — Column Generation and Element Potential Bounds

### Q1: Where is the error coming from? How to stabilize?

**Primary cause:** The view-filter bug. **Secondary cause:** You are solving the dual problem (element potentials) with a fixed set of columns (species), but the dual problem is actually a **semi-infinite program**: the full database is the set of all possible species, and you are approximating it with a finite subset. When that subset is large but contains species with similar stoichiometries (e.g., 50 different hydrocarbons all with C:H ratios near 1:2), the dual variables are underdetermined and the Hessian `Q` becomes nearly singular.

**Stabilization strategy:**

Do not solve on 682 species at once. Use **column generation** from linear programming:

1. **Start with a tiny working set:** The 6 species from your original subset (CH4, O2, H2O, CO2, H2, C).
2. **Solve the dual problem** on this working set. Obtain `lambda_C`, `lambda_H`, `lambda_O`.
3. **Price out the full database:** For every species *not* in the working set, compute its "reduced cost":
   ```
   reduced_cost_j = mu_j - sum_i(lambda_i * n_ij)
   ```
   At equilibrium, all reduced costs must be >= 0 (for minimization). If you find a species with `reduced_cost_j < -RT * 5` (significantly negative), it wants to enter the equilibrium.
4. **Add the most violating species** to the working set and re-solve.
5. **Repeat** until no species violates the bound by more than a small threshold.

This is how large-scale process simulators (Aspen, gPROMS) handle huge species lists. The key insight is that **the element potentials contain all the information needed to reject 95% of species without solving a large system**. The Newton solver only ever sees 6-20 species, so it is always well-conditioned.

### Q2: How to choose a good subset programmatically?

Column generation *is* the subset selection algorithm. You don't choose a subset upfront; you discover it dynamically. The working set at convergence is exactly the subset you need.

For gameplay, you can cache the working set per element-combination and temperature range. A "C/H/O @ 1000 K" cache might contain 15 species. When the player adds nitrogen, you invalidate the cache and re-run column generation starting from the C/H/O set plus N2.

### Q3: T_c, P_c, v_c source

Use the **NIST ThermoData Engine (TDE)** bulk data files. NIST provides SQLite databases of evaluated thermodynamic data including critical properties for thousands of species. Unlike the WebBook (which is page-by-page), the TDE bulk files are machine-processable. Access requires a free account at NIST SRD but the files are downloadable.

If NIST TDE is too bureaucratic, use the **DWSIM** open-source process simulator database. DWSIM ships with a SQLite database (`dwsim.ini` or `coolprop.xml` depending on version) containing critical properties for ~2000 compounds, extracted from DIPPR and other open compilations.

---

## Approach 3: The Statistical Mechanics Fix — Equipartition and a Temperature-Dependent Basis

### Q1: Where is the error coming from? How to stabilize?

**Primary cause:** The view-filter bug. **Secondary cause:** The Element Potential Method assumes the equilibrium composition is determined by minimizing Gibbs free energy, which is correct, but it does not exploit the fact that **at any given temperature, only a few species dominate the free-energy landscape**. The other 600+ species are not "barely present"; they are **thermodynamically forbidden** at that T/P, and their inclusion pollutes the numerical conditioning.

**Stabilization strategy:**

Use a **temperature-dependent species hierarchy** derived from the partition function:

1. **Compute the partition-function weight** for each species:
   ```
   w_j = exp(-mu_j / (RT))
   ```
   This is proportional to the statistical weight of species j in an ideal mixture.

2. **Cluster species by stoichiometric similarity.** Use k-means or simple threshold clustering on the normalized formula vectors (e.g., all CxHyOz species with similar x:y:z ratios cluster together).

3. **Select the cluster representative** with the highest `w_j` (lowest `mu_j`). Replace the entire cluster with this one representative species.

4. **Solve the EPM on the representatives** (typically 10-30 clusters).

5. **Post-process:** Within each cluster, the true equilibrium distribution follows a Boltzmann distribution over the cluster members. You can approximate this analytically without solving another Newton system.

This is related to the "coarse-graining" methods used in combustion reduced chemistry (e.g., CSP, ILDM). For a game, it has the added benefit that you can tune the number of clusters to balance accuracy vs. performance.

### Q2: How to choose a good subset programmatically?

The clustering algorithm *is* the subset selector. A simple implementation:

```csharp
// For each species, compute a feature vector
Vector<double> feature = [n_C, n_H, n_O, mu_j / RT];
// Cluster by Euclidean distance in this space
// Keep the species with minimum mu_j in each cluster
```

Alternatively, use **hierarchical agglomerative clustering** on the formula vectors. At the leaf level you have all 682 species; at the root you have just "CHxOz". Cut the dendrogram at a distance threshold that gives ~20 clusters. This is O(S^2) but you only run it once per element-set and cache the result.

### Q3: T_c, P_c, v_c source

Use **Yaws' Handbook of Thermodynamic and Physical Properties of Chemical Compounds** (published by Knovel). It is available as a downloadable Excel/CSV file and contains critical properties, acentric factors, and heat capacities for ~25,000 compounds. It is the industry standard for bulk property data. The ISBN is 978-0-8155-1500-0.

If purchasing Yaws is not an option, use the **Joback + Reid** group contribution method for T_c, P_c, and V_c. The formulas are:
```
T_c = Tb / (0.584 + 0.965 * sum(Gk) - (sum(Gk))^2)
P_c = (0.113 + 0.0032 * Na - sum(Gk))^-2  [bar]
V_c = 17.5 + sum(Gk)  [cm^3/mol]
```
where `Gk` are group contributions (e.g., `-CH3`, `>C=O`, `-OH`) and `Na` is the number of atoms. The group contribution tables are public domain and reproduced in every chemical engineering textbook (e.g., Poling, Prausnitz & O'Connell, *The Properties of Gases and Liquids*).

---

## Approach 4: The Software Architecture Fix — Kill the Global Solver, Use Reaction Graphs

### Q1: Where is the error coming from? How to stabilize?

**Primary cause:** The view-filter bug. **Secondary cause:** The entire architecture of "load all species into one big linear algebra problem and solve it globally" is fundamentally mismatched to a game where:
- The player only ever manipulates a few materials at a time
- Reactions happen in devices with specific inputs and outputs
- 99% of the 1612 species phases will never appear in gameplay

**Stabilization strategy:**

**Stop using a global equilibrium solver.** Instead, implement a **reaction graph**:

1. **Define a graph where nodes are species and edges are reactions.** Edges are annotated with rate laws, equilibrium constants, and device requirements (e.g., "BlastFurnace", "Electrolyzer").

2. **Only allow reactions along edges.** The BlastFurnace device has edges for `Fe2O3 + 3CO -> 2Fe + 3CO2`. The Volume has edges for dissociation/recombination of species that share the same element set.

3. **Equilibrium is reached locally along each edge**, not globally across all species. This is how real chemical process simulators work: they do not find the Gibbs minimum of the universe; they find the steady state of a connected flowsheet.

4. For the Volume specifically, maintain a **small active set** of species that have actually been introduced (by mining, by reaction, or by the player). When a new species appears (e.g., the player dumps UO2 into the box), add it and its immediate reaction neighbors to the active set. The EPM solver only ever sees the active set, which should stay under 20 species in practice.

This eliminates the subset-selection problem entirely: the subset is defined by gameplay, not by thermodynamics.

### Q2: How to choose a good subset programmatically?

You don't. The subset is emergent:
- **Initial active set:** Whatever the asteroid is made of (maybe 10-20 minerals).
- **Expansion rule:** When a species amount exceeds `n_jMin`, add all species reachable by one dissociation/recombination step to the active set.
- **Pruning rule:** If a species amount stays below `n_jMin` for 1000 frames, remove it from the active set.

This is how games like *Oxygen Not Included* and *Stationeers* handle chemistry: they hardcode the reaction network. You have the advantage of a real thermodynamic database, but you should still curate the reaction graph.

For the BoxSim specifically, if the goal is just to validate the thermodynamics, load the full database but only allow the solver to see species whose **formation enthalpy per atom is within 200 kJ/mol of the most stable species** for that element combination. This is a single-pass filter that requires no iteration.

### Q3: T_c, P_c, v_c source

Since you are building a game, you do not need laboratory-grade critical properties for all 1612 species. You need them only for species that the player actually encounters in fluid phases (gases and liquids). For everything else (solid minerals, trace intermediates), use placeholder EOS.

**Recommended data pipeline:**
1. Export the list of species that appear in your reaction graph (maybe 50-100).
2. Query the **PubChem PUG REST API** programmatically. PubChem has critical properties for ~10,000 compounds, accessible via:
   ```
   https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/CO/property/MolecularWeight,IUPACName/JSON
   ```
   While PubChem does not have a direct "critical temperature" field in the REST API, the Compound Summary pages contain it under "Physical Description" and can be scraped or queried via the PUG View API.
3. For missing species, use the **Peng-Robinson estimation** built into many process simulators: if you have Tb (boiling point) and the acentric factor omega, you can estimate T_c and P_c from vapor-pressure correlations. The NIST WebBook provides Antoine parameters for vapor pressure; fit them to the Peng-Robinson alpha function to back out T_c and P_c.

---

## Summary Table

| Approach | Core Idea | Subset Selection | T_c/P_c/v_c Source |
|----------|-----------|------------------|-------------------|
| **1: Structural Fix** | Fix view filter + precondition Newton | Top 50 by `mu_j / atom` | CoolProp + Joback |
| **2: Column Generation** | Solve tiny working set, price out database | Dynamic, via reduced costs | NIST TDE / DWSIM DB |
| **3: Statistical Clustering** | Cluster by formula + Gibbs energy, solve representatives | Hierarchical clustering on formula space | Yaws Handbook / Joback |
| **4: Architecture Change** | Replace global solver with reaction graph | Gameplay-emergent active set | PubChem PUG + PR fit |

## What To Do Right Now

Regardless of which long-term approach you choose, do these three things immediately:

1. **Fix `FormulaTable.GetView` line 379.** A species must only enter the view if **all** its elements are in the selected element set. Without this fix, every other optimization is meaningless.

2. **Add a `GetmuPure` method to `SpeciesPhase`** (as recommended in `solver/kimi_k2_6.md`) and use it in `SolveReactions`. The current `Getmu(T, P, x_j=1)` still includes the mixing term via the `x_j < 1.0` check, which is conceptually wrong for the dual problem exponent.

3. **After fixing (1), re-run the full-database BoxSim.** I predict you will see ~50-100 C/H/O species, no uranium, no aluminum, and mass conservation restored. The solver will probably still struggle with 100 species, but the explosion will be bounded. At that point, the `solver_debug_2` fixes (add CO, fix `SolvePhases`) become relevant again.

## Final Note on the Previous Council

The previous models all gave excellent advice for the 6-species subset case. But their analysis of the full-database instability was absent because they were not asked to look at `solver_debug_3`. The `output.txt` in this directory contains the smoking gun: a 3-element lambda vector trying to balance 682 species. The moment `UO2` appears in the view, the solver is no longer solving a C/H/O equilibrium — it is solving a broken linear system with missing conservation laws. That is not something damping, SVD, or better lambda initialization can fix.
