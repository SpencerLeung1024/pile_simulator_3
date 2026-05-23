# Council response: moonshotai/kimi-k2.6

Date: 2026-05-23

Reviewed code/docs:
- `DSA/Chemistry/Volume.cs` (all 819 lines)
- `DSA/Chemistry/EquationsOfState.cs`
- `DSA/Chemistry/HeatCapacityFunctions.cs`
- `Data/Species.cs`
- `Data/Constants.cs`
- `docs/chemistry/solver_debug_2/cearun.txt`
- `docs/chemistry/solver_debug_2/boxsim.txt`
- `docs/chemistry/solver_debug_2/openai_gpt_5_5.md`
- `docs/chemistry/solver_debug_2/anthropic_claude_opus_4_7.md`
- `docs/chemistry/solver_debug_2/deepseek_v4_pro.md`
- `docs/chemistry/solver/kimi_k2_6.md`
- `docs/chemistry/solver_architecture.md`
- `docs/chemistry/energy_minimization_3.md`
- `docs/chemistry/solver_debug/deepseek_v4_pro/epm_nan_fix_journey.md`
- `docs/chemistry/solver_debug/gpt_5_5/water_overproduction_fix.md`

The three prior reviewers have already given correct, detailed answers. I agree with their broad conclusions. This response focuses on implementation-level subtleties in `Volume.cs` that have not been explicitly traced, adds some quantitative verification, and offers a slightly different framing of why the multi-phase problem breaks in this specific codebase.

---

## Question 1: CEA's solution — what's happening, and why is BoxSim different?

### The CEA result is ordinary partial-oxidation chemistry

CEA conserves the reactants' internal energy (`u0/R = -294.3 K`) and the assigned specific volume (`1/rho = 0.15625 m^3/kg`). The element inventory is:

- C: 200 mol (from 200 CH4)
- H: 800 mol
- O: 200 mol (from 100 O2)

With O/C = 1, there is exactly one oxygen atom per carbon atom. Complete combustion to CO2 + H2O would require O/C = 4. The system is four times fuel-rich (`phi = 3.99`). At the UV-equilibrium temperature of 1409 K and 62 bar, the natural carbon carrier is CO (one O per C), not CO2 (two O per C). The CEA result — roughly 57% H2, 30% CO, 6% CH4, 5% H2O, 1% CO2 — is exactly what every thermochemistry textbook predicts for oxygen-starved methane at high temperature.

BoxSim cannot reach this state. The reasons have been enumerated well by the other reviewers. I want to verify and rank them with an eye toward which ones are *fixable without rewriting the solver architecture*.

### Ranking of BoxSim simplifications (by fixability and impact)

**Rank 1 (catastrophic, trivial fix): Missing CO.**
Adding CO to `Data/Species.cs` subset is a one-line change. Without it, the equilibrium problem is geometrically different: the carbon-oxygen stoichiometry is constrained to a subspace that does not contain the true optimum. No solver quality can compensate for a missing species that carries 30% of equilibrium moles.

**Rank 2 (severe, ~10-line fix): `SolveReactions` creates ghost condensed species despite the gas-only mask.**
This has not been explicitly flagged in the prior responses, and it explains the phantom liquid/solid water in the BoxSim trace.

Look at `Volume.cs:557-564`:
```csharp
vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
vec_n = Vector<double>.Build.Dense(s);
for (int j = 0; j < s; j++)
{
    int phase = (int)viewSpecies[j].Phase;
    vec_n[j] = vec_N[phase] * vec_x[j];
}
```

The Newton minor correctly excludes liquid/solid from the Jacobian. But the final application loop computes `vec_n` for **all** species in `viewSpecies`, including condensed phases. `vec_N[liquid]` and `vec_N[solid]` are never updated after construction (they remain at `1e-6 mol` forever because `active_m` skips them in the delta application at line 541-547). If `vec_x[j]` for a condensed species is large — and it can be, because `vec_lambda` is computed without any constraint from the absent phase totals — then `vec_n[j] = 1e-6 * large_number` can exceed `n_jMin`.

DeepSeek's trace documented `x_C(s) = 3905`. With `N_solid = 1e-6`, that gives `n_C(s) = 3.9e-3 mol`, well above the 1e-6 threshold. The same mechanism produces the 8.38 mmol H2O(L) and 3.46 mmol H2O(s) seen in BoxSim's early frames. **The gas-only mask is a Jacobian-only filter, not a species filter.**

The fix is to build `viewSpecies` from gas-only species *before* computing `vec_x` and `vec_n`, or to skip condensed species in the application loop. This is distinct from the phase-solver bug; it is a reaction-solver bug.

**Rank 3 (severe, ~20-line fix): `SolvePhases` is missing the standard chemical potential comparison.**
Both Opus 4.7 and DeepSeek V4 Pro gave excellent derivations. I will add the quantitative check:

At 903 K (BoxSim late state), using NASA9 polynomials:
- C(g): `H° ≈ 714 kJ/mol`, `S° ≈ 157.5 J/mol·K` → `G° ≈ 714000 - 903*157.5 ≈ 571.8 kJ/mol`
- C(gr): `H° ≈ 0`, `S° ≈ 24.5 J/mol·K` → `G° ≈ -22.1 kJ/mol`

The equilibrium condition `μ_Cg = μ_Cgr` gives:
```
G°_Cg + RT ln(P_Cg / P°) = G°_Cgr
P_Cg,eq = P° * exp((G°_Cgr - G°_Cg) / RT)
        = 1e5 * exp((-22100 - 571800) / (8.314 * 903))
        = 1e5 * exp(-79.1)
        ≈ 1e5 * 5.6e-35
        ≈ 6e-30 Pa
```

The equilibrium partial pressure of monatomic carbon over graphite at 903 K is not merely small — it is **thirty-five orders of magnitude** below any representable threshold. The BoxSim `SolvePhases` formula `n_gas_eq = P * V_gas / (RT)` gives ~339 mol. It is wrong by a factor of ~10^35.

The correct `SolvePhases` criterion for a species with gas and condensed phases is:
```
G°_gas(T) + RT ln(φ_gas * P_gas / P°)  vs  G°_cond(T) + v_cond*(P - P°)
```
where `P_gas` is the species partial pressure. For ideal gas + incompressible condensed (Poynting correction negligible), the equilibrium gas moles are:
```
n_gas,eq = (P° * V_gas / RT) * exp((G°_cond - G°_gas) / RT) * (φ_cond / φ_gas)
```

The missing exponential factor `exp((G°_cond - G°_gas)/RT)` is everything. At 903 K for carbon, it is `exp(-79.1) ≈ 5.6e-35`, which drives `n_gas,eq` to exactly zero for all practical purposes. The NASA9 data already contains `G°`; `SolvePhases` just does not use it.

**Rank 4 (moderate, architectural): The per-frame dissociation gate.**
BoxSim equilibrates only the 0.1% dissociated pool each frame. The late-state BoxSim still has 130 mol CH4 because the gate has not yet chewed through it. CEA equilibrates the entire inventory instantly. This is a deliberate gameplay choice, not a bug, but it means single-frame BoxSim states should never be compared to CEA. Only the infinite-time limit should match, and only if the solver is otherwise correct.

**Rank 5 (moderate, architectural): Operator splitting.**
`Solve()` runs Dissociate → Reactions(at old T,P) → UT → VP → Phases. CEA solves composition, T, and P simultaneously. The splitting error is bounded by one frame of change. For a 0.1% dissociation gate, this is small. It becomes significant only when large amounts dissociate at once (e.g., spark overriding the gate).

**Rank 6 (mild): Missing mixing entropy in displayed thermodynamic quantities.**
`DeriveQuantities()` computes:
```csharp
S += resource.SpeciesPhase.HeatCapacityFunction.GetS(T) * n;
```
`GetS(T)` returns the standard-state entropy at 1 bar. The actual entropy of a mixture includes:
- **Pressure correction**: `-R ln(P_j / P°)` for each gas species
- **Mixing entropy**: `-R Σ n_j ln(x_j)` for each phase

For the BoxSim initial state (200 mol CH4 + 100 mol O2 at 731 kPa), the missing pressure correction is about -3.4 kJ/K and the missing mixing entropy is about +1.5 kJ/K, for a net error of roughly -2 kJ/K (about 3% of total S). This does not affect the solver, but it means the BoxSim UI displays thermodynamically inconsistent S, G, and A values. It is a display bug, not a physics bug.

**Rank 7 (mild at 1400 K): Missing radicals and atoms.**
CEA's trace list shows H, O, OH, CH3, etc. at <1e-6 mole fraction. At 1409 K they are genuinely trace. They would become significant above ~2500 K or in highly dissociated plasmas. Not the dominant error here.

---

## Question 2: Why is the solver trying to make gaseous carbon at 1000 K?

The prior reviewers identified three compounding causes. I want to trace the *exact mechanism* through one frame of `Volume.Solve()` to show why the quasi-steady state has 41 mol C(g).

### Frame walkthrough (BoxSim late state, T ≈ 903 K, P ≈ 2.54 MPa)

**State at frame start:**
| Species | Mol |
|---------|-----|
| C(g)    | 40.9 |
| C(gr)   | 0.289 |
| CH4(g)  | 130 |
| CO2(g)  | 29 |
| H2(g)   | 0.162 |
| H2O(g)  | 139 |
| H2O(s)  | 1.25 |
| O2(g)   | 0.952 |

**Step 1: Dissociate**
- 0.1% of 130 mol CH4 → 0.13 mol C + 0.52 mol H enter `freeElements`
- Negligible dissociation from other species (small amounts or high activation energy)

**Step 2: RebuildIndexes**
- `viewSpecies` includes C(g), C(gr), CH4, CO2, H2, H2O(g), H2O(L), H2O(s), O2
- `bitmask` covers {C, H, O}

**Step 3: SolveReactions (gas-only Newton, but all species in view)**
- `active_m = {true, false, false}`
- `vec_N[gas]` is iterated; `vec_N[liquid]` and `vec_N[solid]` stay at `1e-6` (their constructor values, never updated)
- The Newton solve finds `lambda_C`, `lambda_H`, `lambda_O` and `N_gas`
- Carbon has three sinks in the gas phase: CH4, CO2, C(g). CO is absent.
- With O tightly bound in CO2 and H2O, some free C has no oxygen partner. The EPM places it in C(g) because C(gr) is excluded from the active phase set.
- The solver produces, say, 0.05 mol of new C(g) from the 0.13 mol free C.
- **Crucially**, because `viewSpecies` still contains C(gr) and H2O(s), and `vec_N[solid] = 1e-6`, the final application loop may also produce tiny amounts of condensed species if their `vec_x` is large. But the dominant carbon product is C(g).

**Step 4: SolveUT / SolveVP**
- T and P adjust slightly. The ~0.05 mol of new gas species changes total C_v and V slightly.

**Step 5: SolvePhases (for carbon)**
- `phaseResources` for carbon: [C(g) = 40.95 mol, C(gr) = 0.289 mol]
- `n_total = 41.239`
- `V_gas ≈ 1 m^3` (mostly gas)
- Code computes:
  ```csharp
  double n_gas_eq = (phi_cond / phi_gas) * P * V_gas / (Constants.R * T);
  // With phi_cond = phi_gas = 1 (ideal gas / incompressible placeholder):
  n_gas_eq = 2.54e6 * 1 / (8.314 * 903) ≈ 339 mol
  n_gas_eq = Math.Clamp(n_gas_eq, 0.0, n_total) → 41.239 mol
  double n_gas_target = n_gas + Constants.PhaseDamping * (n_gas_eq - n_gas)
                      = 40.95 + 0.5 * (41.239 - 40.95)
                      = 41.095 mol
  ```
- Result: **0.145 mol moves from C(gr) to C(g)** this frame.

**Net effect:** Dissociation produced 0.05 mol C(g). SolvePhases then **converted 0.145 mol of graphite into gas carbon**. The phase solver is not merely failing to condense carbon — it is actively evaporating graphite every frame.

**Why this is stable (the ratchet):**
- `n_gas_eq = P*V/(RT)` is always ~339 mol at this T/P
- Actual carbon inventory is ~41 mol
- `Clamp` sets `n_gas_eq = n_total` (all carbon as gas)
- `PhaseDamping = 0.5` means each frame moves halfway from current gas amount toward "all carbon as gas"
- Since `n_gas_eq > n_gas` always (as long as `n_gas < n_total`), the direction is always solid → gas
- The only reason C(gr) persists at 0.289 mol is that `PhaseDamping = 0.5` is not 1.0; with damping = 1.0, graphite would vanish in one frame

**Root cause hierarchy:**
1. **Missing CO** makes surplus carbon inevitable in an O-poor system.
2. **Gas-only EPM** forces that surplus carbon into C(g) rather than C(gr) during the reaction step.
3. **Broken SolvePhases** not only fails to correct the error — it actively drives the system toward *more* C(g) by ignoring the ~580 kJ/mol standard Gibbs difference between gas carbon and graphite.

If you fix only #1 (add CO), the 41 mol C(g) artifact shrinks dramatically because carbon now has a thermodynamically preferred gas-phase carrier. If you fix only #3 (use `G°` in SolvePhases), any small amount of C(g) created by the reaction solver is immediately condensed to graphite. Both fixes are needed for robust behavior.

---

## Question 3: What makes multi-phase equilibrium harder? Is the outer loop lazy?

### The gas-only problem is smooth because the mixing term provides curvature

For an ideal gas phase at fixed T, P, the Gibbs energy is:
```
G = Σ_j n_j [μ_j°(T,P) + RT ln(n_j / N_gas)]
```
The term `RT n_j ln(n_j / N_gas)` is strictly convex on the interior `n_j > 0`. In the dual formulation, this curvature manifests as the positive-definite Hessian block `Q = view * diag(vec_n) * view^T`. Newton's method converges quadratically because the saddle-point system `[Q D; D^T 0]` is well-conditioned when all active species have significant `n_j`.

### Pure condensed phases remove curvature

A pure condensed species (e.g., C(gr)) has no mixing term. Its chemical potential is constant with respect to amount:
```
μ_Cgr = G°_Cgr(T) + v(P - P°)   (independent of n_Cgr)
```
Its contribution to G is `n_Cgr * μ_Cgr`, which is **linear** in `n_Cgr`. The dual formulation reflects this: the exponent `x_Cgr = exp((-μ_Cgr + Σ λ_i n_iCgr)/RT)` depends only on `lambda`, not on `N_solid`. The normalization equation `Z_solid = x_Cgr + ... = 1` is enforced, but the "restoring force" that pulls `N_solid` toward a stable value comes entirely from the coupling with other species in the same phase. If a phase has only one species (e.g., a pure solid), its normalization is trivially satisfied and provides no information about `N_solid`.

This is why the Jacobian becomes singular when a phase has fewer independent species than the dimension of the element space they span. The D column for that phase becomes rank-deficient with respect to the gas-phase D columns, and the SVD solver discards the small singular values — which correspond to the very direction (condensed phase amount) you are trying to solve for.

### Why minoring out liquids/solids is not "lazy" — it is an incomplete version of the correct algorithm

Your feeling that it is lazy is partially correct, but the laziness is not in avoiding work. The laziness is in **not telling the solver that the problem dimension has changed**.

The mathematical reality is:
- With only gas active: the dual problem has `a+1` unknowns and `a+1` equations. The equations are smooth and meaningful.
- With gas + liquid active: the dual problem has `a+2` unknowns and `a+2` equations. The new equation (`Z_liquid = 1`) is only meaningful if `N_liquid > 0`.
- At the boundary where `N_liquid → 0`, the equation system changes discontinuously. The liquid normalization equation goes from "enforce mole fractions" to "do not enforce anything, the phase does not exist."

Newton's method cannot handle this discontinuity. It requires a fixed equation system with a fixed number of variables. The outer loop is the standard way to handle piecewise-smooth problems: solve on one smooth patch (active set), check if the patch boundary has been crossed (phase stability test), and switch patches if necessary.

So the CEA/STANJAN outer loop is not a kludge. It is the exact numerical expression of a physical fact: **phases are discrete**. In real thermodynamic systems, a phase either exists or it does not. There is no such thing as a phase with `N = 10^-12` moles that still obeys normalization equations. The outer loop respects this discreteness. Your current code, by clamping `N_m` to `1e-6`, is actually the one using a physically fictitious smoothing — it forces a phase to persist with a microscopic amount so that the equations don't change dimension.

### Is the problem non-convex?

At fixed T and P with ideal mixtures, the primal Gibbs minimization is still convex. The feasible set (element balances, `n_j >= 0`) is a convex polytope. The objective is convex: sum of strictly convex mixing terms for multi-species phases plus linear terms for pure condensed phases. A linear term does not break convexity; it just means the optimum lies on a boundary rather than in the interior.

The practical difficulty is not non-convexity in the optimization sense. It is:
1. **Nonsmoothness**: The objective is not differentiable at `n_j = 0` for pure condensed phases.
2. **Active-set combinatorics**: The number of possible phase combinations grows exponentially. You need to find which subset is active at the optimum.
3. **Jacobian rank deficiency**: When a phase total `N_m` approaches zero, the corresponding rows/columns in the saddle-point system lose independence.
4. **Scaling**: With `N_gas ≈ 400` and `N_solid = 1e-6`, Jacobian entries span 6+ orders of magnitude. Without scaling, floating-point truncation destroys the small singular values.

### What I recommend for Pile Simulator 3

The other reviewers recommended: gas-only `SolveReactions` + proper `SolvePhases` using `G°`. I agree, with one implementation clarification.

**The current minoring is unsafe because it does not filter the species list.** You should do one of the following:

**Option A (cleanest):** Build `viewSpecies` as gas-only inside `SolveReactions`.
```csharp
// Inside SolveReactions, after GetView:
var gasIndices = Enumerable.Range(0, s).Where(j => viewSpecies[j].Phase == Phase.Gas).ToArray();
int sGas = gasIndices.Length;
// Build a reduced view, vec_mu, etc. using only gas species
```
This guarantees no condensed species are created by the reaction solver. The condensed phase amounts then come entirely from `SolvePhases`, which you rewrite to compare `G°` values.

**Option B (minimal change):** Keep the full view but skip condensed species in the application loop:
```csharp
for (int j = 0; j < s; j++)
{
    if (viewSpecies[j].Phase != Phase.Gas) continue;
    // apply vec_n[j] to Resources
}
```

**For `SolvePhases`, the correct criterion for species `j` with gas and condensed phases is:**
```csharp
// For gas phase:
double mu_gas = speciesPhaseGas.HeatCapacityFunction.GetH(T) - T * speciesPhaseGas.HeatCapacityFunction.GetS(T)
              + Constants.R * T * Math.Log(phi_gas * P_gas / Constants.bar);

// For condensed phase (pure, incompressible approximation):
double mu_cond = speciesPhaseCond.HeatCapacityFunction.GetH(T) - T * speciesPhaseCond.HeatCapacityFunction.GetS(T);
// Optional Poynting correction: + v_cond * (P - Constants.bar)

// Equilibrium gas moles:
// mu_gas(T, P_gas) = mu_cond(T, P)
// P_gas,eq = (Constants.bar / phi_gas) * exp((mu_cond - mu_gas_at_Pbar) / (R*T))
// where mu_gas_at_Pbar = H° - TS° + RT*ln(phi_gas_at_Pbar)
// n_gas,eq = P_gas,eq * V_gas / (R*T)
```

At 1000 K for carbon, `mu_cond - mu_gas` is roughly `-580 kJ/mol`, so `n_gas,eq` is driven to effectively zero. At 373 K for water, `mu_cond - mu_gas` is roughly zero (the boiling point), so `n_gas,eq` gives a meaningful vapor pressure. This single formula works correctly for both cases.

### Direct, short answers

1. **CEA found H2 + CO because O/C = 1 makes partial oxidation the natural chemistry.** The UV equilibrium at 1409 K / 62 bar is standard for fuel-rich methane. BoxSim differs primarily because CO is missing from the species list, but also because: (a) the reaction solver's gas-only mask does not actually filter condensed species from `viewSpecies`, creating ghost liquid/solid water; (b) `SolvePhases` lacks the standard Gibbs term `G°`, making it thermodynamically blind; (c) only 0.1% of inventory equilibrates per frame; (d) T/P/composition are operator-split; (e) displayed S/G/A omit mixing entropy and pressure corrections. Missing radicals are negligible at 1409 K.

2. **Gaseous carbon persists because `SolvePhases` evaporates graphite.** The gas-only EPM (with CO absent) creates some C(g). `SolvePhases` then computes an "equilibrium" gas amount of `n_gas_eq = P*V/(RT) ≈ 339 mol`, clamps it to the total carbon inventory (~41 mol), and damp-moves toward that target every frame. Because `n_gas_eq` is always clamped to `n_total`, the direction is always solid → gas. The missing `G°_Cgr - G°_Cg ≈ -580 kJ/mol` term corresponds to an equilibrium pressure ratio of `exp(-70) ≈ 10^-30`, which would drive `n_gas,eq` to zero instantly.

3. **Multi-phase equilibrium is harder because phase existence is a discrete decision, not a continuous equation.** The underlying Gibbs minimization remains convex, but pure condensed phases contribute linear (not strictly convex) terms to G, placing their optima at boundaries. When a phase total `N_m → 0`, the normalization equation becomes mathematically meaningless, and the Jacobian loses rank. The CEA/STANJAN outer loop is not lazy — it is the standard and correct way to solve a piecewise-smooth problem whose equation system changes dimension at phase boundaries. Your current approach of clamping `N_m` to `1e-6` is actually the smoothing approximation; it prevents the dimension change but introduces phantom phases that distort the element balance. For Pile Simulator 3, I recommend: (1) truly filter `viewSpecies` to gas-only in `SolveReactions`; (2) rewrite `SolvePhases` to use `G° = H° - TS°` comparisons; (3) add CO. Do not implement a full active-set loop until these two pieces are correct in isolation.
