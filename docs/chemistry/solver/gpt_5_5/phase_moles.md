# Phase Moles In The Element Potential Method

## What `N_m` Means

In the multi-phase Element Potential Method, each active phase has a total mole amount:

$$
N_m
$$

Each species-phase pair `j` belongs to exactly one phase `m`. Its mole fraction is:

$$
x_j = \frac{n_j}{N_m}
$$

and its moles are recovered with:

$$
n_j = N_m x_j
$$

The phase normalization equation is:

$$
\sum_{j \in m} x_j = 1
$$

This equation only makes sense for a phase that exists. If `N_liquid = 0`, then liquid mole fractions are not physically meaningful. There is no liquid mixture to normalize.

## Active Phases

The robust rule is:

> Only active phases are included in the Newton unknown vector.

If only gas is active, the unknowns are:

$$
\lambda_1, \lambda_2, ..., \lambda_a, N_{gas}
$$

If gas and liquid are active, the unknowns are:

$$
\lambda_1, \lambda_2, ..., \lambda_a, N_{gas}, N_{liquid}
$$

If liquid is absent, do not include `N_liquid`, do not include the liquid normalization residual, and do not include liquid species in the reaction solve unless an outer phase-activation step turns liquid on.

This avoids the bad state where Newton is asked to normalize mole fractions in a phase with zero moles.

## Why `N_m = 0` Oscillates

The oscillation you described can happen if the code mixes two concepts:

1. Reactions decide which chemical species should exist.
2. Phase equilibrium decides which phase of a species should exist.

Example:

1. Step `n`: the reaction solve has no active liquid phase, so all products become gas.
2. Step `n + 1`: phase equilibrium says one gaseous species should condense.
3. The liquid phase has `N_liquid = 0`, so liquid mole fractions are undefined.
4. A seeded or guessed liquid phase appears, maybe too much liquid appears.
5. The next step removes it again.

This is an active-set problem. It should not be solved by letting `N_liquid` bounce around zero inside one unconstrained Newton solve.

## Phase Activation Outer Loop

Use an outer loop around the Newton solve:

```text
active phases = phases already present above epsilon

repeat:
    solve reactions using only active phases
    check inactive phases for stability
    if a phase should appear:
        activate it with a small positive initial N_m
        continue
    if an active phase solved to N_m <= epsilon:
        deactivate it
        continue
    break
```

The exact stability test depends on how much phase equilibrium is folded into `SolveReactions` versus `SolvePhases`.

For the first version, the clean split is:

1. `SolveReactions` chooses chemical species in a reaction pool.
2. `SolvePhases` moves moles between gas/liquid/solid phases of the same species using chemical potential or fugacity.

With that split, phase activation mostly belongs in `SolvePhases`.

## Condensation Without Oscillation

For each chemical species that has multiple possible phases, phase equilibrium is:

$$
\mu_{species,gas} = \mu_{species,liquid} = \mu_{species,solid}
$$

or equivalently, for a cubic EOS phase pair, equal fugacity where that model applies.

Operationally:

1. If only gas exists and liquid has lower chemical potential at current `T` and `P`, move some gas moles into liquid.
2. If only liquid exists and gas has lower chemical potential, move some liquid moles into gas.
3. If both exist, move moles from higher `mu` to lower `mu` until the difference is small.
4. Never move more moles than the source phase has.
5. Use damping and a small threshold so numerical noise does not flip a tiny phase on and off every frame.

This is separate from the reaction element potentials. It can be slower and game-time-based, just like dissociation.

## Thresholds And Hysteresis

Use two thresholds, not one:

1. `PhaseCreateMoles`: minimum amount needed to create a missing phase.
2. `PhaseDeleteMoles`: amount below which an existing phase is removed.

Make:

$$
PhaseDeleteMoles < PhaseCreateMoles
$$

This hysteresis prevents a phase from being created and deleted repeatedly because of tiny numerical changes.

Also use a chemical-potential deadband:

$$
|\Delta \mu| < \epsilon_\mu
$$

When inside the deadband, do not transfer moles.

## Current `Volume` Implications

Current `Volume.SolveReactions` uses a fixed `vec_n = new double[3]` for gas, liquid, and solid. That is fine as storage, but the Newton system should not always include all three phases.

Specific problems to avoid:

1. Initializing every phase to `42.0` moles creates matter out of the initial guess. Newton may converge away from it, but a bad initial phase can distort the path or fail.
2. Dividing by `vec_n[phase]` when a phase is absent is undefined.
3. Applying `vec_n[m] += delta` can make phase moles negative unless the step is damped or the phase is removed from the active set.
4. The lower-right zero block must be written at `J[a + m1, a + m2]`, not `J[m1, m2]`. Otherwise it overwrites part of `Q`.

The first stable version can avoid most of this by making `SolveReactions` gas-only, then using `SolvePhases` to condense species after reactions. Multi-phase reaction equilibrium can be added later with an active phase set.
