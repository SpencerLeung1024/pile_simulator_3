# The Dual Problem in the Element Potential Method

The two PDFs are scanned images. Neither WebFetch nor I can extract text from them.
So this doc is built from first principles, the writeups already in `docs/chemistry`,
and the standard CEA / STANJAN formulation that's covered in graduate-level
combustion textbooks.

## TL;DR

**The primal problem** is: pick non-negative species moles $n_1, ..., n_S$ that
minimize total Gibbs (or Helmholtz) energy, subject to element conservation
constraints. There are $S$ unknowns and $E$ constraints.

**The dual problem** is: pick element potentials $\lambda_1, ..., \lambda_E$
that satisfy element conservation, where the species moles are *no longer
independent unknowns* — they're explicit functions of $\vec\lambda$. There are
only $E$ unknowns and $E$ equations.

You went from $S$ unknowns (potentially hundreds) to $E$ unknowns (usually 5-15).
That's the whole point.

The dual problem is what you solve with Newton's method. The Jacobian is $E \times E$.
Inverting a 10×10 matrix is trivial. Inverting a 500×500 matrix is not.

## Setting Up The Primal

Pick chemical potential to be Gibbs (constant T, P) for concreteness. Same math
works for Helmholtz (constant T, V), just with different $\mu_i^\circ$ definitions.

Variables: $\vec n = (n_1, ..., n_S)$. $n_i \geq 0$ is moles of species $i$.

Objective:
$$G(\vec n) = \sum_{i=1}^S n_i \mu_i(\vec n)$$

For ideal gas species:
$$\mu_i(\vec n) = \mu_i^\circ(T) + RT \ln \frac{n_i}{n_\text{total}} + RT \ln \frac{P}{P^\circ}$$

For pure condensed species (ideal solid/liquid solution per `energy_minimization_3.md`):
$$\mu_i = \mu_i^\circ(T) + RT \ln x_i^\text{(phase)}$$

where $x_i^\text{(phase)}$ is the mole fraction of $i$ within its own
condensed phase (solid solution or liquid solution).

Constraints: for each element $j = 1, ..., E$:
$$g_j(\vec n) = \sum_{i=1}^S a_{ij} n_i - N_j = 0$$

where $a_{ij}$ is the number of atoms of element $j$ in species $i$, and $N_j$
is the total moles of atomic element $j$ available in the box.

Non-negativity: $n_i \geq 0$ for all $i$.

This is the primal. **Hard to solve directly** because (a) $S$ is large, (b)
$\mu_i$ depends on the entire $\vec n$ through the $\ln(n_i / n_\text{total})$
term, and (c) you have to keep all $n_i \geq 0$.

## The Lagrangian

$$\mathcal L(\vec n, \vec\lambda) = \sum_i n_i \mu_i(\vec n) - \sum_j \lambda_j \left( \sum_i a_{ij} n_i - N_j \right)$$

(Sign convention varies by author. Plus or minus on $\lambda_j$ doesn't matter
mathematically — it just flips the sign of the multipliers.)

At the optimum, KKT conditions give:

**Stationarity** ($\partial \mathcal L / \partial n_i = 0$):
$$\mu_i(\vec n) - \sum_j \lambda_j a_{ij} = 0$$

That is:
$$\boxed{\mu_i(\vec n) = \sum_j \lambda_j a_{ij}}$$

This is the *key insight*. It says: **at equilibrium, the chemical potential of
every species equals the sum of element potentials weighted by the species'
atomic composition.**

You can read it like a price. $\lambda_j$ is the "price" of one mole of atomic
element $j$. The chemical potential of a molecule like $\text{H}_2\text{O}$
must equal $2\lambda_H + 1\lambda_O$, because that's how much "atomic currency"
it costs to assemble.

**Primal feasibility** ($\partial \mathcal L / \partial \lambda_j = 0$):
$$\sum_i a_{ij} n_i = N_j$$

Just the original element constraint.

## Where the Dual Problem Comes From

This is where you went `?`.

Notice the stationarity condition. For ideal gas $i$:
$$\mu_i^\circ + RT \ln \frac{n_i}{n_\text{total}} + RT \ln \frac{P}{P^\circ} = \sum_j \lambda_j a_{ij}$$

**Solve for $n_i$:**
$$n_i = n_\text{total} \cdot \frac{P^\circ}{P} \cdot \exp\!\left( \frac{\sum_j \lambda_j a_{ij} - \mu_i^\circ}{RT} \right)$$

This is your equation (2.9) from PDF 1, rearranged. It says: **given the
element potentials $\vec\lambda$, you can compute every $n_i$ in closed form.**

So $n_i$ is no longer an independent unknown. It's a function $n_i(\vec\lambda)$.

The $S$ unknowns collapse into the $E$ unknowns $\vec\lambda$.

Now substitute $n_i(\vec\lambda)$ back into the element constraint:
$$\sum_i a_{ij} \, n_i(\vec\lambda) - N_j = 0 \quad \text{for each } j = 1, ..., E$$

This is the dual problem. **$E$ nonlinear equations in $E$ unknowns
$\vec\lambda$.**

Let me write it out:
$$F_j(\vec\lambda, n_\text{total}) = \sum_i a_{ij} \, n_i(\vec\lambda, n_\text{total}) - N_j = 0$$

Plus one extra equation to close $n_\text{total}$:
$$F_0 = \sum_i n_i(\vec\lambda, n_\text{total}) - n_\text{total} = 0$$

So actually $E+1$ equations in $E+1$ unknowns ($\vec\lambda$ plus $n_\text{total}$).
Still way smaller than $S$.

## Why It's Called "Dual"

In convex optimization, every constrained minimization (the primal) has a
matched problem called the dual. The dual maximizes some lower bound built
from the Lagrange multipliers.

For a strictly convex primal with linear constraints (your case, because the
$\ln$ terms make $G$ strictly convex), the dual is also convex, and:

- **Strong duality** holds: primal optimum equals dual optimum.
- The dual variables $\vec\lambda$ at the dual optimum are *exactly* the
  Lagrange multipliers at the primal optimum.

In other words, solving the dual gives you the same answer as solving the
primal, but it's done in a smaller variable space.

PDF 1's chapter on the dual probably writes the dual as a maximization. Something like:
$$\max_{\vec\lambda} \sum_j N_j \lambda_j - \text{(some convex function of }\vec\lambda)$$

The specific functional form is:
$$\text{Dual}(\vec\lambda) = \sum_j N_j \lambda_j - n_\text{total}(\vec\lambda) \cdot RT$$

(Where $n_\text{total}(\vec\lambda) = \sum_i n_i(\vec\lambda)$ as defined
above. This is concave in $\vec\lambda$, so its max is unique.)

But in practice, **you don't maximize the dual function.** You solve the
gradient-zero condition $F_j = 0$ for each $j$ using Newton's method, which is
faster.

## How to Solve It

This is what page 11-23 of PDF 1 describes.

### Step 1: Initial guess

Page 7 onwards is the dominant-species algorithm that picks a smart initial
guess. You can ignore it. Start with $\vec\lambda = \vec 0$ and
$n_\text{total} = \sum_j N_j$. Modern CPUs are fast.

Caveat: For very off-stoichiometric mixtures (e.g. trace fluorine in mostly
hydrocarbons), this initial guess can converge slowly or fail. If that bites
you later, revisit STANJAN's algorithm.

### Step 2: Compute $n_i$ for current $\vec\lambda$

For each species $i$ (gas):
$$n_i = n_\text{total} \cdot \frac{P^\circ}{P} \cdot \exp\!\left( \frac{\sum_j a_{ij} \lambda_j - \mu_i^\circ(T)}{RT} \right)$$

For pure condensed species, see condensed-phase handling below.

### Step 3: Compute residuals

For each element $j$:
$$F_j = \sum_i a_{ij} n_i - N_j$$

Plus the closure equation:
$$F_0 = \sum_{i \in \text{gas}} n_i - n_\text{total}$$

(Condensed species are excluded from $F_0$ because they don't share the gas
mole-fraction normalization.)

### Step 4: Build Jacobian

The Jacobian $J$ is $(E+1) \times (E+1)$. Its entries come from differentiating
$F_j$ with respect to $\lambda_k$ and $n_\text{total}$. Since
$\frac{\partial n_i}{\partial \lambda_k} = n_i \cdot \frac{a_{ik}}{RT}$:

$$\frac{\partial F_j}{\partial \lambda_k} = \sum_i a_{ij} a_{ik} \frac{n_i}{RT}$$

And $\frac{\partial n_i}{\partial n_\text{total}} = \frac{n_i}{n_\text{total}}$
for gases:

$$\frac{\partial F_j}{\partial n_\text{total}} = \sum_{i \in \text{gas}} a_{ij} \frac{n_i}{n_\text{total}}$$

$$\frac{\partial F_0}{\partial \lambda_k} = \sum_{i \in \text{gas}} a_{ik} \frac{n_i}{RT}$$

$$\frac{\partial F_0}{\partial n_\text{total}} = \frac{\sum_{i \in \text{gas}} n_i}{n_\text{total}} - 1$$

Note that the upper-left $E \times E$ block of $J$ is **symmetric and positive
semi-definite**. It's actually $\frac{1}{RT} A^T \text{diag}(n_i) A$, where $A$
is the atom-count matrix. That structure means you can solve the linear system
with Cholesky decomposition (faster than general LU). The full $(E+1)$-block
loses pure symmetry once you include $n_\text{total}$, but you can eliminate
$n_\text{total}$ analytically and keep solving the symmetric $E \times E$
system. (STANJAN does exactly that.)

### Step 5: Newton step

Solve the linear system:
$$J \cdot \Delta\vec x = -\vec F$$

where $\vec x = (\lambda_1, ..., \lambda_E, n_\text{total})$ and $\vec F = (F_1, ..., F_E, F_0)$.

Update:
$$\vec x \leftarrow \vec x + \alpha \cdot \Delta\vec x$$

The step size $\alpha$ should be limited to prevent overshoot. A common
heuristic: clamp so that no $|\Delta \ln n_i|$ exceeds 2 or so. Otherwise the
exponential in step 2 explodes.

In practice: compute the largest $|\Delta \ln n_i|$, and if it exceeds some
threshold (say 4), set $\alpha = 4 / (\text{largest } |\Delta \ln n_i|)$.
Otherwise $\alpha = 1$. This is called *step damping*. STANJAN does this.

### Step 6: Check convergence

If $\max_j |F_j| / N_j < \text{tolerance}$ (e.g. 1e-6), done. Otherwise go to
step 2.

Newton converges quadratically near the solution. Typically 5-20 iterations
total, regardless of $S$.

## Condensed Phase Handling

A pure condensed species (no mixing) has constant $\mu_i = \mu_i^\circ(T)$ —
no $\ln(n_i)$ term, so its $n_i$ is **not** an explicit function of
$\vec\lambda$. The stationarity condition becomes an inequality:

- If $\mu_i^\circ(T) > \sum_j a_{ij} \lambda_j$: species is **unstable** at
  current $\vec\lambda$, set $n_i = 0$.
- If $\mu_i^\circ(T) < \sum_j a_{ij} \lambda_j$: species **should appear**.
  $n_i$ is determined by whatever element balance is left over after the gas
  phase consumes its share. This adds extra unknowns and constraints.
- If $\mu_i^\circ(T) = \sum_j a_{ij} \lambda_j$: species sits on the phase
  boundary.

This is where the algorithm gets messy. CEA / STANJAN handle it with a
phase-activation outer loop: start with no condensed species, solve the dual,
check if any condensed species has $\mu_i^\circ(T) < \sum_j a_{ij} \lambda_j$.
If so, add it to the active set and re-solve. If a condensed species in the
active set wants $n_i < 0$, remove it.

For an ideal solid/liquid *solution* (multiple condensed species mixing per
`energy_minimization_3.md`), each phase $k$ has its own $n_\text{total}^{(k)}$:

$$\mu_i^{(k)} = \mu_i^\circ(T) + RT \ln \frac{n_i^{(k)}}{n_\text{total}^{(k)}}$$

and you get an extra closure equation per phase. The dual problem then has
$E + (\text{number of phases})$ unknowns. Still tiny.

## Why the Dual Beats Minimizing h

Looking back at `energy_minimization_2.md`:

- Minimizing $h$ via finite differences: $O(S^2)$ per gradient step, hundreds
  of steps, unstable near saddle points.
- Dual problem with Newton: $O((E+1)^3)$ per step for the linear solve plus
  $O(S \cdot E)$ to assemble the Jacobian, ~10 steps total, quadratic
  convergence.

For $S = 30$, $E = 5$: dual is roughly 30× faster per step and converges in
many fewer steps. For $S = 500$, $E = 10$: dual is roughly 2500× faster per
step. The bigger your species database, the bigger the win.

Also crucial: $n_i = n_\text{total} \cdot P^\circ / P \cdot \exp(\cdot)$ is
**always positive**. No need to clamp negative species moles to zero between
iterations. The non-negativity constraint is automatically enforced by the
exponential — a real numerical luxury.

## Going Back to the Primal

After Newton converges on $\vec\lambda$:
1. Compute every $n_i$ from the closed-form expression.
2. Sanity check: $\sum_i a_{ij} n_i \approx N_j$ for all $j$.
3. Sanity check: $\sum_i n_i \approx n_\text{total}$.
4. These $n_i$ are your equilibrium composition.

You don't directly use $\vec\lambda$ for anything else (it's an intermediate
quantity), although it has a physical interpretation: $\lambda_j$ is the
chemical potential of pure atomic element $j$ at the equilibrium state. If
free atomic hydrogen actually existed in your box, its chemical potential
would be $\lambda_H$.

## Mapping Onto Pile Simulator 3

In your `SolveReactions()` from `energy_minimization_2.md`:

```
SolveReactions():
    // 1. Dissociation step (unchanged — pulls atoms into freeElements)
    ...

    // 2. Set up dual problem
    int E = freeElements.Count
    int S = allCandidateSpeciesPhases.Length
    float[] lambda = new float[E]  // start at 0
    float n_total = freeElements.Values.Sum()  // initial guess

    // 3. Newton iteration
    for (int iter = 0; iter < MAX_ITER; iter++):
        // Compute all n_i from current lambda
        float[] n = new float[S]
        for each species i:
            float exponent = (sum_j a[i,j] * lambda[j] - mu_std[i](T)) / (R*T)
            n[i] = n_total * (P_std / P) * exp(exponent)

        // Compute residuals F
        float[] F = new float[E + 1]
        for each element j:
            F[j] = -N[j]
            for each species i:
                F[j] += a[i,j] * n[i]
        F[E] = -n_total
        for each gas species i:
            F[E] += n[i]

        // Check convergence
        if max |F[j]/N[j]| < TOL: break

        // Build Jacobian
        float[,] J = new float[E+1, E+1]
        // ... (see Step 4 above)

        // Solve J * delta = -F (LU or Cholesky)
        float[] delta = SolveLinearSystem(J, -F)

        // Damp step size
        float max_dlnN = max over i of |sum_j a[i,j] * delta[j] / (R*T) + delta[E]/n_total|
        float alpha = (max_dlnN > 4) ? 4 / max_dlnN : 1

        // Update
        for each j: lambda[j] += alpha * delta[j]
        n_total += alpha * delta[E]

    // 4. Write final moles back to SpeciesPhases
    for each species i: SpeciesPhases[i].n = n[i]

    return H_after - H_before
```

The only "hard" part is `SolveLinearSystem` on an (E+1)×(E+1) matrix. For E up
to ~30, even naive Gaussian elimination is fast. C# doesn't have a built-in
linear solver, but writing one is ~50 lines, or use a tiny dependency.

## Caveats Specific to Your Game

1. **Per-frame budget.** Newton converges in ~10 iterations. Each iteration is
   O(S·E + E³). For S=100, E=8: maybe 1000 cycles per iteration × 10 = 10000
   per call. A few microseconds. Cheap.

2. **Coupling to phase equilibrium.** The dual problem assumes T and P are
   fixed. Coupling to your cubic-EOS pressure-from-volume loop means an
   outer loop wrapping the dual solver. See `energy_minimization_2_review.md`
   section 4.

3. **Non-ideal everything.** PDF 1 assumes ideal gas and ideal solutions. If
   you want non-ideal cubic EOS *and* element-potential method, the
   dual-problem closed form for $n_i$ breaks because $\mu_i$ depends on the
   full composition through the EOS mixing rules. STANJAN does not handle
   this; modern tools (Cantera in some modes) handle it iteratively.

   In practice, for a game, do what PDF 1 does: assume ideal gas / ideal
   solutions for the reaction solver, and run the cubic EOS only for
   pressure-volume mechanical work. The two solvers run as separate
   subsystems; they don't share the non-ideality.

4. **Dissociation gate.** Your dissociation temperature gate is independent of
   the dual problem. The dual problem just decides what to *do* with the
   atoms that dissociation has released into `freeElements`. Diamond will
   still persist at low T because at low T, no dissociation happens, so no
   atoms get fed into the dual solver, so nothing recombines.

## Recap

| Concept | Primal | Dual |
|---------|--------|------|
| Variables | $\vec n$, $S$ unknowns | $\vec\lambda$ (and $n_\text{total}$), $E+1$ unknowns |
| Objective | min $\sum n_i \mu_i$ | max (concave function of $\vec\lambda$), but in practice solve $F_j(\vec\lambda) = 0$ |
| Constraints | $E$ element conservation, $S$ non-negativity | None (positivity automatic via exp) |
| Solver | Hard (large S, ln coupling, $\geq 0$) | Newton's method on small $E+1$ system |
| Outcome | $\vec n^*$ directly | $\vec\lambda^*$ → plug in for $\vec n^*$ |

The dual problem is the same physics, repackaged in a vastly more tractable
variable space.
