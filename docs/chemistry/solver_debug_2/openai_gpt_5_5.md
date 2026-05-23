# Council response: openai/gpt-5.5

Date: 2026-05-23

Reviewed code/docs:

- `DSA/Chemistry/Volume.cs`
- `Data/Species.cs`
- `Data/NASA9Loader.cs`
- `DSA/Chemistry/EquationsOfState.cs`
- `Scripts/BoxSim.cs`
- `docs/chemistry/solver_debug_2/cearun.txt`
- `docs/chemistry/solver_debug_2/boxsim.txt`
- `docs/chemistry/dual_problem/gpt_5_5.md`
- `docs/chemistry/solver/*`

## Executive summary

CEA is finding a physically sensible fuel-rich methane/oxygen UV equilibrium. With equal fuel/oxidizer mass, this is not close to stoichiometric combustion. It is a very fuel-rich system, about `phi = 3.99`, and at the assigned density/internal energy CEA finds about `T = 1408.81 K`, `P = 62.333 bar`, and a product gas dominated by `H2` and `CO`, with smaller `CH4`, `H2O`, and `CO2`.

BoxSim cannot find that state because its current problem is not the same equilibrium problem. The current implementation combines these approximations:

- Only the dissociated per-frame pool is equilibrated, not the whole inventory.
- `SolveReactions()` currently minors out liquid and solid phases, so the reaction solve is gas-only.
- The loaded product species set excludes `CO`, which is the main carbon/oxygen carrier in CEA's result.
- The species set also excludes radicals and atom species that CEA keeps available, especially `H`, `O`, `OH`, `CH3`, `CH`, etc.
- The phase-transfer model does not include the condensed-phase standard chemical potential, so it is not actually comparing `mu_gas` against `mu_condensed`.
- The gas/condensed EOS setup is still placeholder: ideal gases, zero-volume incompressible condensed phases, and `ln(phi) = 0` for condensed phases.
- Temperature, pressure, reaction equilibrium, and phase equilibrium are operator-split and damped, not solved as one coupled UV equilibrium.

The current BoxSim result is therefore not a failed reproduction of CEA. It is a different simplified model whose missing species and phase approximations push carbon into unphysical places.

## 1. What is CEA doing, and why did it find that solution?

The CEA input in `cearun.txt` is a `uv` problem, meaning assigned internal energy and assigned volume. CEA is conserving the reactants' total internal energy and the specified density, then solving the full equilibrium composition and temperature simultaneously.

The important starting condition is equal mass methane and oxygen:

- BoxSim starts with `200 mol CH4` and `100 mol O2`.
- That is about `3.21 kg CH4` and `3.20 kg O2`.
- Stoichiometric methane combustion would need `2 mol O2` per `1 mol CH4`, so `200 mol CH4` would need `400 mol O2`.
- The current case has only one quarter of the stoichiometric oxygen.
- CEA reports `PHI,EQ.RATIO = 3.989263`, which is essentially four times fuel-rich.

In a fuel-rich methane/oxygen equilibrium at around 1400 K, the oxygen is not expected to end up mostly as `CO2 + H2O`. There is not enough oxygen for that, and at this temperature carbon monoxide is strongly favored over forcing every oxidized carbon atom to `CO2`. The dominant reaction direction is closer to partial oxidation/reforming than complete combustion:

```text
CH4 + 1/2 O2 -> CO + 2 H2
```

That simple reaction is not the whole CEA solution, but it explains the shape of the result. CEA's reported mole fractions above `trace = 1e-6` are roughly:

- `H2`: `0.57403`
- `CO`: `0.30043`
- `CH4`: `0.062196`
- `H2O`: `0.051161`
- `CO2`: `0.012126`
- trace `CH3`, ketene, ethylene, ethane, formaldehyde

This is consistent with a carbon-rich, hydrogen-rich, oxygen-poor equilibrium. Most hydrogen becomes `H2`. Most oxygen-bearing carbon becomes `CO`, not `CO2`. Some methane survives because the mixture is very fuel-rich and 1400 K is not hot enough to destroy all hydrocarbons under these constraints.

CEA also has a much larger candidate species set than BoxSim. The `SPECIES BEING CONSIDERED` list in `cearun.txt` includes `CO`, `H`, `O`, `OH`, `CH`, `CH2`, `CH3`, many hydrocarbons, oxygenates, graphite, water condensed phases, and more. That matters because equilibrium is a constrained minimization over the species you allow. If the best species is not in the basis, the solver must express the same element inventory with worse substitutes.

### Why BoxSim's equilibrium is different

The closest BoxSim output in `boxsim.txt` is around `T = 903 K`, `P = 2.54 MPa`, with roughly:

- `CH4(g)`: `130 mol`
- `H2O(g)`: `139 mol`
- `CO2(g)`: `29 mol`
- `C(g)`: `40.9 mol`
- `C(solid)`: `0.289 mol`
- `H2(g)`: `0.162 mol`
- `O2(g)`: `0.952 mol`, eventually disappearing below threshold

Element balance is roughly conserved, but the distribution is not CEA-like. The main reason is that BoxSim has no `CO`. In an oxygen-poor C/H/O system, removing `CO` is a severe constraint. Carbon that should be carried by `CO` has to be represented as some combination of:

- `CH4`
- `CO2`
- `H2O` plus elemental carbon
- `C(g)` or `C(gr)`

That is already enough to make the equilibrium qualitatively wrong. But there are additional simplifications.

### Other BoxSim simplifications causing wrong equilibrium

1. The species basis is too small.

`Data/Species.cs` loads only `H2`, `C`, `O2`, `CH4`, `H2O`, and `CO2` in the current subset. CEA has enough species to place small but important amounts into radicals and intermediate molecules. Even if most radicals are trace, they stabilize the equilibrium algebra and absorb high-temperature dissociation paths. With no `H`, `O`, `OH`, `CH3`, etc., BoxSim forces dissociated atoms into the few available closed-shell species or into `C(g)`.

2. The current reaction solve is gas-only.

`Volume.SolveReactions()` sets:

```csharp
bool[] active_m = new bool[3]
{
    true,
    false,
    false
};
```

That means the Newton solve only includes the gas phase mole total. Condensed phases are effectively excluded from the main chemical equilibrium active set. They can still appear later through `SolvePhases()`, but they are not part of the same minimization that chooses chemical products.

3. `SolvePhases()` is not using full phase chemical potentials.

For phase equilibrium you need equality of chemical potentials for the same species in different phases:

```text
mu_gas(T, P, x) = mu_liquid(T, P, x) = mu_solid(T, P, x)
```

The current `SolvePhases()` compares fugacity-like terms, but for condensed phases `IncompressiblePhaseEquation.GetLogphi()` returns `0.0`. That makes the condensed fugacity effectively `P`, with no standard Gibbs term for graphite, ice, or liquid water. This is not enough information to know whether graphite is favored over carbon vapor, or ice over water vapor.

For example, for carbon the current idealized gas/condensed comparison behaves roughly like:

```text
f_gas = P_C
f_condensed = P
```

Then the gas amount target becomes approximately:

```text
n_gas_eq = P * V_gas / (R T)
```

At BoxSim's late state, `P` is on the order of `2.5 MPa`, `V_gas` is about `1 m^3`, and `T` is about `900 K`, so this target is hundreds of moles. If there are only tens of moles of carbon, the phase solver is happy leaving almost all carbon as gas. That is not physical. Real graphite has an extremely low vapor pressure at 900-1000 K, and the missing term is the standard chemical potential difference between `C(g)` and `C(gr)`.

4. Condensed phases have placeholder volumes and EOS behavior.

`AllSpeciesPhases.MakeUpEquationsOfState()` gives gases an ideal gas EOS and all condensed phases an incompressible EOS with `v = 0.0`. That is acceptable as a first display/volume hack, but it is not a thermodynamic phase model. It erases condensed volume, pressure response, and phase-specific fugacity behavior.

5. The solver is operator-split.

CEA solves composition, temperature, and pressure for the UV problem as one equilibrium calculation. BoxSim runs:

```text
Dissociate()
RebuildIndexes()
DeriveQuantities()
SolveReactions()
SolveUT()
SolveVP()
RebuildIndexes()
SolvePhases()
```

This is a game-time kinetic/operator split. It is much easier to implement and can be a good game model, but it is not equivalent to CEA. Reaction equilibrium is computed at the old `T` and `P`, then `T` and `P` are adjusted, then phases move after that. A fully coupled UV equilibrium would let all of those variables respond together.

6. BoxSim is intentionally rate-limited.

CEA allows the whole inventory to redistribute. BoxSim only dissociates a fraction per frame, stores those atoms in `freeElements`, equilibrates that pool, and adds products back. That design is conceptually right for gameplay, but it means intermediate BoxSim states should not be expected to match CEA. Only the long-time limit should approach a comparable equilibrium, and only if the same species, phases, and thermodynamic models are available.

7. The CEA comparison uses full-equilibrium products while BoxSim still has a dissociation gate.

The BoxSim late state still has `130 mol CH4`. That is not surprising under a 0.1 percent-per-frame dissociation model. But if the goal is to compare to CEA, you need either a special debug mode that gives the reaction solver the whole element inventory, or you need to run enough frames and remove kinetic thresholds so the game model actually approaches its own equilibrium.

## 2. Why is the solver trying to make gaseous carbon at 1000 K?

There are three separate reasons.

### Reason A: no `CO` leaves carbon without its natural oxygen-poor carrier

In CEA, about 30 percent of gas moles are `CO`. In BoxSim, that species does not exist. Once oxygen is limited and much of it is pulled into `H2O` and `CO2`, excess carbon cannot become `CO`. It has only a few choices:

- stay in `CH4`
- become `CO2`, consuming two oxygen atoms per carbon
- become `C(g)` or `C(gr)`, consuming no oxygen

When the element-potential solve decides the free pool wants lots of water and some carbon dioxide, leftover carbon has to appear as elemental carbon. Since the reaction solve is gas-only, the available elemental carbon inside `SolveReactions()` is `C(g)`, not graphite.

### Reason B: gas-only EPM includes `C(g)` but not `C(gr)` as an active reaction phase

The current gas-only minor is a useful stabilization hack, but it has a direct consequence: if a chemical product needs to be elemental carbon, the reaction solver's active phase for it is gas. Graphite is deferred to `SolvePhases()`.

That split can be okay if the phase solver correctly moves `C(g)` to `C(gr)`. Right now it does not, because the phase solver is missing the standard Gibbs/free-energy difference between gas carbon and graphite. So `C(g)` is created by the reaction solve and then not reliably removed by the phase solve.

### Reason C: the carbon phase equilibrium test is physically wrong

At 1000 K, equilibrium carbon should overwhelmingly be condensed graphite, not tens of moles of monatomic carbon vapor at tens of bar. The current phase solver does not know that. It sees ideal gas fugacity for `C(g)` and a placeholder condensed fugacity for `C(gr)`.

The decisive missing quantity is not the fugacity coefficient. It is the reference chemical potential:

```text
mu_j = G_j^0(T) + RT ln(activity or fugacity ratio)
```

For gas carbon, `G_j^0(T)` includes the very high enthalpy of atomizing graphite into gaseous carbon. For graphite, `G_j^0(T)` is the stable reference state. `SolvePhases()` currently does not compare these NASA9 `H - T S` terms between phases. It mostly compares pressure/fugacity factors. That makes carbon vapor far too easy to keep.

So the short answer is: the reaction solver makes `C(g)` because `CO` is absent and the chemical active set is gas-only; the phase solver fails to turn it into graphite because condensed phase chemical potentials are not actually included in the phase-equilibrium criterion.

## 3. What exactly makes multi-phase equilibrium harder?

Minoring out liquids and solids is not elegant as a final thermodynamic solver, but it is not a lazy misunderstanding. It is a standard way to avoid a real active-set/complementarity problem while the rest of the chemistry is still under construction.

### The gas-only problem is smooth

For one ideal gas phase with a fixed species list, the equilibrium problem is comparatively friendly:

```text
minimize G(n)
subject to A n = b
subject to n_j >= 0
```

The ideal mixing term gives curvature. In the interior where all active `n_j > 0`, Newton has a smooth system. The dual variables are well-defined, and the single gas phase normalization equation makes sense because the gas phase exists.

### A fixed active multi-phase problem is also manageable

If you already know exactly which phases exist, the multi-phase EPM equations are still solvable. You add one phase mole total `N_m` and one normalization equation for each active phase:

```text
sum_{j in phase m} x_j = 1
```

For a fixed active set, this is not conceptually impossible. It is just a larger nonlinear system.

### The hard part is that you do not know the active set

The phase normalization equation only makes physical sense for a phase that exists. If `N_liquid = 0`, then liquid mole fractions are undefined. Asking Newton to enforce:

```text
sum liquid x_j = 1
```

while there is no liquid phase is mathematically bogus. Near `N_m = 0`, the Jacobian becomes ill-conditioned or singular because the phase can appear/disappear at a boundary of the feasible region.

This is the key difficulty: phase presence is controlled by inequalities and complementarity conditions, not just equalities.

For an inactive phase, the condition is not `sum x_j = 1`. It is more like:

```text
the phase has no thermodynamic drive to appear
```

For an active phase, the condition is:

```text
N_m > 0 and sum x_j = 1
```

Those are different equations depending on whether the phase exists.

### Does multi-phase equilibrium become non-convex?

For ideal mixtures at fixed `T` and `P`, the underlying Gibbs minimization is usually still a convex optimization problem over species moles, or at least it is not hard because of ordinary local minima in the way gameplay programmers usually mean non-convex. The hard part is more specific:

- The objective may be only weakly convex because pure condensed phases have little or no mixing curvature.
- Phase appearance/disappearance creates nonsmooth boundaries.
- The dual formulation has singularities when an active phase mole total tends to zero.
- Multiple condensed phases can have nearly identical compositions or stoichiometric constraints, making the formula matrix/Jacobian rank-deficient or nearly rank-deficient.
- Cubic EOS models can introduce their own root-selection and metastability issues if gas/liquid fugacity is coupled directly into the reaction solve.
- A UV problem adds coupling through `T` and `P`, so the composition solve is not just a fixed-`T,P` Gibbs minimization.

So I would not summarize the practical difficulty as simply "multi-phase makes G non-convex." The better summary is: fixed-active-set equilibrium is smooth enough; unknown phase activity turns it into an active-set problem with complementarity conditions, weak curvature, and singular Newton systems at phase boundaries.

### Why CEA/STANJAN use an outer loop

The outer loop is not just a historical kludge. It is the natural numerical expression of the phase rule:

```text
guess active phases
solve smooth equilibrium for that active set
test inactive phases for whether any should appear
remove active phases whose amounts go to zero or whose stability test fails
repeat
```

That is exactly how many constrained optimizers work: solve an equality-constrained subproblem, update the active inequality constraints, solve again.

The reason it feels inelegant is that the clean mathematical statement hides the discrete decision: which phases exist? Once that decision is fixed, Newton is fine. When it changes, Newton's current system is the wrong system.

### What I would do next

For Pile Simulator 3, I would keep the gas-only `SolveReactions()` for now, but make the split more explicit and fix the pieces that make the comparison misleading.

1. Add `CO` immediately.

This is the largest single correction for the CEA case. Without `CO`, the oxygen-poor methane equilibrium will keep being qualitatively wrong.

2. Add a debug/full-equilibrium mode.

For comparison against CEA, create a path that gives the solver the whole element inventory instead of only `freeElements`. Keep the gameplay dissociation model separate. Otherwise every CEA comparison is mixing equilibrium errors with kinetic-gate behavior.

3. Filter `viewSpecies` to gas species inside `SolveReactions()`.

Right now inactive phases are removed from the minor Jacobian, but `vec_x` and final `vec_n` are still computed for all phases using the small stored `vec_N` values. A cleaner gas-only EPM should build the candidate species list as gas-only. That avoids ghost liquid/solid products from huge exponentials multiplied by `1e-6 mol`.

4. Rewrite `SolvePhases()` around chemical potential, not just fugacity coefficient.

For two phases of the same species, compare something like:

```text
mu_phase = H_phase(T) - T S_phase(T) + pressure/activity/fugacity correction
```

For ideal gases:

```text
mu_gas = G_gas^0(T) + RT ln(P_j / P0)
```

For a pure condensed phase, first approximation:

```text
mu_cond = G_cond^0(T)
```

plus optional `v (P - P0)` Poynting correction later. This alone would make graphite strongly preferred over `C(g)` at 900-1000 K.

5. Add a proper active-set loop only after the gas-only plus phase-transfer split behaves.

The current minoring approach is a reasonable stabilizer during development. The final robust version can use CEA/STANJAN-style phase activation, but it will be much easier to implement after the species basis and phase chemical potentials are correct.

## Direct answers to the three questions

1. CEA found mostly `H2 + CO` because the mixture is about four times fuel-rich and the UV equilibrium temperature is about 1409 K. There is far too little oxygen for complete combustion, and at that temperature/element ratio `CO` is the correct major carbon-oxygen product. Apart from missing `CO`, BoxSim differs because it has a tiny species basis, no radicals/atoms, gas-only reaction equilibrium, placeholder condensed EOS, phase transfer without standard phase chemical potentials, operator-split `T/P/composition`, and a per-frame dissociation gate rather than full-inventory equilibrium.

2. The solver makes gaseous carbon because oxygen-poor carbon wants to be `CO`, but `CO` is unavailable; the gas-only reaction solve can express leftover carbon as `C(g)`; and the phase solver lacks the thermodynamic information needed to move that `C(g)` into graphite. Tens of moles of `C(g)` at 1000 K is not physical. It is a model/active-set/species-basis artifact.

3. Multi-phase equilibrium is harder because phase existence is an active-set/complementarity problem. For a known active phase set, Newton is mostly fine. The singularity happens when a phase has zero moles but the equations still try to normalize its mole fractions, or when an active phase should disappear. This does not have to mean the underlying fixed-`T,P` ideal Gibbs minimization is badly non-convex. The practical issue is nonsmooth phase boundaries, weak curvature for condensed phases, rank issues, and active/inactive phase switching. The CEA/STANJAN outer loop is not merely lazy; it is the standard practical way to solve the correct piecewise-smooth problem.
