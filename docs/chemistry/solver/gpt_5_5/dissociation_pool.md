# Element Potentials With Per-Frame Dissociation

## The Difference From NASA CEA

NASA CEA answers this question:

> Given the total element inventory in the box, what complete equilibrium composition minimizes free energy?

Pile Simulator 3 asks a different first-version gameplay question:

> Given that only some moles dissociated this frame, what should those freed atoms recombine into?

Those are not the same problem.

If the whole box is passed into the Element Potential Method, then all existing graphite, diamond, methane, water, etc. can be freely reallocated immediately. That defeats the point of `DissociationTemperature` and the 0.1% per-frame kinetic gate.

For the first implementation, the Element Potential Method should solve only a **reaction pool**.

## Recommended First-Version Model

At the start of a frame, every `SpeciesPhaseResource` has existing moles:

$$
n_j^{old}
$$

The dissociation rule chooses an amount to remove:

$$
q_j
$$

The locked remainder is:

$$
l_j = n_j^{old} - q_j
$$

Only `q_j` enters the reaction pool. Convert it to element moles:

$$
b_i = \sum_j A_{ij} q_j
$$

where `A_ij` is the formula table. Then solve the Element Potential Method with:

$$
p_i = b_i
$$

The solver returns product moles for the pool:

$$
r_j
$$

The final box inventory is:

$$
n_j^{new} = l_j + r_j
$$

This means existing non-dissociated species are not optimization variables. Diamond can remain diamond at low temperature because `q_diamond = 0`, so its carbon never enters `b_i`.

## Important Code Implication

`freeElements` from `Volume.Dissociate()` is already the pool element inventory. That part is conceptually right.

The recombination step should add products back to the locked resources. It should not overwrite every resource with the equilibrium composition of only `freeElements`.

In other words, this shape is correct:

```text
old resources
  -> remove q_j
  -> freeElements = A q
  -> solve equilibrium of only freeElements
  -> add pool products r_j to remaining resources
```

This shape is wrong for the gameplay model:

```text
old resources
  -> remove q_j
  -> freeElements = A q
  -> solve equilibrium of only freeElements
  -> set total resource moles to r_j
```

The wrong version destroys the locked remainder.

## Should Existing Moles Affect Chemical Potential?

There are two possible models.

Before choosing either model, separate two meanings of chemical potential in the code.

The Element Potential Method equation uses a pure-species term:

$$
g_j(T,P)
$$

or, for gases, the pure-species term plus the pressure/fugacity term:

$$
g_j(T) + RT \ln\left(\frac{\phi_j P}{P^\circ}\right)
$$

It does **not** include the unknown mixing term `RT ln(x_j)`, because solving for `x_j` is the whole point of the exponential equation.

So the reaction solver should not call a method that returns:

$$
g_j + RT \ln(x_j) + \text{pressure term}
$$

and then put that value inside:

$$
x_j = \exp\left(-\frac{\mu_j}{RT} + \sum_i \lambda_i A_{ij}\right)
$$

That double-counts the mole-fraction term and makes the result depend on an old or guessed `x_j`. In code terms, `SpeciesPhase.Getmu(T, P, x_j)` is useful for comparing already-known phases or reporting the chemical potential of an existing mixture. The Element Potential Method needs a separate pure term, for example `GetPureMuForEquilibrium(T, P)`, that excludes `RT * Math.Log(x_j)`.

### Model A: Pool-Only Equilibrium

The reaction pool is treated as its own ideal mixture. Existing locked moles affect the volume's total `T`, `P`, mass, heat capacity, and internal energy, but they do not enter the reaction pool's mole fractions.

This is the recommended first version because it preserves the gameplay rule exactly:

1. Only dissociated material can react.
2. The Element Potential Method remains the normal unconstrained positive-moles solve.
3. No product species needs a lower-bound constraint.
4. The code remains close to the STANJAN equations already documented.

In this model, do not pass `vec_moles_existing[j] / vec_n[phase]` into `Getmu` for the reaction solver. The `x_j` in the Element Potential Method is the unknown pool mole fraction, not the old box mole fraction.

### Model B: Locked Remainder As A Bath

The more physically coupled version minimizes total free energy of:

$$
n_j = l_j + r_j
$$

subject to:

$$
A r = b
$$

and:

$$
r_j \geq 0
$$

Now the chemical potential uses the final total mixture:

$$
x_j = \frac{l_j + r_j}{L_m + R_m}
$$

where `L_m` is the locked total in phase `m`, and `R_m` is the reactive pool total in phase `m`.

This creates lower-bound constraints. A normal Element Potential Method update can imply:

$$
l_j + r_j < l_j
$$

which means:

$$
r_j < 0
$$

That is impossible because locked moles cannot be consumed unless they dissociate. The correct treatment is an active-set constrained optimization:

1. Solve for candidate `r_j`.
2. If any `r_j < 0`, clamp that species at `r_j = 0` and remove it from the active product set.
3. Re-solve with the reduced active set.
4. Repeat until all active products have `r_j >= 0`.

This is more accurate, but it is not needed for the first version.

## What CEA Means By Starting From Zero

CEA does not require the element inventory to be zero. It starts species moles from an initial guess and converges to equilibrium for a nonzero element inventory.

The conflict is not "CEA starts from zero elements." The conflict is that CEA assumes all elements in the problem are free to redistribute. Pile Simulator 3 only wants the dissociated fraction to redistribute.

So the modification is simple at the problem boundary:

1. Do not give the solver the whole box's element inventory.
2. Give it only the element inventory of the dissociated pool.
3. Add the returned product moles to the locked remainder.

## Reusing Previous Element Potentials

Keeping `vec_lambda` from the previous frame is still useful, but it must correspond to the same active element view and phase active set.

Invalidate and restart when:

1. The element bitmask changes.
2. The active phase set changes.
3. The candidate species set changes in a way that changes matrix shape.

The initial `N_m` guess should come from the reaction pool, not from `42.0` moles. A safer first guess is:

1. `N_gas = max(poolMoles, epsilon)` if gas is active.
2. `N_liquid` and `N_solid` are absent unless the active-set phase logic turns them on.
