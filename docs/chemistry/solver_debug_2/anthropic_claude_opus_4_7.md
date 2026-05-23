# Council response: anthropic/claude-opus-4.7

Date: 2026-05-23

Reviewed code/docs:

- `DSA/Chemistry/Volume.cs`
- `DSA/Chemistry/EquationsOfState.cs`
- `DSA/Chemistry/HeatCapacityFunctions.cs`
- `Data/Species.cs` (esp. `AllSpecies.Initialize` subset and `SpeciesPhase.Getmu`)
- `Data/Constants.cs`
- `docs/chemistry/solver_debug_2/cearun.txt`
- `docs/chemistry/solver_debug_2/boxsim.txt`
- `docs/chemistry/solver_debug_2/openai_gpt_5_5.md`
- `docs/chemistry/solver/opus_4_7.md`
- `docs/chemistry/dual_problem/opus_4_7.md`

I have read GPT-5.5's note in this folder. I agree with most of it. This response focuses on the specific three questions and tries to add the bits I think GPT-5.5 missed or got slightly wrong, rather than re-deriving the same conclusions.

## Question 1: What is CEA computing, and why that solution?

### What the CEA case actually is

`cearun.txt` is a `uv` problem (assigned internal energy, assigned volume) with `CH4` as fuel and `O2` as oxidizer, equal mass of each. CEA does not see "200 mol CH4 + 100 mol O2 at 293 K, 1 bar". It sees:

- effective fuel: pure CH4 at 293 K
- effective oxidizer: pure O2 at 293 K
- `O/F` mass ratio = 1.0, equivalent to `phi = 3.99` (four times fuel-rich)
- assigned density `rho = 6.4 kg/m^3` (matches BoxSim's 6.41 kg / 1 m^3)
- assigned `u0/R = -2.943e2 (kg-mol)(K)/kg`, derived from the reactants' standard-state enthalpies and the constraint that the mixture starts at 293 K, 1 bar

That assigned internal energy carries CH4's `-74.6 kJ/mol` formation enthalpy and O2's `0`. CEA conserves *that* `U` and the assigned `V`, then asks: across the entire candidate species list, what composition and `T` minimize `G` at the equilibrium `T, P`?

Because the reactants' `U` includes the energy that would be released by combustion if it ran to completion, CEA finds the temperature `T = 1409 K` and pressure `P = 62.3 bar` at which a four-times fuel-rich C/H/O mixture sits at equilibrium. That is not a real flame temperature; it is the UV-equilibrium temperature for this specific element inventory and energy budget.

### Why the products are mostly H2 + CO

Element balance for the inventory is:

- H: 800 atoms (from 200 CH4)
- C: 200 atoms
- O: 200 atoms (from 100 O2)

So `H / C = 4`, `O / C = 1`. With one O per C, the "natural" carbon carrier is `CO`, not `CO2`. Carrying carbon as `CO2` would require two O per C, which would force half the carbon to be elemental. Carrying carbon as `CO` lets all the carbon be oxidized once and leaves all 800 H atoms free to become 400 H2.

Idealized stoichiometry:

```text
200 CH4 + 100 O2 -> 200 CO + 400 H2
```

That gives `200 / 600 = 0.333` mole fraction CO and `400 / 600 = 0.667` H2. CEA finds 0.300 CO and 0.574 H2, plus a few percent residual `CH4`, `H2O`, `CO2`, and trace radicals. The deviation from the idealized stoichiometry is the usual water-gas shift plus partial-reforming chemistry:

```text
CO + H2O <-> CO2 + H2     (water-gas shift, K_eq ~ 1 around 1400 K)
2 CO     <-> CO2 + C(gr)  (Boudouard, slightly disfavored at this T/P)
CH4 + H2O <-> CO + 3 H2   (steam reforming)
```

At 1409 K the equilibrium constants of these are all in the range where ~1% `CO2`, ~5% `H2O`, ~6% `CH4` and ~30% `CO` is a self-consistent mixture. The numbers are not memorizable but the *shape* is: oxygen-poor, carbon-rich, hot mixtures want `CO + H2`, not `CO2 + H2O`. This is also why partial oxidation reformers, gasifiers, and rocket fuel-rich preburners exist.

CEA is correct. There is nothing mysterious about its solution.

### Other simplifications in BoxSim that push the equilibrium

GPT-5.5 listed missing species, gas-only EPM, placeholder condensed EOS, missing condensed `mu^0`, operator splitting, and the dissociation gate. I agree with all of those. Three additions:

1. **The reactant pool BoxSim equilibrates is fundamentally different from CEA's.**

   CEA equilibrates *the whole inventory*. BoxSim's `SolveReactions` equilibrates only `freeElements`, the per-frame dissociated pool. Even with infinite frames and a perfect solver, BoxSim will only ever approach equilibrium of a moving subset of the inventory, and only the species that have actually dissociated contribute atoms. If CH4 dissociates faster than CO2 (or vice versa), the trajectory through composition space is not the CEA trajectory. This is intentional for gameplay, but it means CEA is the wrong reference for any single BoxSim frame.

2. **`Getmu(T, P, x_j = 1)` evaluates the chemical potential of every species at its own pure-gas standard state, not at the partial pressure it would actually occupy.**

   This is correct for the EPM formulation in the dual problem doc, but it means the "pressure term" inside `Getmu` is `RT ln(P / bar)`, where `P` is the *system* pressure (~2.5 MPa in the late state), not the species' partial pressure. The system pressure cancels out of equilibrium ratios when all candidate species share it, so the algebra is still correct for gas reactions. But it means the absolute `mu` numbers being compared between gas and condensed phases in `SolvePhases` are not directly meaningful, because the gas `mu` is referenced to the system pressure while the condensed `mu` is referenced to zero pressure. See question 2.

3. **The reactant temperature is wrong relative to CEA's `U` reference.**

   CEA's `u0/R` is computed from formation enthalpies at 298.15 K (the NASA9 reference), then cooled to 293 K by the integrated `c_p`. BoxSim sets `T = 293.15` (NIST normal) and `P = 1 bar`, then computes `U` from `GetU(T, v) = H(T) - RT` for gases. That gives a starting `U` that differs from CEA's `u0/R` by however far the NASA9 polynomials for CH4 and O2 deviate from the published formation enthalpies. For combustion-scale energies this is small, but it is one more reason not to expect bit-for-bit matching.

The summary: CEA's solution is correct and reflects oxygen-poor C/H/O chemistry. BoxSim cannot reach it because (a) the species set has no `CO`, the dominant carbon carrier; (b) the equilibrium target is a moving sub-pool, not the full inventory; (c) the phase solver is not actually comparing chemical potentials of condensed phases. Even fixing the species set, the per-frame dissociation gate means BoxSim's late state is not "CEA equilibrium of the whole box", it is "CEA equilibrium of whatever has dissociated so far, plus locked unreacted CH4".

## Question 2: Why is the solver making gaseous carbon at 1000 K?

This has three causes that compound. GPT-5.5 listed them. I want to flag the one I think is the *primary* cause, because the fix is different depending on which one dominates.

### Cause A (necessary): no CO

Without `CO`, any carbon that the reaction solver decides to oxidize at all has to consume two O atoms (as `CO2`) or zero O atoms (as `C(g)`). With H/C = 4 and O/C = 1, the linear-programming-style answer is: send most C to `CO2` (using up O), send the rest to `C(g)`, and use the remaining O on H2O. CEA's actual answer with `CO` available is to send almost all C to `CO`, which uses each O atom once instead of twice.

If you add `CO` to the species list and *nothing else changes*, the `C(g)` artifact will largely vanish. This is the highest-value single fix.

### Cause B (mechanism): the EPM exponential prefers gas C over absent graphite

Inside `SolveReactions`, condensed phases are masked out (`active_m = {true, false, false}`). Even though `C(gr)` is in `viewSpecies` and contributes to `vec_x`, its `N_solid` stays at `Constants.N_mMin = 1e-6 mol`. So at solver convergence:

```text
n_Cgr = N_solid * x_Cgr <= 1e-6 * x_Cgr
n_Cg  = N_gas    * x_Cg
```

If `x_Cgr` is huge (and it should be, since at 1000 K graphite is dramatically more stable than monatomic carbon vapor), it still gets multiplied by `1e-6`. Meanwhile `x_Cg` is tiny but `N_gas` is hundreds of moles. The reaction solver therefore puts carbon in `C(g)` because graphite is forcibly disabled, not because the math actually thinks graphite is unstable.

A clean fix here is what GPT-5.5 suggested: when the reaction solver runs in gas-only mode, *filter `viewSpecies` to gas species* before building the Jacobian. Right now condensed species are still in the column space of `view`; they just have a clamped phase total. That is what produces the small but nonzero `C(s)` amounts in the BoxSim output, and the absurd `H2O(L)` and `H2O(s)` amounts. Those condensed amounts are coming out of the EPM, not out of `SolvePhases`.

### Cause C (primary, I think): `SolvePhases` cannot move `C(g)` to `C(gr)` because the comparison is dimensionally wrong

Look at `SolvePhases` lines 730-742:

```csharp
double v_gas = V_gas / Math.Max(n_gas, Constants.n_jMin);
double P_gas = gasResource.SpeciesPhase.EquationOfState.GetP(T, v_gas);
double phi_gas = Math.Exp(... GetLogphi(T, P_gas, v_gas));

double phi_cond = Math.Exp(... GetLogphi(T, P, v_cond));
// IncompressiblePhaseEquation.GetLogphi returns 0 -> phi_cond = 1

double n_gas_eq = (phi_cond / phi_gas) * P * V_gas / (Constants.R * T);
```

This is the saturation equilibrium for a *species that exists as both gas and liquid with similar standard chemical potential*. It is correct for water near its boiling point, where `mu^0_gas(T) ~ mu^0_liquid(T)` and the difference between phases comes purely from the fugacity correction. It is *catastrophically wrong* for graphite at 1000 K.

The actual condition at equilibrium between `C(g)` and `C(gr)` is:

```text
mu_Cg(T, P, partial) = mu_Cgr(T, P)
mu^0_Cg(T) + RT ln(P_Cg / P0) = mu^0_Cgr(T)
P_Cg_eq = P0 * exp((mu^0_Cgr - mu^0_Cg) / RT)
```

At 1000 K, `mu^0_Cgr(T) - mu^0_Cg(T)` is roughly `-700 kJ/mol` (the sublimation/atomization energy of graphite is enormous). So `P_Cg_eq` is `bar * exp(-84000)`, i.e. essentially zero. The equilibrium amount of monatomic gas carbon over graphite at 1000 K is well below any threshold the simulation can represent.

The current code is asking "what partial pressure of gas carbon makes its fugacity equal to graphite's fugacity at the system pressure?" with both fugacity coefficients = 1. That collapses to `P_Cg_eq = P_system`, which is hundreds of bar, which translates to hundreds of moles in 1 m^3 at 1000 K. That is exactly the value `n_gas_eq` you compute. The phase solver is doing exactly what its formula says; the formula just does not encode the actual thermodynamics of carbon sublimation.

This is the part GPT-5.5 also identified, but I want to be explicit: the missing physics is not "the fugacity coefficient of graphite". It is the `mu^0` difference between the two phases. The fugacity coefficient is a small correction. The standard chemical potential difference between gas carbon and graphite at 1000 K is many `RT`.

The correct test for "should `C(g)` move to `C(gr)`?" is:

```text
mu^0_Cg(T) + RT ln(P_Cg / P0)   vs   mu^0_Cgr(T) + 0
```

or equivalently:

```text
fugacity_gas / fugacity_cond,eq = exp((mu^0_Cg - mu^0_Cgr + RT ln(P_Cg/P0)) / RT)
```

`mu^0_j(T)` for both phases is already available: it is `HeatCapacityFunction.GetH(T) - T * GetS(T)` for that `SpeciesPhase`. The NASA9 polynomials for `C` (gas) and `C(gr)` already encode the atomization energy. The information is present in your data; it is just not used by `SolvePhases`.

### Why I think C is the primary cause

If only Cause A were true, the reaction solver would still create some `C(g)` (because oxygen-poor carbon has nowhere else to go), but the phase solver would immediately move it to graphite each frame. The visible state would not have 41 mol `C(g)` floating around.

If only Cause B were true (condensed phases excluded from EPM), the reaction solver might briefly produce `C(g)`, but again, a working phase solver would condense it.

The reason BoxSim shows tens of moles of `C(g)` at quasi-steady-state is that the phase solver is asking the wrong question. Even if you add `CO` and rewrite the EPM to be gas-only-clean, you will still get `C(g)` puddles in any genuinely oxygen-starved or temperature-extreme case unless `SolvePhases` learns to use `mu^0_j`.

In short: gaseous carbon at 1000 K is a `SolvePhases` bug at heart. The missing `CO` makes the bug visible by producing surplus `C` that needs to be condensed, but the bug is in the condensation criterion.

## Question 3: What makes multi-phase equilibrium harder, and is the outer loop just laziness?

### Short answer

It is not laziness. The phase-activation outer loop is the standard way to express a fundamental fact about phase equilibrium: **phase presence is a complementarity condition, not an equation**. Newton's method solves smooth equations on a connected domain. Phase presence chops that domain into pieces with different equation systems on each piece. The outer loop is the procedure that decides which piece you are on.

### A bit more detail

I'll try to add structure to GPT-5.5's correct points.

**Gas-only EPM is convex in the right variable.** For ideal gases at fixed T and P, the Gibbs energy is

```text
G(n) = sum_j n_j (mu^0_j + RT ln(n_j / N_gas) + RT ln(P/P0))
```

The element-balance constraints are linear in `n`. The ideal mixing term `RT n_j ln(n_j / N_gas)` is strictly convex on the interior of the positive orthant. The constraint set is a polytope. So the primal is a convex problem with a unique minimum.

The dual formulation (element potentials) is then a smooth nonlinear system with a positive-definite Hessian on the interior, which is what makes Newton converge so well in CEA / STANJAN.

**Adding a pure condensed phase removes the curvature.** A pure `C(gr)` species has no `RT ln(x_j)` term, because `x_j = 1` always. Its chemical potential is just `mu^0_Cgr(T)`. The Gibbs contribution `n_Cgr * mu^0_Cgr(T)` is *linear* in `n_Cgr`. Combined with linear element-balance constraints, this means the optimum for the `n_Cgr` direction is at a boundary, not in the interior. The first-order condition is no longer "set the gradient to zero", it is the inequality:

```text
mu^0_Cgr(T) - sum_i lambda_i a_iC,gr >= 0  (graphite cannot reduce G further)
n_Cgr * (mu^0_Cgr(T) - sum_i lambda_i a_iC,gr) = 0  (complementarity)
n_Cgr >= 0
```

This is a classic linear-program-style KKT condition. It is fine mathematically; it is just not a system of equations Newton can solve directly.

**The problem can still be convex.** Even with several pure condensed species, the *underlying convex program is well posed*. The minimum exists and is unique (generically). The hard part is purely algorithmic: how do you find which condensed species have `n_j > 0` at the optimum?

So GPT-5.5 is correct that "non-convex" is the wrong word in the strict optimization sense. The issue is non-smoothness at phase boundaries plus an active-set decision. It still feels like non-convexity in practice because you can land in a region where Newton oscillates between configurations.

**Ideal solid/liquid solutions are easier than pure condensed phases.** If you treat condensed species as an *ideal solid solution* (per `energy_minimization_3.md`), then each phase has its own `N_m` and its mole-fraction normalization, and every species gets a mixing term `RT ln(x_j)` again. The math becomes structurally identical to gas, just with one extra normalization equation per active phase. This is what the current `SolveReactions` code architecture (with the `Z_m - 1 = 0` normalization rows) is trying to be. The bug is that we are then doing it with condensed species that the rest of the data assumes are pure, and with a phase total that the masking forces to `1e-6 mol`.

**The Jacobian singularity is the symptom.** When a phase mole total `N_m -> 0`, the row `sum_j (x_j) - 1 = 0` is enforced over species whose `x_j` are arbitrary (since they are not actually present). The dual variables for those rows are not meaningful. Newton then either picks a huge step in some `N_m` direction or returns a delta with negligible effect, depending on regularization. This is exactly what you experienced with patching `J` with 1s on the diagonal.

The cleanest mathematical fix is the active-set loop, because it just *removes* the meaningless equations entirely until the phase has reason to exist. That is what CEA, STANJAN, and modern process simulators do. It is not historical baggage; it is the correct way to write a Newton solver for a constrained problem whose constraint set has corners.

### Is the outer loop "real" or "for practical reasons"?

Both, and they are the same thing. The Gibbs energy landscape is not differentiable across a phase appearance event. The first-order conditions are different equations on either side. Any solver, including a hypothetical infinitely powerful one, has to make the discrete decision "this phase exists / does not exist" at some point. The outer loop is the explicit place that decision happens. You can hide it inside a smoothing scheme (interior-point methods do this) or inside a complementarity solver, but it does not go away.

For Pile Simulator 3 specifically, my recommendation matches GPT-5.5: do not write an active-set loop yet. Instead, split chemistry and phase change into two separate problems (EPM on gas only, then a Margules-style `SolvePhases` that actually uses `mu^0`). When both behave well in isolation, then look at unifying them with an active-set loop if you still need to. There is a good chance you will not need to: a gameplay-time-scale simulator can use the operator-split approach indefinitely and will produce visually correct chemistry as long as each piece is right on its own.

## Direct, short answers

1. **CEA's solution is correct.** Equal-mass CH4/O2 is `phi = 3.99`, so there is only enough oxygen to oxidize each carbon once. The natural carrier is `CO`, and the natural hydrogen carrier is `H2`. At the UV-equilibrium `T = 1409 K, P = 62 bar`, the mole fractions of CO ~ 0.30, H2 ~ 0.57, CH4 ~ 0.06, H2O ~ 0.05, CO2 ~ 0.01 are exactly the partial-oxidation/reforming mixture you would predict. BoxSim's biggest single simplification is the absence of `CO`. Other simplifications that matter: dissociation gate (BoxSim equilibrates a sub-pool, not the whole box), gas-only EPM with condensed species still in the view, placeholder condensed-phase EOS (`v = 0`, `logphi = 0`), and a `SolvePhases` that does not actually include `mu^0_j` for condensed phases. Radicals (H, O, OH, CH3) are missing but are not the dominant issue at 1409 K.

2. **`C(g)` at 1000 K is a `SolvePhases` bug, exposed by the missing `CO`.** Without `CO`, oxygen-poor carbon has nowhere to go but `C(g)` or `C(gr)`. Without `CO` and with a gas-only EPM, the reaction solver produces `C(g)`. `SolvePhases` should immediately move it to `C(gr)`, but the criterion it uses is `f_gas = phi_gas * P_gas` vs `f_cond = phi_cond * P_system` with both `phi`'s = 1. That collapses to "gas carbon at system pressure is in equilibrium with pure graphite", which is wildly wrong at any temperature where graphite is stable. The fix is to use `mu^0_gas(T)` vs `mu^0_cond(T)` directly. The NASA9 data for `C` and `C(gr)` already contains the answer; `SolvePhases` just does not consult it.

3. **Multi-phase equilibrium is harder because phase existence is a complementarity condition, not an equation, and Newton solves equations.** The underlying convex program is still convex, and a fixed active set of phases gives a smooth Newton problem. The hard part is the discrete decision of which condensed phases exist at the optimum. Pure condensed species contribute *linear* terms to `G`, so their optimal amounts sit at boundaries, not at zero-gradient points. When the active set is wrong, Newton's equations are wrong. The CEA / STANJAN outer loop is the natural numerical expression of this: solve a smooth subproblem for a guessed active set, check the complementarity conditions, adjust the set, repeat. It is not historical sloppiness; it is the correct algorithm for a piecewise-smooth problem. That said, for this project I would not implement the full active-set loop yet. Instead, fix the two pieces separately: keep `SolveReactions` gas-only but actually *filter `viewSpecies` to gas*, and rewrite `SolvePhases` to compare `mu^0_j` between phases using the NASA9 enthalpy/entropy data. That alone will eliminate the `C(g)` artifact and let the next debugging round focus on the equilibrium proper rather than on the active-set numerics.
