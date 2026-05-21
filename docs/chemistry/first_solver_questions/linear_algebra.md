# Linear Algebra For The Reaction Solver

## Recommended Package

Use **MathNet.Numerics**.

Add it to the Godot C# project with:

```sh
dotnet add "Pile Simulator 3.csproj" package MathNet.Numerics
```

Use these imports in C# files that build and solve matrices:

```csharp
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
```

Typical dense solve shape:

```csharp
Matrix<double> J = Matrix<double>.Build.Dense(a + p, a + p);
Vector<double> rhs = Vector<double>.Build.Dense(a + p);

// Fill J and rhs. rhs is -F.

Vector<double> delta = J.Solve(rhs);
```

This gives the missing operation in `Volume.SolveReactions`: solve `J * delta = -F` without writing a custom Gaussian elimination routine.

## Which Solver

For the current Newton system:

$$
\begin{bmatrix}
Q & D \\
D^T & 0
\end{bmatrix}
\begin{bmatrix}
\Delta \lambda \\
\Delta N
\end{bmatrix}
=
-F
$$

use `J.Solve(rhs)` first.

Do **not** use Cholesky for this full matrix. `Q` itself is symmetric positive semidefinite, but the full block matrix has the zero lower-right block and is an indefinite KKT-style matrix. Cholesky expects positive definite matrices and can fail or produce nonsense here.

If `J.Solve(rhs)` throws because the matrix is singular or nearly singular, that is usually a chemistry/model problem, not a reason to hide the error. Common causes are:

1. An active phase has no species in it.
2. An element appears in `freeElements` but no active species can consume it.
3. Two equations are redundant because the active species set is degenerate.
4. `N_m` became zero or negative during Newton.

For debugging only, `J.Svd().Solve(rhs)` can show whether the issue is rank deficiency, but it should not be the default because it can conceal bad active-set logic.

## Can Matrix Objects Remove The Slow Nested Loops?

Partly, but they should not be the first performance target.

The solver has two different kinds of work:

1. Building the chemistry data for the current state.
2. Solving the small Newton linear system.

MathNet helps a lot with the second part. It helps less with the first part because the program still has to inspect species, phases, element counts, mole fractions, and chemical potentials. That data does not become free because it is stored in a `Matrix<double>`.

For Pile Simulator 3, the number of active elements should usually be small. A matrix of `(active elements + active phases)` is probably around 3x3 to 20x20. Solving that is cheap. The loops over species are more likely to dominate, but even those are usually cheap unless the full NASA database is active every frame in many volumes.

## Useful Matrix Form

The current `SolveReactions` loops are computing these objects:

Let:

$$
A_{ij} = \text{count of element } i \text{ in species-phase } j
$$

where `A` has shape `a x s`.

Let:

$$
w_j = N_{phase(j)} x_j
$$

Then the element residual is:

$$
H = A w - p
$$

and the upper-left Jacobian block is:

$$
Q = A \operatorname{diag}(w) A^T
$$

For the phase block, define a matrix `B` with shape `s x p`:

$$
B_{jm} =
\begin{cases}
x_j & \text{if species } j \text{ is in phase } m \\
0 & \text{otherwise}
\end{cases}
$$

Then:

$$
D = A B
$$

This can be written with MathNet matrix multiplication, but building `diag(w)` and `B` allocates memory. For small systems, the direct loops are probably faster and clearer. For large systems, a cached sparse stoichiometry structure is better than repeatedly building dense matrices.

## Practical Recommendation

For the first implementation:

1. Use MathNet for `J.Solve(rhs)`.
2. Keep direct loops for building `x`, `F`, `Q`, and `D`.
3. Remove expensive accidental loops before trying to vectorize everything.

The obvious cleanup targets in `Volume.SolveReactions` are:

1. Do not call `Array.IndexOf(viewSpecies, resource.SpeciesPhase)` for every resource every iteration. Build a `Dictionary<SpeciesPhase, int>` once per view.
2. Do not restore moles with `Resources.Where(...)` inside a loop over all species. Build a `Dictionary<SpeciesPhase, SpeciesPhaseResource>` once.
3. Cache `speciesToPhase` with the view.
4. Consider caching nonzero stoichiometry entries per species or per element if the species list gets large.

Those changes will matter more than trying to force the whole chemistry assembly into matrix multiplication.
