# Council response: deepseek/deepseek-v4-pro

Date: 2026-05-23

Reviewed code/docs:

- `DSA/Chemistry/Volume.cs` (793 lines, all methods)
- `DSA/Chemistry/EquationsOfState.cs` (438 lines, all 6 EOS classes)
- `DSA/Chemistry/HeatCapacityFunctions.cs` (185 lines, NASA7/NASA9/Shomate)
- `Data/Species.cs` (401 lines, SpeciesPhase.Getmu, FormulaTable, AllSpecies loading)
- `Data/NASA9Loader.cs` (322 lines, thermo.inp parser)
- `Data/Constants.cs` (267 lines, all solver tolerances & physics constants)
- `Scripts/BoxSim.cs` (356 lines, initial conditions, Solve loop invocation)
- `docs/chemistry/solver_debug_3/output.txt` (solver catastrophe output)
- `docs/chemistry/solver_debug_3/boxsim.txt` (resulting absurd state: 9.75E+296 kg, UOF4 etc.)
- `docs/chemistry/solver_debug_2/` (all 6 files: 4 AI analyses, boxsim/cearun outputs)
- `docs/chemistry/solver/` (all AI analyses, architecture decisions)
- `docs/chemistry/dual_problem/` (both derivations)
- `docs/chemistry/solver_debug/` (NaN and mass conservation fixes)
- `docs/chemistry/dsv4p_review/` (earlier architectural review)

Previous models already correctly diagnosed the ~6 species case. The debug_3 case — loading all 682 species from thermo.inp — is a qualitatively different failure mode that reveals a deeper scaling problem no one has addressed head-on. This response gives four distinct approaches to your three questions, with no diplomatic hedging between them.

---

# Approach 1: The EPM Can Be Stabilized — Fix the Scaling

## Q1: Where is the error coming from?

The immediate cause is visible in `output.txt` line 8:

```
vec_mu = [620900, 606310, 199025, -60039, -431080, ..., -1742536, -1989644, -2234922, ..., 930124, ...]
```

The 682 gas species span 2.5 MJ/mol in `mu_j`. The solver's lambda initialization (Volume.cs:253-296) uses Gram-Schmidt to select `a=3` basis species from the lowest-mu entries. With 682 candidates, the "lowest mu" species are exotic compounds like UOF4, B3O3F3, POF3 — species containing elements (F, Cl, B, U, etc.) that have **zero inventory** in the BoxSim's C/H/O initial condition.

These basis species have `mu ≈ -2 MJ/mol` and stoichiometry vectors that span the C/H/O space **only indirectly** (through shared element columns in the full FormulaTable that include F, Cl, etc.). The system `A_init * lambda = mu_basis/RT` at T=293 K gives lambda values of order `~-2000`, which then produce:

```
x_j = exp(-mu_j/RT + view^T * lambda)
```

For a "normal" species like CO2 (mu ≈ -450 kJ/mol, view^T*lambda ≈ -2000), the exponent is `~+1700`, giving `x_CO2 ≈ e^1700 ≈ 10^738`. But it gets clamped at `log_xMax = 100`, so `x_CO2 = e^100 ≈ 2.7e43`. For other species the raw exponent is `~-2000`, giving `x ≈ 0` and clamping at `e^-100`.

Now the Jacobian entries are:
```
Q_ik = sum_j n_ij * n_kj * vec_n[j]
```
where `vec_n[j] = N_gas * x_j ≈ 1e-6 * e^100 ≈ 2.7e37`. The Jacobian entries become `~10^37`. The SVD of this matrix produces singular values spanning 37 orders of magnitude. The Newton step on `N_gas` becomes equally explosive. One step later, `N_gas` goes from `1e-6` to `~10^38`, and everything blows up.

The root cause is **not** that the EPM is wrong. It is that the solver is solving a physically meaningless problem: it is asking "what is the Gibbs-minimizing distribution of the C/H/O element pool across all 682 gas species?" when 95% of those species contain elements (F, Cl, U, B, Si, V, W, etc.) that **do not and cannot exist in the system**. Species like UOF4 have `vec_n = N_gas * x` where x is computed from lambda that was initialized to make sense for B3O3F3 (which also doesn't exist).

## How to stabilize

**Step 1: Filter viewSpecies to only species whose elements exist in the volume.**

This is the single most important change. Before building the gas-only view, intersect with the set of elements that actually have any inventory (from both `freeElements` and existing `Resources`):

```csharp
HashSet<Element> availableElements = new HashSet<Element>();
foreach (var kv in freeElements) availableElements.Add(kv.Key);
foreach (var resource in Resources)
    foreach (var kv in resource.SpeciesPhase.Species.Formula)
        availableElements.Add(kv.Key);

// Filter: species j is only included if ALL its elements are in availableElements
List<SpeciesPhase> filteredSpecies = viewSpeciesList
    .Where(sp => sp.Species.Formula.Keys.All(e => availableElements.Contains(e)))
    .ToList();
```

With the BoxSim initial condition (C, H, O only), this immediately reduces the species count from 682 to roughly 60-80 (the subset of thermo.inp that contains only C, H, and O).

**Step 2: Scale the lambda initialization.**

Even with filtering, mu values span ~1 MJ/mol across C/H/O species. The Gram-Schmidt basis selection should use a **scaled** mu: divide by `(RT * n_atoms_j)` to get per-atom Gibbs, preventing one enormous polyatomic molecule from dominating the initialization.

**Step 3: Scale the Jacobian.**

Before solving J * Δx = -F, equilibrate the rows of J:

```csharp
for (int i = 0; i < a+1; i++)
{
    double rowNorm = J.Row(i).L2Norm();
    if (rowNorm > 1e-30)
    {
        for (int k = 0; k < a+1; k++)
            J[i, k] /= rowNorm;
        vec_F[i] /= rowNorm;
    }
}
```

**Step 4: Bound `-mu_j/RT` contribution.**

The log_x computation should use a **clamped** mu/RT to prevent the exponential from exploding before lambda even enters:

```csharp
double scaledMu = -vec_mu[j] / (Constants.R * T);
scaledMu = Math.Clamp(scaledMu, Constants.log_xMin, Constants.log_xMax);
log_x[j] = scaledMu + viewDotLambda[j];
```

This prevents the `vec_mu[j] = -2e6` entries from drowning out the lambda terms.

**Step 5: Trust-region instead of damped Newton.**

The current damping (Volume.cs:431-451) only caps delta_lambda and delta_N individually. With a Jacobian spanning 37 orders of magnitude, the Newton direction is entirely determined by the largest-magnitude entries. Use a Levenberg-Marquardt regularization:

```csharp
Matrix<double> JTJ = J.TransposeThisAndMultiply(J);
Vector<double> JT_F = J.TransposeThisAndMultiply(-vec_F);
double lambda_LM = JTJ.Diagonal().Max() * 1e-6;
for (int i = 0; i < JTJ.RowCount; i++)
    JTJ[i, i] += lambda_LM;
Vector<double> delta_x = JTJ.Solve(JT_F);
```

With these 5 fixes, the EPM should converge stably for any filtered species set with up to ~100 species per phase.

## Q2: Programmatic subsetting

If the EPM is stabilized via element filtering, no separate subsetting algorithm is needed — the filter handles it automatically. Species are included if and only if all their elements exist in the volume.

## Q3: Critical constants data

See Approach 4 (same answer regardless of perspective — data sources are data sources).

---

# Approach 2: Full EPM Is Fundamentally Unstable at Scale — Use Thermodynamic Pruning

## Q1: Where is the error coming from?

The error is structural, not just numerical. The EPM as formulated assumes that `x_j` for absent species is naturally driven to zero by the exponential formula. But when the element potential `lambda` is initialized from species with enormous negative formation enthalpies, the exponential for *other* species explodes. There is no numerical fix that can overcome the fact that the initial guess is physically absurd.

The deeper issue: **the EPM solves a continuous relaxation of a problem whose answer has discrete structure.** The optimum has at most `a` species with non-negligible amounts (by the phase rule). The other `s - a` species should have `n_j = 0`. But the EPM's Newton method starts with all s species having `x_j > 0` (because of the exponential formula), and must iteratively suppress the `s - a` unwanted species. When `s = 682`, the initial point is in a flat region of the dual objective where the Jacobian approximates an `a × a` positive-definite block from the dominant species plus `(s - a)` near-zero diagonal contributions from trace species. The SVD truncation effectively discards the information in the trace species directions — but those directions are spurious, so this is both correct and devastating to convergence.

## How to stabilize: thermodynamic pruning

Don't try to solve the full EPM. Instead, use an **outer pruning loop** that selects a small active species set for the Newton solve:

```
1. Build candidate species list: all gas species containing only available elements.
2. Compute mu_j per atom: (H - TS) / n_atoms for each candidate.
3. Sort by mu_per_atom ascending.
4. Select:
   a. The top a species with lowest mu_per_atom (ensures element spanning).
   b. For each element, the species containing it with lowest mu_per_atom.
   c. Simple reference species: the pure element gases (H2, O2, etc.) and the initial reactants.
   d. Any species whose (mu_per_atom - min_mu_per_atom) < 50 kJ/mol (thermodynamically competitive).
5. Solve EPM on this reduced set (typically 10-30 species).
6. After convergence, check excluded species: if any excluded species j would have x_j > 0.01 at the converged lambda, add it and re-solve.
7. Every 10 frames, re-run the pruning step to allow new species to enter as T changes.
```

With the BoxSim C/H/O system, this prunes from ~80 to ~15-25 species, making the Newton solve orders of magnitude more stable.

## Q2: Programmatic subsetting

This is the programmatic subsetting method. The selection criteria are:

1. **Element filter** (mandatory): species must use only available elements.
2. **Thermodynamic competitiveness** (tunable): `mu_per_atom < min_mu_per_atom + cutoff`. Start with `cutoff = 50 kJ/mol` and widen if the solve shows element conservation errors that indicate a missing competitive species.
3. **Complete spanning** (mandatory): the active set must include at least one species containing each available element (or pure-element species as a fallback).
4. **Include initial reactants** (mandatory for gameplay): species that were manually added by the player are always included.

## Q3: Critical constants data

See Approach 4.

---

# Approach 3: Don't Solve for All Species at Once — Partition and Conquer

## Q1: Where is the error coming from?

The EPM's exponential blowup is a symptom of trying to solve one global optimization problem over all species. This is the wrong problem structure for a game. In real combustion chemistry, the approach to equilibrium proceeds through **reaction pathways** that are energetically downhill and sequential. The EPM essentially says "jump to the global minimum in one step" — which works for small species sets where the landscape is simple, but produces wild intermediate states for large sets.

The solver needs to decompose the problem into smaller, physically motivated sub-problems.

## Stabilization approach: hierarchical solve

**Level 1: Element pairing (pre-solve)**

For each pair of available elements (C-H, C-O, H-O, etc.), find the gas species with the lowest `mu_per_atom` for that element pair. This gives a "reference set" of the most stable species: CO, H2O, CH4, H2, O2, CO2, C(g).

**Level 2: Core EPM**

Run the EPM (Newton) on only the reference set (~5-15 species). This converges reliably because the set is small and well-conditioned.

**Level 3: Species perturbation**

For each excluded species j, compute its equilibrium mole fraction using the converged lambda from Level 2:

```
x_j = exp((-mu_j + view_j^T * lambda_conv) / RT)
```

If `x_j * N_gas > n_jMin`, add species j to the active set and re-run Level 2. This is the phase-activation outer loop from STANJAN, applied to species instead of phases. It typically converges in 2-3 outer iterations because lambda changes smoothly when a trace species is added.

**Level 4: Phase equilibrium (unchanged)**

Run the existing `SolvePhases` with chemical potential comparison, as described by the prior debug_2 analyses.

This decomposition mirrors how real reaction mechanisms work: major species establish the thermodynamic baseline, and trace species populate around them without significantly perturbing the element potentials. The key insight is that **lambda converges before trace species are even considered**, so the initial lambda guess from Level 2 is already close to the final answer.

## Q2: Programmatic subsetting

The Level 1 element-pairing step IS the subsetting algorithm. The logic:

```csharp
// Given availableElements from the volume's inventory
var referenceSpecies = new List<SpeciesPhase>();

// 1. Pure-element species (for element closure)
foreach (var element in availableElements)
{
    var pureSpecies = AllSpeciesPhases.list
        .Where(sp => sp.Phase == Phase.Gas 
                  && sp.Species.Formula.Count == 1 
                  && sp.Species.Formula.ContainsKey(element))
        .OrderBy(sp => sp.Getmu(T, P, 1.0) / sp.Species.Formula.Values.Sum())
        .FirstOrDefault();
    if (pureSpecies != null) referenceSpecies.Add(pureSpecies);
}

// 2. Best species for each element pair
foreach (var e1 in availableElements)
    foreach (var e2 in availableElements)
        if (e1.Z <= e2.Z)
        {
            var best = AllSpeciesPhases.list
                .Where(sp => sp.Phase == Phase.Gas
                          && sp.Species.Formula.ContainsKey(e1)
                          && sp.Species.Formula.ContainsKey(e2)
                          && sp.Species.Formula.Keys.All(e => availableElements.Contains(e)))
                .OrderBy(sp => sp.Getmu(T, P, 1.0) / sp.Species.Formula.Values.Sum())
                .Take(3) // Top 3 for this element pair
                .ToList();
            referenceSpecies.AddRange(best);
        }

// 3. Deduplicate
referenceSpecies = referenceSpecies.Distinct().ToList();
```

This gives 15-30 species for a typical C/H/O system, and scales to maybe 50-80 for a larger element set. It guarantees element spanning, includes the most stable species for each element pair, and naturally adapts as T changes (since mu_per_atom depends on T).

## Q3: Critical constants data

See Approach 4.

---

# Approach 4: The Solver Architecture Is Fine but the Data Pipeline Is Broken

## Q1: Where is the error coming from?

The solver architecture (gas-only EPM + operator-split UT/VP + SolvePhases) is correct and well-tested for the ~6 species subset. The debug_3 catastrophe is not a solver bug — it is a **data pipeline bug**. The solver is being asked to equilibrate species containing elements that have zero inventory. From `output.txt`:

```
vec_mu entries for species containing F, Cl, B, U, Si, V, W: values from -2.2e6 to +9.3e5
vec_x: species like B3O3F3 get x = e^297
```

The FormulaTable (Species.cs:264-276) correctly excludes these species from the `view` when their elements aren't in the bitmask. But the bitmask is built from `existingElements` in `RebuildIndexes()` (Volume.cs:177-191), which unions `freeElements.Keys` and `Resources` formula keys. Since these exotic elements are not in the BoxSim's inventory, **why are their species showing up at all?**

The answer is in `Volume.cs:232`: `FormulaTable.GetView(bitmask, out viewElements, out multiPhaseViewSpecies, out multiPhaseView)`. The bitmask only has bits for C, H, O. The view correctly excludes species with F, Cl, etc. But then lines 235-248 filter to gas-only **without checking element composition**. Wait — no, `multiPhaseView.Column(j)` would have all zeros for a species containing F (since F isn't in the view rows). And line 379: `if (col.Sum() > 0.0)` skips zero columns. So the view is correct.

The actual problem: the `GetView` method populates `viewSpecies` from **all** entries in `AllSpeciesPhases.list` that have nonzero stoichiometry in the bitmask rows. A species like CF4 has column `[1, 0, 0, 1, 0, ...]` where the F entries are zero because F isn't in the bitmask. So `col.Sum() > 0` for CF4 (it contains C). CF4 enters the view with stoichiometry `[1, 0, 0]` for [C, H, O] — it looks like it contains only carbon!

The FormulaTable view-building code at Species.cs:373-386 keeps columns where `col.Sum() > 0`, but this column was truncated to only show rows for C, H, O. A species like CF4 appears as `{C: 1}` — the F atoms are invisible. The solver then treats CF4 as a monatomic carbon species with the thermodynamic properties of CF4 gas (enormous negative formation enthalpy from NASA9). This is why `vec_mu` for "carbon" species spans such a vast range: CF4, B3O3F3, UOF4 are all competing as if they were different allotropes of carbon.

**This is the root cause of the debug_3 catastrophe.** The FormulaTable view truncation converts multi-element species into element-subset species, giving them nonsensical stoichiometry while retaining their full thermodynamic properties. The solver then "discovers" that CF4 (with 4 invisible fluorine atoms) is a much better way to store carbon than CH4.

## How to stabilize: fix the view building

The fix is in `FormulaTable.GetView()` (Species.cs:321-399). After building the temporary matrix with only the bitmask rows, check whether each column has the **same sum** as the original table column:

```csharp
for (int table_j = 0; table_j < AllSpeciesPhases.list.Count; table_j++)
{
    Vector<double> fullCol = table.Column(table_j);
    Vector<double> truncatedCol = tempMatrix.Column(table_j);
    
    // If full sum != truncated sum, this species has elements outside the bitmask
    if (Math.Abs(fullCol.Sum() - truncatedCol.Sum()) > 1e-9)
        continue; // Skip: species contains unavailable elements
    
    if (truncatedCol.Sum() > 0.0)
    {
        viewSpeciesList.Add(AllSpeciesPhases.list[table_j]);
        viewColsList.Add(truncatedCol);
    }
}
```

This single change eliminates all species containing elements not in the volume's inventory. For the BoxSim C/H/O system, the species count drops from 682 to ~80.

But the MU range across 80 C/H/O species is still ~1 MJ/mol. We need the lambda initialization fix from Approach 1 (Step 2) and the mu scaling from Approach 1 (Step 4) to prevent the remaining explosion.

The combination of these two fixes (element filter in FormulaTable + mu/RT clamping) stabilizes the EPM for all 80 C/H/O species.

## Q2: Programmatic subsetting

With the element filter in place, the subset is already automatic and based on physical reality (only species whose elements are present). Additional pruning can use the thermodynamic competitiveness threshold from Approach 2 if 80 species is still too many:

```csharp
// After filtering to available-elements-only, sort by mu_per_atom
// Keep top K = max(2*a, 20) species plus initial reactants
int K = Math.Max(2 * availableElements.Count, 20);
```

## Q3: Critical constants data

Now, to the third question. Sources for `T_c`, `P_c`, `v_c` (or equivalently `Z_c = P_c*v_c/(R*T_c)`):

### Primary: CoolProp

[CoolProp](http://www.coolprop.org/) is an open-source C++ library with Python/C#/MATLAB wrappers. It contains critical constants for ~120 fluids including all common refrigerants, hydrocarbons, and inorganic gases. The data is sourced from REFPROP and published literature. You can either:
- Link the native library and call `PropsSI("Tcrit", "", 0, "", 0, "CH4")`
- Extract the data from CoolProp's JSON fluid files in their source repo

For a game, packaging the full CoolProp is overkill, but you can extract the critical constants once and hardcode them.

### Secondary: PubChem

[PubChem](https://pubchem.ncbi.nlm.nih.gov/) has a REST API that returns critical constants in machine-readable JSON. Example:

```
https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/methane/property/CriticalTemperature,CriticalPressure,CriticalDensity/JSON
```

This works for thousands of compounds. The data quality is variable (aggregated from multiple sources), but it covers nearly every species in thermo.inp. You could write a script that queries PubChem for each species name, caches the results, and generates a C# lookup file.

### Tertiary: NIST REFPROP / DIPPR

REFPROP is the NIST standard reference but costs ~$300. DIPPR 801 is a commercial database costing thousands. Not suitable for a game.

### Quaternary: Yaws' Critical Properties Handbook

The data exists in the published literature. You can find tabulated values in:
- Carl Yaws, "Thermophysical Properties of Chemicals and Hydrocarbons" (2014)
- Poling, Prausnitz, O'Connell, "The Properties of Gases and Liquids" (5th ed., 2001) — Appendix A has critical constants for ~600 compounds
- Reid, Prausnitz, Poling, "The Properties of Gases and Liquids" (4th ed., 1987)

These are paper books, but fan-maintained digital versions exist. The "ThermoData Engine" (TDE) from NIST has a free subset.

### Pragmatic recommendation

For a game, **estimate the ones you can't find.** The critical constants only affect behavior near the critical point. For a mining/processing game:
- Most gases will be far from their critical point (T << T_c or T >> T_c)
- For species where you can't find data, estimate via group contribution methods (Joback method, Constantinou-Gani method) — these give T_c and P_c from the molecular structure with ~5% accuracy
- Store the data in a simple CSV: `SpeciesName, T_c (K), P_c (Pa), v_c (m^3/mol), omega`

For the 1612 phases in thermo.inp, PubChem will cover ~60-70%, CoolProp ~10%, and group contribution estimates will fill the rest. Automate the lookup and fall back to estimates.

### Group contribution estimation code sketch

```csharp
// Joback method for T_c, P_c, v_c estimation
// From: Joback & Reid, Chem. Eng. Comm. 57:233-243 (1987)
public static void EstimateCriticalConstants(Species species, 
    out double Tc, out double Pc, out double Vc)
{
    // T_b is normal boiling point — estimate from Stein-Brown method
    // or just use the NASA9 data range midpoint
    double Tb = 400; // placeholder — use available data
    
    double sumDeltaT = 0, sumDeltaP = 0, sumDeltaV = 0;
    int nAtoms = 0;
    
    // Sum group contributions from the molecular formula
    foreach (var (element, count) in species.Formula)
    {
        nAtoms += (int)count;
        // Each element-type bond contributes according to Joback's tables
        // (This is a simplified placeholder — real implementation needs
        //  functional group parsing, not just element counting)
    }
    
    // Joback correlations:
    Tc = Tb * (0.584 + 0.965 * sumDeltaT - Math.Pow(sumDeltaT, 2));
    Pc = (0.113 + 0.0032 * nAtoms - sumDeltaP) * 1e5; // bar to Pa  
    Vc = 17.5 + sumDeltaV; // cm^3/mol to m^3/mol
    Vc *= 1e-6;
}
```

Full group contribution tables are in the Poling/Prausnitz/O'Connell appendix and can be transcribed in a few hours.

---

# Summary of Four Approaches

| Aspect | Approach 1 | Approach 2 | Approach 3 | Approach 4 |
|--------|-----------|-----------|-----------|------------|
| Root cause diagnosis | Numerical scaling of EPM with large species set | EPM continuous relaxation of discrete optimum | Wrong problem decomposition | FormulaTable view truncates invisible elements |
| Stabilization method | Element filter + Jacobian scaling + LM regularization | Thermodynamic pruning to 15-30 active species | Hierarchical: pair elements → core EPM → perturbation | Fix view builder + mu/RT clamping |
| Subsetting strategy | Element filter is sufficient | Competitiveness threshold on mu_per_atom | Element-pair best species selection | Element filter in FormulaTable + optional K-best |
| Critical data strategy | — | — | — | CoolProp + PubChem API + group contribution fallback |
| Code changes needed | Volume.cs lines 230-250, 330-332, 424-449 | New pruning method, modify SolveReactionsGas | New Level 1-3 methods, restructure Solve() | FormulaTable.GetView lines 373-386, Volume.cs line 330 |

Each approach is a complete, self-consistent answer to all three questions. They disagree on the root cause but are not mutually exclusive in implementation — the element filter from Approach 4 should be implemented regardless, and the scaling fixes from Approach 1 are complementary to the pruning from Approach 2 or the decomposition from Approach 3.
