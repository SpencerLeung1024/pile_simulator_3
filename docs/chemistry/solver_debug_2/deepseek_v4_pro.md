# Council response: deepseek/deepseek-v4-pro

Date: 2026-05-23

Reviewed code/docs:

- `DSA/Chemistry/Volume.cs` (all 819 lines, every method)
- `DSA/Chemistry/EquationsOfState.cs` (IncompressiblePhaseEquation.GetLogphi, IdealGasEquation)
- `DSA/Chemistry/HeatCapacityFunctions.cs` (NASA9: GetH, GetS contract)
- `Data/Species.cs` (SpeciesPhase.Getmu, AllSpecies.Initialize subset, FormulaTable view builder, active-mask interaction)
- `Data/Constants.cs` (solver tolerances, PhaseDamping, n_jMin, N_mMin)
- `Scripts/BoxSim.cs` (per-frame Solve loop, spark behavior, initial condition)
- `docs/chemistry/solver_debug_2/cearun.txt` (CEA2 uv problem, phi=3.99, product distribution)
- `docs/chemistry/solver_debug_2/boxsim.txt` (actual runtime output, two quasi-steady states)
- `docs/chemistry/solver_debug_2/openai_gpt_5_5.md`
- `docs/chemistry/solver_debug_2/anthropic_claude_opus_4_7.md`
- `docs/chemistry/dual_problem/*`
- `docs/chemistry/solver/*`
- `docs/chemistry/solver_debug/*`

Both prior reviewers have already identified the major issues correctly. I agree with their broad conclusions. This response focuses on points of disagreement, amplification, or synthesis that neither provided, plus a more quantitative trace through the equations to verify their qualitative claims.

## Executive summary

CEA is correct. The BoxSim result is wrong at every level: the species basis omits CO (the dominant product at this equivalence ratio), the EPM is gas-only and cannot place carbon into graphite, and the phase solver's equilibrium criterion is thermodynamically nonsensical for species whose gas and condensed phases differ in standard chemical potential by hundreds of kJ/mol. SolvePhases is not merely "missing mu^0" -- at the BoxSim operating point it actively *evaporates* graphite into monatomic carbon vapor via a ratchet effect: `n_gas_eq = P * V_gas / (RT)` computes to ~340 mol at 2.5 MPa / 900 K, which exceeds the actual carbon inventory (~41 mol), so every damped step pushes more carbon into the gas phase. The 40.9 mol C(g) at quasi-steady state is not a leftover from a previous solver pass -- it is the equilibrium toward which the phase solver itself is driving the system.

## Question 1: CEA's solution -- what's happening, and why?

### The CEA case defined

CEA solves `uv` (assigned internal energy, assigned volume). Input: 50 wt% CH4, 50 wt% O2, density 6.4 kg/m^3. The reactants are at 293 K, 1 bar. CEA sums the total internal energy from the reactant formation enthalpies plus the sensible energy at 293 K (`u0/R = -0.294323e3`), and conserves it. It finds `T = 1408.81 K`, `P = 62.333 bar`.

### The fundamental chemical reality

The atom budget is:
- C: 200 (from 200 CH4)
- H: 800
- O: 200 (from 100 O2)

With O/C = 1, there is exactly one oxygen atom per carbon atom. Complete combustion (`CH4 + 2 O2 -> CO2 + 2 H2O`) requires O/C = 4. The fuel/oxidizer mass ratio of 1:1 gives phi = 3.99, four times fuel-rich.

The "natural" stoichiometry for an O/C = 1 system is partial oxidation/reforming:
```
CH4 + 0.5 O2 -> CO + 2 H2
```
which gives 200 CO and 400 H2 at full conversion. CEA's actual result at 1409 K reflects this plus a modest water-gas shift:
```
CO + H2O <-> CO2 + H2    (K_eq ~ 1.2 at 1400 K)
```
and some residual methane from steam reforming equilibrium:
```
CH4 + H2O <-> CO + 3 H2
```

The final mole fractions (CO: 0.300, H2: 0.574, CH4: 0.062, H2O: 0.051, CO2: 0.012) are a perfectly ordinary partial-oxidation equilibrium for these conditions. There is no mystery.

### Why BoxSim is different, ranked by impact

The difference between CEA and BoxSim is not one error but a cascade. I'll rank the simplifications by their quantitative effect on the carbon distribution:

**Rank 1: Missing CO (catastrophic).** Without CO in the species basis, every carbon atom that leaves CH4 must go to CO2 (consuming 2 O per C), C(g) (consuming 0 O), or C(gr) (consuming 0 O, but the EPM is gas-only). The optimization problem is fundamentally different. CEA can route carbon through a species with O:C = 1; BoxSim cannot. The equilibrium of the species subset *cannot* match the equilibrium of the full set, regardless of solver quality. Adding CO alone would shift the result from CO2+H2O-dominated to CO+H2-dominated, matching CEA's qualitative picture.

**Rank 2: Gas-only EPM (severe).** The `active_m = {true, false, false}` mask means the Newton solver in SolveReactions can only produce C(g), never C(gr) as a major product. C(gr) is still in `viewSpecies` but its phase total `N_solid` is clamped to `N_mMin = 1e-6 mol` for the entire duration of the Newton solve. No matter how thermodynamically favorable graphite is, the EPM expresses that preference as an enormous `x_Cgr` multiplied by a negligible `N_solid`. The carbon that should be graphite ends up as C(g) because that's the only carbon-bearing species in the gas phase that doesn't consume oxygen.

The math: with both C(g) and C(gr) in the view, the EPM mole fraction formula gives:
```
x_Cg  = exp((-mu_Cg  + lambda_C) / RT)
x_Cgr = exp((-mu_Cgr + lambda_C) / RT)
```
At 900 K, `mu_Cg - mu_Cgr ≈ 717 kJ/mol` (atomization energy of graphite), so:
```
x_Cg / x_Cgr = exp(-717000 / (8.314 * 900)) ≈ 2.7e-42
```
But the *amounts* are:
```
n_Cg  = N_gas    * x_Cg    ≈ 400 * x_Cg
n_Cgr = 1e-6     * x_Cgr   (clamped)
n_Cg / n_Cgr = (400 / 1e-6) * 2.7e-42 ≈ 1.1e-33
```
Even with graphite's overwhelming thermodynamic preference, the gas-only mask forces more carbon into C(g) by a factor of N_gas / N_mMin = 4e8. However, this ratio is only ~10^8 -- it doesn't overcome the ~10^42 thermodynamic preference. The gas-only mask *alone* would not produce 41 mol C(g) if the lambda values correctly reflect the underlying Gibbs minimization. The problem is the compound effect with the broken SolvePhases.

**Rank 3: Broken SolvePhases (severe, creates the C(g) puddle).** Cross-verified against `Volume.cs:730-742`:

```csharp
double v_gas = V_gas / Math.Max(n_gas, Constants.n_jMin);
double P_gas = gasResource.SpeciesPhase.EquationOfState.GetP(T, v_gas);
double phi_gas = Math.Exp(gasResource.SpeciesPhase.EquationOfState.GetLogphi(T, P_gas, v_gas));
double phi_cond = Math.Exp(condResource.SpeciesPhase.EquationOfState.GetLogphi(T, P, v_cond));
double n_gas_eq = (phi_cond / phi_gas) * P * V_gas / (Constants.R * T);
```

With ideal gas (GetP gives `RT/v_gas`, GetLogphi gives 0) and incompressible condensed (GetLogphi gives 0), this simplifies to:
```
f_gas  = φ_gas * P_gas  = 1 * (n_gas * RT / V_gas) = n_gas * RT / V_gas
f_cond = φ_cond * P     = 1 * P
```
Setting f_gas = f_cond yields:
```
n_gas_eq = P * V_gas / (R * T)
```
At the BoxSim late state (P = 2.54 MPa, T = 903 K, V_gas ≈ 1 m^3):
```
n_gas_eq = 2.54e6 * 1 / (8.314 * 903) ≈ 339 mol
```
This is the "equilibrium" gas amount. But total carbon is only ~41.2 mol. The `Math.Clamp(n_gas_eq, 0.0, n_total)` makes n_gas_eq = 41.2 -- *the formula says carbon at system pressure as an ideal gas is in equilibrium with graphite*. Since n_gas ≈ 40.9 mol and the damped target n_gas_target = 40.9 + 0.5*(41.2 - 40.9) ≈ 41.05 mol, each call of SolvePhases *increases* the gas carbon amount.

This is not failing to condense carbon. This is actively evaporating graphite at 900 K. The phase solver is computing that monatomic carbon vapor at 2.5 MPa is thermodynamically equivalent to graphite -- a statement that is wrong by the graphite atomization energy (~717 kJ/mol). At the real equilibrium, `P_Cg(eq) ≈ P0 * exp((mu^0_Cgr - mu^0_Cg)/RT) ≈ 1e5 * exp(-96) ≈ 2e-37 Pa`, i.e., no gaseous carbon whatsoever.

**Rank 4: The per-frame dissociation gate (moderate, explains residual CH4).** CEA equilibrates the *entire* inventory of 200 CH4 + 100 O2. BoxSim dissociates at most `DissociationThreshold = 0.1%` per frame (= 0.2 mol CH4/frame), stores atoms in `freeElements`, and equilibrates *only that pool*. The late-state BoxSim still has 130 mol unreacted CH4. This is expected under the gate model. Unless the spark overrides the gate entirely, BoxSim will never reach CEA's state because less than the full inventory participates in equilibrium.

**Rank 5: Operator split (moderate).** `Solve()` runs Dissociate -> Rebuild -> Derive -> SolveReactions(EMO) -> SolveUT(T) -> SolveVP(P) -> Rebuild -> SolvePhases. CEA solves composition, T, and P simultaneously. The sequential solver computes reaction equilibrium at the *old* T and P, adjusts T to conserve U based on the resulting composition, then adjusts P based on the new T and composition. The coupling error is bounded by one frame of realistic changes but prevents exact matching.

**Rank 6: Missing radicals (mild at 1400 K).** CEA's species list includes H, O, OH, CH3, CH, CH2, HO2, HCO, etc. These carry only trace amounts at 1409 K (most are <1e-6 in the trace list), but they absorb high-temperature dissociation that BoxSim's closed-shell-only basis cannot represent. At higher temperatures this would become the dominant error source.

**Rank 7: Placeholder condensed EOS (mild for this case).** `v = 0` for condensed phases erases their volume and sensible heat capacity contributions. This shifts the pressure/volume relationship and total C_v slightly but is a second-order effect below 1500 K.

**Rank 8: `Getmu` using system P (algebraically correct, not an error).** Both prior reviewers correctly note that `Getmu(T, P, x_j=1)` uses system pressure `P` for all gas species. The term `RT ln(P/bar)` is identical for every gas species and cancels out of the exponential ratios in `x_j`. It gets absorbed into `N_gas` during normalization. This is correct for the EPM. The real issue is that SolvePhases never consults `mu^0_j` at all.

### A clarifying note on Getmu

The code at `Species.cs:98-126` correctly implements:
```
mu_j = H(T) - TS(T)                      [gibbs_term, from NASA9]
     + RT ln(x_j)                         [mixing_term, skipped when x_j=1]
     + RT ln(phi * P / bar)               [pressure_term, gas only]
```
This is what the EPM needs. `H(T)` from NASA9 includes the formation enthalpy via the `a_6` coefficient. The `RT ln(P/bar)` term is a constant offset for all gas species in a given frame (same T, same P). The EPM's mole fraction formula:
```
x_j = exp((-mu_j + sum_i lambda_i * n_ij) / RT)
     = exp((-H_j + TS_j)/RT) * (bar/P) * exp(sum_i lambda_i * n_ij)
```
shows that the `(bar/P)` factor is identical for all gas species and commutes with the phase normalization equation. The lambdas implicitly absorb it. No bug here.

## Question 2: Why gaseous carbon at 1000 K?

The proximate cause is compound. The prior reviewers correctly identified three causes. I want to add precision on the *mechanism*, because the fix depends on which cause dominates at which stage of the solve pipeline.

### Mechanism: a ratchet in the phase solver

The BoxSim quasi-steady state has:
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

Walk through one frame to see the ratchet:

1. **Dissociate:** 0.1% of ~130 mol CH4 → 0.13 mol C + 0.52 mol H enters freeElements. Negligible from CO2/H2O/H2 since these have small amounts or don't dissociate.

2. **SolveReactions (gas-only):** freeElements has C, H, O atoms. The EPM with gas-only active set produces n_j amounts. The available species for carbon placement are C(g), CH4(g), CO2(g). Since CO is absent and the O budget is tight, some carbon must go to C(g). The amount from a single frame is small (~0.01-0.1 mol) and accumulates in the resource.

3. **SolvePhases for carbon:** Now the Volume has both C(g) and C(gr) resources. The phase solver computes:
   ```
   n_gas_eq = P * V_gas / (RT) ≈ 339 mol at 2.5 MPa, 900 K, 1 m^3
   ```
   Clamped to `n_total = n_gas + n_cond ≈ 41.2 mol`. So `n_gas_eq = 41.2`.
   
   `n_gas_target = 40.9 + 0.5 * (41.2 - 40.9) = 41.05`
   
   The phase solver moves 0.15 mol from C(gr) to C(g) this frame. Not from C(g) to C(gr).

4. **Next frame:** Repeat. Each frame the phase solver takes a half-step toward putting ALL carbon in the gas phase. This is not random drift -- it is the deterministic equilibrium of a wrong formula.

The ratchet is one-directional because n_gas_eq = P*V/(RT) is always larger than the actual carbon inventory at realistic P, T values for this box (1 m^3, reasonable pressures). At P = 100 kPa, n_gas_eq = 13.4 mol; at P = 1 MPa, 134 mol; at P = 2.5 MPa, 339 mol. The formula places the equilibrium at "gas carbon fills the entire box at system pressure," which requires more gas moles than physically present for any P > ~300 kPa.

### The break-after-first logic compounds this

`SolvePhases` `Volume.cs:754` has:
```csharp
break; // One condensed phase per species per SolvePhases call
```
For a species with both liquid and solid forms plus gas, only one condensed phase gets processed per frame. This isn't the primary problem here (carbon has only gas + solid), but it means multi-phase species (like water) would require multiple frames for full phase equilibration even if the formula were correct.

### Root cause hierarchy

1. **CO missing** (the necessary condition): without CO, carbon atoms that cannot oxidize to CO2 have nowhere sensible to go.
2. **Gas-only EPM** (the enabling condition): the EPM's active set prevents C(gr) from being a first-class equilibrium product.
3. **Broken SolvePhases formula** (the proximate cause of the visible 41 mol): the phase solver not only fails to correct the EPM's C(g) -- it actively amplifies it.

### Why CEA doesn't make gaseous carbon

CEA considers C(g) but assigns it mole fraction < 1e-6 (it appears in the "considered but trace" list, line 171). The reason is threefold: CEA (a) has CO to carry carbon, (b) activates graphite as a condensed phase when the stability test passes, and (c) compares `mu^0_Cg` vs `mu^0_Cgr` correctly through the Gibbs minimization framework, not through a hand-rolled fugacity ratio.

## Question 3: What makes multi-phase equilibrium harder?

Both prior reviewers gave correct answers here. I will add specificity on *why* the current code's attempt failed, beyond the conceptual reason.

### The gas-only problem is smooth and self-regularizing

For the gas-only EPM at fixed T, P, the system of (a + 1) equations in (a + 1) unknowns is:
```
H_i(lambda, N_gas) = N_gas * sum_{j in gas} n_ij * exp((-mu_j + view^T * lambda) / RT) - p_i = 0
Z(lambda, N_gas)  = sum_{j in gas} exp((-mu_j + view^T * lambda) / RT) - 1 = 0
```
The Jacobian quadrant Q (a x a) is `view * diag(vec_n) * view^T`, which is the Hessian of the dual objective. For any interior point where all active species have n_j > 0, Q is positive definite. The full saddle-point system `[Q D; D^T 0]` is invertible (provided no element is completely absent from the active species). Newton converges quadratically.

### Adding condensed phases breaks the structure

With condensed phases active, each phase m gets its own `N_m` and normalization equation:
```
Z_m(lambda, N_1, ..., N_p) = sum_{j in m} exp((-mu_j + view^T * lambda) / RT) - 1 = 0
```
This is structurally identical to the gas phase -- the same mixing term `RT ln(x_j)` applies if we model condensed phases as ideal solutions. The problem is not fundamentally different equations; it is a larger system of the same form.

The failure in the earlier code (before GPT-5.5's minoring) was not that the equations don't exist -- it was that the system becomes singular or near-singular when a phase has strictly fewer independent species than elements it can represent, combined with phases whose molar amounts are wildly mismatched.

### Tracing the solid-phase phantom to its source

Consider the system with 3 elements (C, H, O) and phases:
- Gas: C(g), H2(g), O2(g), CH4(g), H2O(g), CO2(g)
- Liquid: H2(L), O2(L), CH4(L), H2O(L)
- Solid: C(gr), H2O(cr)

The solid phase has only two species. Its normalization equation is:
```
x_Cgr + x_H2O_cr = 1
```
The solid phase's dual variable (N_solid's Lagrange multiplier) is supposed to enforce this. But the element columns for C(gr) = [1,0,0] and H2O(cr) = [0,2,1] span a 2D space within the 3-element space. The solid phase's D column is `[x_Cgr, 2*x_H2O_cr, x_H2O_cr]^T`. Near the optimum, if graphite is overwhelmingly preferred (x_Cgr ≈ 1, x_H2O_cr ≈ 0), D_solid ≈ [1, 0, 0]^T. This column is nearly collinear with D_gas's column (since gas species also span the C direction). The Jacobian becomes rank-deficient.

With the SVD solver, the small singular values get truncated, routing the Newton correction into the larger singular values -- which means into λ_C and N_gas, not into N_solid. The solid normalization equation's residual never decreases because the SVD has "decided" it's not a useful correction direction. This is the "solid phase phantom" from `epm_nan_fix_journey.md`.

### Is the underlying problem convex?

At fixed T, P, the primal Gibbs minimization:
```
minimize G(n) = sum_j n_j * mu^0_j + RT sum_j n_j * ln(n_j / N_m(j))
subject to A * n = b,  n_j >= 0
```
where A is the stoichiometry matrix and b is the element inventory, is convex for ideal mixtures. The objective is strictly convex where all n_j > 0 (the logarithmic terms provide curvature). Pure condensed species (x_j = 1 always, no mixing term) contribute `n_j * mu^0_j`, which is linear -- the optimum for these species sits at a boundary (n_j = 0 or n_j = large). But the problem remains convex -- the feasible set is a polytope and the objective is convex.

The "non-convexity" language is imprecise. The actual difficulty is:
1. **Active set identification.** Which phases exist at the optimum is a combinatorial decision. The KKT conditions for phase existence are inequalities, not equalities. Newton solves equalities.
2. **Jacobian singularity at phase boundaries.** When N_m → 0 for a phase with fewer independent species than elements, the D columns become rank-deficient with other phases' D columns.
3. **Bad scaling.** When N_m = 1e-6 and N_gas = 400, the Jacobian entries for condensed phase rows/columns are 4e8 times smaller than gas entries. This is not insolvable with good scaling, but the current code doesn't scale.
4. **Lambda initialization.** The Gram-Schmidt basis selection from the lowest-mu gas species may produce lambda values that make condensed x_j explode to 10^3+, creating the 3904 residual that DeepSeek documented.

### Is the CEA/STANJAN outer loop "lazy"?

No. It is the correct expression of the mathematical structure. The phase rule of Gibbs says that at equilibrium, f = c - p + 2 degrees of freedom exist, where p is the number of phases *that actually exist*. You don't know p a priori. The outer loop:
```
while true:
    solve equilibrium for current active set
    test inactive phases for appearance (compare mu^0 to element-potential sum)
    test active phases for disappearance (n_m < threshold)
    if no changes: break
```
is solving a mixed complementarity problem by iterating on the discrete decision. The alternative (smoothing via interior-point methods or phase regularization) doesn't eliminate the decision; it just hides it inside a numerical scheme.

That said, for Pile Simulator 3, I agree with both prior reviewers: the outer loop is overkill for now. The gas-only EPM plus a properly written SolvePhases (using `mu^0_j` comparison) will produce physically correct chemistry for all practical cases in a game. The operator-split approach (reactions, then T, then P, then phases) is valid for gameplay time scales as long as each component is thermodynamically correct.

### A note on ideal condensed solutions

The current SpeciesPhase architecture (mixing terms in Getmu, phase normalization in the EPM) already assumes ideal condensed solutions. If you want pure condensed phases (x_j = 1 always), the EPM dual formulation changes: the condensed species contribute `n_j * mu^0_j` linearly to G, and the KKT conditions become complementarity inequalities rather than normalization equations. This is a fundamentally different problem structure. For now, treating condensed phases as ideal solutions with small amounts in the EPM and then using SolvePhases to keep them pure is a pragmatic hybrid.

## Direct, short answers

1. **CEA found H2 + CO because the mixture is phi = 3.99 (four times fuel-rich).** O/C = 1 means exactly one oxygen per carbon -- the natural carrier is CO (one O per C), not CO2 (two O per C). The equilibrium at 1409 K and 62 bar is a standard partial-oxidation/reforming mixture. BoxSim cannot reach this because CO is not in the species list. Other simplifications that prevent matching: gas-only EPM (C(gr) cannot be a major product), a broken SolvePhases that actively evaporates graphite, the per-frame dissociation gate (only 0.1% participates per frame), and sequential operator splitting. Missing radicals are minor at this temperature.

2. **The solver makes gaseous carbon because the phase solver drives it there.** The EPM (gas-only, no CO) produces some C(g) as the only available carbon sink that doesn't consume oxygen. SolvePhases then computes `n_gas_eq = P * V_gas / (RT) ≈ 339 mol` at the operating point, which exceeds the actual carbon inventory of ~41 mol. The damped step toward this "equilibrium" moves carbon from solid to gas. The missing `mu^0_Cg - mu^0_Cgr` term (~717 kJ/mol at 1000 K, corresponding to a pressure ratio of e^-96) is not just missing -- its absence makes the phase solver's formula actively incorrect about the *direction* of phase transfer.

3. **Multi-phase equilibrium is harder because phase existence is a complementarity condition, not an equation.** At fixed T, P with ideal mixtures, the underlying problem is still convex -- the difficulty is algorithmic, not a loss of convexity. Pure condensed species contribute linear terms to G, placing their optima at boundaries. When the active set is wrong, the Newton system's equations are literally the wrong equations (normalizing mole fractions for a phase that should not exist). Phase boundaries create rank-deficient D columns in the Jacobian. The CEA/STANJAN outer loop is the standard, correct solution to the mixed complementarity problem. For Pile Simulator 3, I recommend keeping gas-only SolveReactions and rewriting SolvePhases to use `mu^0_j` from NASA9. The outer loop can wait until both pieces work independently.
