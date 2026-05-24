# OpenAI GPT-5.5 Analysis

## Short Answer

The immediate error is not a thermodynamic subtlety. It is a species-basis error in `FormulaTable.GetView`: the current view admits any species containing at least one active element, then drops all inactive elements from its formula. With initial CH4/O2, the active elements are H/C/O, but the solver is allowed to create species like `ALHF2`, `B3O3F3`, `UOF4`, etc. Their Al/F/B/U/etc. atoms are invisible to the element-balance equations, so those atoms are created for free. That is why `boxsim.txt` contains absurd moles and mass after one step.

Fix that first. If the species basis is invalid, damping, SVD, log clamps, and better initial guesses only make the wrong answer fail more slowly.

After that, I would not try to make all 1612 species phases active at once. I would run an active-set equilibrium solver: start with a small guaranteed-feasible basis, solve it, compute every inactive species' formation desirability from the current element potentials, admit only the strongest violators, and repeat until no excluded species wants to enter. This is how I would choose the subset programmatically without manually curating the NASA file.

For critical-property data, use the Python `chemicals` package / ChEDL as the first machine-readable source. It exposes `Tc`, `Pc`, `Vc`, `Zc`, and acentric factor lookups by CASRN and cites multiple underlying databanks: IUPAC, Matthews, CRC/TRC, Yaws, WebBook, PSRK, HEOS/REFPROP-derived values, and estimation methods. It is not perfect for 1612 NASA species, but it is the best practical open starting point.

## What I Looked At

Relevant code:

- `DSA/Chemistry/Volume.cs:205-506`: gas-only element-potential reaction solve.
- `DSA/Chemistry/Volume.cs:556-731`: phase transfer solve.
- `Data/Species.cs:155-199`: species loading currently calls `NASA9Loader.Load(path, null)` and then assigns made-up EOS.
- `Data/Species.cs:321-389`: `FormulaTable.GetView`, where the basis-selection bug lives.
- `Data/NASA9Loader.cs:181-268`: NASA entries grouped by base species and charge.
- `DSA/Chemistry/EquationsOfState.cs:14-76`: current runtime uses ideal gas for gases and zero-volume incompressible condensed phases.

Relevant logs:

- `docs/chemistry/solver_debug_3/output.txt:4-14`: 1114 species / 1612 phases load, and the first reaction step already has `vec_x` clamped at extreme values.
- `docs/chemistry/solver_debug_3/boxsim.txt:12-177`: impossible H/C/O-only initial condition produces Al, B, F, Cl, U, W, etc.

Relevant docs:

- `docs/chemistry/dual_problem/gpt_5_5.md`: correct structure of the dual equations.
- `docs/chemistry/solver_architecture.md`: current staged solve architecture.
- `docs/chemistry/energy_minimization_3.md`: useful high-level EPM summary, but outdated now that the code is gas-only and uses full NASA loading.

## Question 1: Where Is The Error Coming From?

### Primary Root Cause: Invalid Species View

`FormulaTable.GetView` currently does this:

```csharp
// 1. Keep only rows (elements) that are in the bitmask
// 2. Keep only columns (species) that have non-zero counts
```

The implementation first builds `tempMatrix` containing only active element rows, then keeps any species whose active-row sum is positive:

```csharp
if (col.Sum() > 0.0)
{
    viewSpeciesList.Add(AllSpeciesPhases.list[table_j]);
    viewColsList.Add(col);
}
```

That criterion means "species contains at least one active element". It should mean "species contains no inactive elements".

With initial resources CH4 and O2, the active element set is `{H, C, O}`. The current filter admits:

- `ALHF2`, because it contains H.
- `B3O3F3`, because it contains O.
- `POF3`, because it contains O.
- `UOF4`, because it contains O.
- Many other species containing H/C/O plus invisible elements.

Inside the reduced H/C/O view, `B3O3F3` becomes just `O3`. The solver is therefore allowed to consume only oxygen and get boron and fluorine for free. The element balance residual can be near zero in the reduced system while real mass conservation is catastrophically false.

That exactly matches `boxsim.txt`: the output is dominated by impossible species with huge moles and mass, while CH4 and O2 remain near their initial amounts. This is not just instability; it is an under-constrained problem.

### Concrete Fix

Build views by checking full formulas before dropping inactive rows. A species phase should be included only if every nonzero element in its full formula is in the active bitmask.

Pseudo-code for the bitmask path:

```csharp
bool speciesAllowed = true;
for (int table_i = 0; table_i < Elements.list.Length; table_i++)
{
    bool hasElement = table[table_i, table_j] != 0.0;
    bool elementActive = table_i < 64 && (bitmask & (1UL << table_i)) != 0;
    if (hasElement && !elementActive)
    {
        speciesAllowed = false;
        break;
    }
}
```

Then only include the species if `speciesAllowed` is true and it has at least one active element. For `bitmask == 0`, do not return the full table blindly for elements above 64; use a non-bitmask set-based view so the same subset rule still applies.

Also consider making this fail loudly in debug builds:

```csharp
foreach (SpeciesPhase sp in viewSpecies)
foreach (Element e in sp.Species.Formula.Keys)
    if (!viewElements.Contains(e)) throw new Exception(...);
```

This invariant would have caught the current bug immediately.

### Secondary Root Cause: Charge Is Not Conserved

`NASA9Loader.ParseFile` reads charge from the NASA `E` pseudo-element, and `BuildSpecies` preserves it only in the species name, e.g. `ALO--`, `PO2--`. The actual `Species.Formula` contains only real elements. There is no charge row in `FormulaTable`.

That means charged species are currently allowed without electron conservation or charge neutrality. Even after the element-subset bug is fixed, ions can still appear in neutral chemistry if they have low Gibbs energy under the NASA data. NASA CEA can handle ions because it includes electrons and charge constraints. This code currently skips `e-` at `NASA9Loader.cs:56-61` and does not constrain charge.

Concrete choices:

- Easiest: exclude any entry with `Charge != 0` until plasma/electrochemistry exists.
- Later: add charge as a conserved pseudo-element and include electrons as a species, but only enable that at high-temperature ionization or when a device explicitly supplies charge separation.

For the current BoxSim methane/oxygen case, exclude ions.

### Secondary Root Cause: The Dual System Is Still Singular-Prone

`SolveReactionsGas` now chops to gas species only, so the Jacobian is `(a + 1) x (a + 1)`: element potentials plus `N_gas`. That is much better than solving all three phases at once, but singularity can still happen when the active species set is degenerate.

Examples:

- Several species have identical formulas but different thermodynamic data or charge labels hidden by current constraints.
- The basis contains species that are always clamped to `exp(-100)` or `exp(100)`, so their derivatives are either zero or enormous.
- `vec_N[0]` is not reset from the current gas inventory or free-element scale before a new reaction solve. It is initialized to `1e-6` and then carries solver history. That can make the first `vec_n = N_gas * x_j` absurd if `x_j` is clamped to `e100`.

The current output shows this pattern: at `reactionStep = 0`, many `vec_x` values are exactly `3.720075976e-44` or `2.688117142e43`, the values of `exp(-100)` and `exp(100)`. The clamp prevents floating-point overflow but does not create a valid equilibrium. It lets many bad species sit at the same artificial ceiling, which gives Newton a fake landscape.

### Secondary Root Cause: The Current Framing Adds New Equilibrium Products Instead Of Replacing The Dissociated Pool Cleanly

The docs say Pile Simulator differs from CEA by dissociating a fraction each frame, then recombining only `freeElements`. That is fine as a game mechanic. The danger is that `SolveReactionsGas` calculates a new equilibrium composition for the free-element pool, then adds `vec_n[j]` to existing resources.

This is only safe if:

- `vec_p` contains only the free atoms being recombined.
- every included species uses only those constrained elements.
- all atoms consumed from `vec_p` are subtracted from `freeElements`.
- negative `freeElements` is treated as a serious diagnostic, not just a harmless self-correcting value.

Because the view bug violates the second condition, the add-only architecture creates arbitrary new matter. After fixing the view, the add-only architecture can be kept, but I would add a post-solve conservation assertion that computes total atoms in `Resources + freeElements` and compares it to the previous frame.

## How I Would Stabilize The Solver With The Existing Architecture

### Step 1: Enforce Conservation Invariants Before Numerical Work

Add cheap checks around each stage of `Volume.Solve()`:

- Before `Dissociate`, compute total atoms by element from resources plus `freeElements`.
- After `Dissociate`, assert totals unchanged.
- After `SolveReactionsGas`, assert totals unchanged.
- After `SolvePhases`, assert totals unchanged.
- Assert no species exists whose formula contains elements outside the volume's known element inventory.
- Assert no ion exists unless charge solving is enabled.

For this project, fail early and loudly. Do not damp through conservation violations.

### Step 2: Fix The Species View

Do this before tuning any Newton constants. A correct H/C/O gas view should include species such as H, H2, O, O2, O3, C, CO, CO2, CH4, CH3, CH2, CH, HCO, H2O, OH, HO2, H2O2, etc., but not `B3O3F3` or `UOF4`.

This one change should remove the absurd mass explosion in `boxsim.txt`.

### Step 3: Exclude Ions For Now

The current game state has no electron resource, electric potential, plasma model, or charge-neutrality constraint. Charged NASA species should be filtered out at load time or at view-selection time.

I would add something like `Species.Charge` instead of encoding charge only in the name, then make `FormulaTable` ignore charged species unless a solver flag enables charge conservation.

### Step 4: Seed `N_gas` From The Free-Element Pool

Right now `vec_N[0]` is a persistent internal variable. It starts at `1e-6`, while the free element pool may represent 0.3 mol from a 0.1% dissociation of 300 mol, or more later. If `N_gas` is too small, Newton can require huge corrections and the exponential terms hit clamps.

A reasonable initial scale is bounded by stoichiometry:

```text
N_min_atoms = max_i(p_i / max_j(n_ij))
N_max_atoms = sum_i(p_i)
```

Use a midpoint or previous-frame value clamped into that range. For gas-only H/C/O, this is simple and prevents `N_gas` from being orders of magnitude wrong at the first Newton step.

### Step 5: Replace Hard Log Clamps With Active Species Control

The clamp to `[-100, 100]` prevents `Math.Exp` overflow, but it also makes all very favorable species equally favorable. That destroys the ranking needed by Newton.

Keep a numerical clamp for safety, but do not let clamped-at-ceiling species remain unexamined. If any inactive/candidate species has `log_x` far above the active set, either add it to the active set or reject it for a physical reason. If an active species stays at the floor for several solves and has negligible moles, remove it.

### Step 6: Use A Line Search On The Dual Objective Or Residual Norm

Current damping limits `lambda` jumps and prevents `N_gas` from going negative. It does not check whether the step improves anything. Add a backtracking line search that only accepts a step if it reduces `||F||` or improves the dual objective `W`.

This can be small and pragmatic:

```text
damping = 1
while damping > min:
    trial = y + damping * delta
    if finite(trial) and norm(F(trial)) < norm(F(y)):
        accept
    damping *= 0.5
```

This is more important than SVD. SVD solves a near-singular linearized system; line search prevents a bad linearized step from poisoning the nonlinear state.

### Step 7: Treat Condensed Phases Outside The Reaction EPM For Now

Given the current architecture, keep `SolveReactionsGas` gas-only and let `SolvePhases` move moles between phases afterward. That is not full thermodynamic equilibrium, but it is much easier to stabilize.

I would not re-enable liquid/solid in the EPM until gas-only is correct with the full H/C/O/N/etc. candidate set. The all-phase ideal-solution model is theoretically clean in the docs, but the current game architecture has separate phase-transfer logic, zero condensed volume, and no robust phase-appearance/disappearance outer loop. Mixing both approaches creates contradictory responsibilities.

## Question 2: If It Cannot Be Stabilized, How Should I Choose A Good Subset Programmatically?

I think it can be stabilized, but not with all 1612 phases blindly active. Use an active-set method.

### My Recommended Active-Set Algorithm

1. Build the feasible universe.

Include only species whose formula is a subset of the current element set. Exclude charged species unless charge solving is enabled. For gas-only EPM, include only `Phase.Gas`.

2. Start with a guaranteed basis.

Include current resources, their likely dissociation products, monatomic elements, common diatomics, and a few low-Gibbs stable molecules. For H/C/O, this basis would include H, H2, O, O2, OH, H2O, C, CO, CO2, CH4.

3. Solve the restricted equilibrium.

Run the current EPM on this small active set.

4. Score excluded species using the solved element potentials.

For every excluded species `j`, compute:

```text
log_x_pred = -mu_j / RT + sum_i(lambda_i * n_ij)
```

This is already the EPM formula. If `log_x_pred` is large, the excluded species would have a meaningful mole fraction if admitted. That is the mathematically natural admission criterion.

5. Admit only the strongest violators.

Add the top `k` excluded species with `log_x_pred > log_threshold`, for example top 5 to 20. Also admit any current resource species even if its score is low, to avoid disappearing/reappearing noise.

6. Repeat until no excluded species exceeds the threshold.

This gives the same final answer as the full feasible universe if the active-set loop converges, but each Newton solve sees a much smaller and better-conditioned matrix.

### Why This Fits The Existing Architecture

The dual method already gives you the exact ranking signal for inactive species. You do not need a hand-built chemistry ontology to decide whether CO, HO2, H2O2, CH3, formaldehyde, etc. matter. If a species wants to enter, its `log_x_pred` says so.

This also lets the game keep a large NASA database without letting all of it participate every frame.

### Extra Heuristics Worth Adding

- Always include species already present in `Resources`.
- Always include the species that just dissociated, or their gas phase if present.
- Always include monatomic species for each active element as a feasibility fallback.
- Prefer neutral species.
- Prefer species with NASA validity range covering current `T`; penalize or exclude species outside range.
- Limit molecular size, e.g. maximum total atom count, unless the species is already present.
- Limit elements to the volume's inventory exactly; no partial-overlap species.
- Keep an LRU cache of species admitted in recent frames to avoid active-set flicker.

### What Not To Do

Do not choose the subset only by lowest pure `mu_j`. Low pure Gibbs energy is not enough. A species with very low `mu_j` can be irrelevant if its stoichiometry cannot be supported by the current element potentials. The correct score includes both `mu_j` and `lambda dot formula`.

Do not include every NASA species containing O just because oxygen is active. That is the current failure.

Do not solve charged and neutral species together without charge conservation.

## Question 3: Where To Get `T_c`, `P_c`, And `v_c` For Cubic EOS?

### Best Practical Open Source: `chemicals` / ChEDL

Use the Python `chemicals` package as the first source to build a generated data table. Its critical-property module provides:

- `Tc(CASRN)`: critical temperature.
- `Pc(CASRN)`: critical pressure.
- `Vc(CASRN)`: critical molar volume.
- `Zc(CASRN)`: critical compressibility.
- Acentric factor elsewhere in the package.

The docs say critical `Tc` and `Pc` cover roughly 26,000 chemicals, and `Vc` roughly 25,000 chemicals. Sources include IUPAC, Matthews, CRC/TRC, Yaws, NIST WebBook, PSRK, HEOS/REFPROP-derived values, and estimation methods like Joback/Wilson-Jasperson/Fedors/Mersmann-Kind.

Advantages:

- Machine-readable.
- Local database after installing the package.
- SI units.
- Cited sources.
- Includes estimates when experimental values are absent.

Disadvantages:

- Lookup is CASRN-based, while NASA `thermo.inp` names are not CAS numbers.
- Many radicals, ions, refractory species, and obscure high-temperature gas species will not have real critical constants.
- Licensing is permissive for the package, but you should verify whether embedding generated derived tables into the game is acceptable for your distribution.

### Good Secondary Source: CoolProp

CoolProp is open-source and high quality, but covers a much smaller set of common fluids. It is useful for validating water, CO2, methane, oxygen, nitrogen, hydrogen, etc. It is not a broad NASA-species database.

Use it as a trusted override for common fluids, not as the main coverage source.

### Process-Simulator Databases

DWSIM is open-source and includes thermodynamic databases and links to ChEDL, CoolProp, Chemeo, DDB, KDB, etc. It can be useful as a reference for data plumbing and compound metadata, but its GPL license is a complication if you want to copy data or code directly into a non-GPL game. Treat it as a source to inspect, not necessarily embed.

### Commercial / Licensed Sources

The better chemical-engineering datasets are often commercial:

- DIPPR 801.
- NIST TRC / ThermoData Engine.
- REFPROP for high-accuracy common fluids.
- Dortmund Data Bank.
- DECHEMA / VDI Heat Atlas style compilations.

These are technically good but likely not a good default for a hobby game pipeline.

### Estimation When No Data Exists

For missing species, use estimation rather than fake constants:

- Joback for organic `Tc`, `Pc`, `Vc` when group decomposition is possible.
- Wilson-Jasperson for `Tc`/`Pc` if available.
- Fedors or Mersmann-Kind for `Vc` from formula/structure.
- Critical surface correlations if two of `Tc`, `Pc`, `Vc` are known.

But many NASA high-temperature species are radicals, ions, or species that do not have a normal vapor-liquid critical point in any practical sense. For those, a cubic EOS is not meaningful. The fallback should be ideal gas, not made-up critical constants.

### Recommended EOS Policy

- Use ideal gas for reaction equilibrium until the solver is stable.
- Use cubic EOS only for neutral stable fluids with known `Tc`, `Pc`, and acentric factor.
- For species with no real critical data, keep ideal gas.
- For condensed phases, do not use cubic EOS until you have real liquid/solid density data and a phase-equilibrium model that uses it consistently.
- Do not require `v_c` for Peng-Robinson or SRK parameterization; those primarily need `T_c`, `P_c`, and acentric factor. `v_c` is useful for diagnostics, volume translation, or vdW-style equations, but PR/SRK can run without it.

## Important Smaller Code Issues

### `SolvePhases` Poynting Sign Looks Wrong

For condensed phases, the pressure correction is normally:

```text
mu(T,P) = mu(T,P0) + v * (P - P0)
```

The code computes `poyntingCorrection = v * (P - bar)` and then subtracts it:

```csharp
mus.Add(resource.SpeciesPhase.Getmu(T, 0.0, 1.0) - poyntingCorrection);
```

That should probably be plus. With current `v = 0.0` for condensed phases it has no effect, but it will matter once real condensed volumes exist.

### `SolvePhases` Uses Standard Mu At 293 K For `P_sat`

The `P_sat` estimate uses:

```csharp
Getmu(Constants.NISTNormalTemperature, Constants.bar, 1.0)
```

Then divides by `R * T` at the current temperature. Saturation should compare phase chemical potentials at the current `T`, not at 293 K. This can easily make water phase transfer nonsensical.

Again, not the cause of the current Al/B/F/U explosion, but it will be the next problem after reaction conservation is fixed.

### `NASA9Loader` Can Duplicate Phase Mappings Incorrectly

`BuildSpecies` creates `phases` from entries, then maps each raw entry to the first phase with the same `Phase` enum:

```csharp
var matchingPhase = phases.FirstOrDefault(sp => sp.Phase == PhaseFromCode(...));
```

If a species has multiple solid allotropes or multiple condensed entries with the same `Phase`, `nameToPhase[entry.RawName]` can point to the wrong one. Since `SpeciesPhase.Name` is intended to distinguish graphite/diamond/ice/etc., the mapping should preserve entry identity, not rematch by enum.

This is not the current explosion either, but it will corrupt phase behavior.

### `SolveReactionsGas` Checks The Wrong Phase-Normalization Residual On Exit

The residual vector correctly uses `vec_Z - 1.0`:

```csharp
Vector<double>vec_F = Vector<double>.Build.DenseOfEnumerable(vec_H.Concat(vec_Z - 1.0));
```

But the early exit checks `vec_Z` itself:

```csharp
if (vec_H.PointwiseAbs().Maximum() < Constants.H_iTolerance
    && vec_Z.PointwiseAbs().Maximum() < Constants.Z_mTolerance)
```

For a normalized phase, `vec_Z` should be near `1`, not near `0`. This means the reaction loop probably never exits early for convergence. It should check `(vec_Z - 1.0).PointwiseAbs().Maximum()`.

## Priority Order

1. Fix `FormulaTable.GetView` so active species are strict subsets of active elements.
2. Exclude charged species or add charge conservation.
3. Add conservation assertions around every `Volume.Solve` stage.
4. Reset/seed `vec_N[0]` from the free-element pool scale.
5. Add active-set species admission using `log_x_pred` from solved element potentials.
6. Add line search on Newton steps.
7. Only then revisit condensed phases and cubic EOS.

If you do only one thing, do item 1. It directly explains the impossible species in `solver_debug_3/boxsim.txt`.
