# BoxSim Water Overproduction Fix

## Problem

The BoxSim methane/oxygen test could produce impossible amounts of water. One observed result was about 474 mol H2O from an initial 200 mol CH4 and 100 mol O2. That violates element conservation because the starting state contains only 200 mol of oxygen atoms.

The earlier NaN-fix attempt stopped NaNs in some cases, but it still allowed the Element Potential Method output to be applied even when the proposed products consumed more atoms than the free-element pool contained.

## Bugs Fixed

### `C_v` Accumulation

`Volume.DeriveQuantities()` reset `Mass`, `vec_V`, `UsedVolume`, `U`, and `S`, but did not reset `C_v`. Every solve accumulated old heat capacity into the next solve.

Fix: reset `C_v = 0.0` with the other derived quantities.

### Missing Free Elements In The Formula View

`Volume.RebuildIndexes()` built the formula-table view from existing resources only. If resources dissociated away, or if a free element existed only in `freeElements`, the reaction view could lose the element needed by the reaction solve.

Fix: include `freeElements.Keys` in `existingElements` before computing the bitmask.

### Dissociation Activation Energy Was Zero

`Species.DissociationActivationEnergy` is derived from `DissociationTemperature`, but species are constructed before the placeholder temperature is assigned in `AllSpecies.Initialize()`.

That left activation energy at the default value `0`, so:

```text
k = exp(-Ea / RT) = exp(0) = 1
```

Spark then fully dissociated every species in one frame instead of applying the 0.1% spark floor.

Fix: after assigning `DissociationTemperature = 600.0`, recompute `DissociationActivationEnergy`.

## Solver Changes

### Gas-Only Reaction Solve

`SolveReactions()` now filters the EPM view to gas species only. Condensed phases are still handled by `SolvePhases()`.

This matches the intended split documented in `phase_moles.md`:

```text
SolveReactions chooses chemical species.
SolvePhases moves species between phases.
```

Including liquid and solid species inside the reaction Newton solve made inactive phase normalization equations singular. It also allowed phantom condensed phases, especially solid carbon and solid water, to dominate the Jacobian.

### Active Phase Set

The reaction Newton system now includes only active gas phase moles instead of hardcoding gas, liquid, and solid phase mole unknowns.

Before:

```text
unknowns = lambda elements + N_gas + N_liquid + N_solid
```

Now, for the current reaction solve:

```text
unknowns = lambda elements + N_gas
```

This avoids zero columns and impossible normalization residuals for absent liquid and solid phases.

### Lambda Initialization

Starting `lambda = 0` made:

```text
x_j = exp(-mu_j / RT)
```

span extreme values. That polluted the Jacobian with huge entries and caused Math.NET's SVD to throw `NonConvergenceException`.

The solver now initializes `lambda` from a linearly independent gas-species basis chosen by lowest pure-species chemical potential. For the CH4/O2/H2O/CO2 system, this starts the dominant gas species near `x_j = 1`, keeping the first Jacobian reasonably scaled.

### Bounded Exponentials And Damped Newton Steps

The exponent passed to `Exp()` is clamped to `[-50, 50]`. Newton updates are damped so lambda and phase mole counts cannot jump by unbounded amounts in one iteration.

This is not a substitute for a complete trust-region method, but it prevents one bad iteration from immediately producing infinities or negative phase moles.

### Conservation Projection

Before reaction products are applied to `Resources`, the proposed product vector is projected back inside the available `freeElements` pool.

If the Newton solve proposes products requiring too much of any element, all proposed products are scaled down by the most limiting element ratio.

This is the final safety boundary:

```text
bad Newton output may under-react, but it cannot create atoms
```

## Observed Runtime Behavior

With initial conditions:

```text
CH4(g): 200 mol
O2(g): 100 mol
V: 1 m3
spark enabled
```

The first spark steps now behave stoichiometrically at the spark floor. About 0.1% of the relevant reactants are processed per frame, producing approximately:

```text
CH4 + 2 O2 -> CO2 + 2 H2O
```

The example log starts with:

```text
CH4: 199.95 mol
O2: 99.90 mol
CO2: 0.050 mol
H2O: 0.100 mol
```

That is the expected stoichiometric ratio.

At later high temperatures, the current limited species set and NASA9 thermodynamics favor mostly CO2 and H2, with water dissociating as temperature rises. This may or may not match a NASA CEA run for the same closed, constant-volume, constant-internal-energy setup. It should be checked separately.

## Remaining Caveats

This is still not a complete production equilibrium solver.

Known limitations:

1. `SolveReactions()` is currently gas-only.
2. Condensed phase activation is still delegated to `SolvePhases()` and does not use a full active-set Gibbs minimization.
3. The Newton solve is damped but not a true trust-region solve.
4. If the Newton solve under-reacts, leftover atoms remain in `freeElements`, which is not currently displayed in BoxSim.
5. The current species subset excludes CO, OH, O, H, and other high-temperature species that matter around 2500-3000 K.

The important fix is that the solver now preserves element conservation at the application boundary and no longer allows phantom condensed phases to drive impossible water production.
