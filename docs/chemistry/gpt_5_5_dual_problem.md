# Element Potential Method: The Dual Problem

This note explains what PDF 1 means by "the dual problem" in the Element Potential Method, translated into the notation used in Pile Simulator 3.

## Short Version

The original, or primal, equilibrium problem is:

$$
\text{Choose species moles } N_j \text{ to minimize } G \text{ while conserving every element.}
$$

The dual problem rewrites the same equilibrium problem so the unknowns are not species moles. The unknowns are:

$$
\lambda_i \quad \text{and} \quad \bar N_m
$$

where:

$$
\lambda_i = \text{element potential of element } i
$$

$$
\bar N_m = \text{total moles in phase } m
$$

Once $\lambda_i$ and $\bar N_m$ are known, every species mole amount is derived directly.

## Notation

PDF 1 uses:

$$
j = \text{species index}
$$

$$
i = \text{element index}
$$

$$
m = \text{phase index}
$$

$$
a = \text{number of elements}
$$

$$
s = \text{number of species}
$$

$$
p = \text{number of phases}
$$

For species and element data:

$$
n_{ij} = \text{number of atoms of element } i \text{ in species } j
$$

$$
p_i = \text{total inventory of element } i \text{, in mol atoms}
$$

For species amounts:

$$
N_j = \text{moles of species } j
$$

$$
\bar N_m = \text{total moles in phase } m
$$

$$
x_j = \text{mole fraction of species } j \text{ within its phase}
$$

For thermodynamics:

$$
\tilde g_j = \frac{g_j(T,P)}{RT}
$$

where $g_j(T,P)$ is the pure-species molar Gibbs free energy at the current temperature and pressure.

## Species Moles From Phase Moles

For a species $j$ in phase $m$:

$$
x_j = \frac{N_j}{\bar N_m}
$$

Therefore:

$$
N_j = \bar N_m x_j
$$

If the same chemical species can exist in more than one phase, PDF 1 treats each species-phase pair as a separate species index $j$.

## Element Potential Formula

At equilibrium, the species mole fraction is:

$$
x_j = \exp\left(-\tilde g_j + \sum_{i=1}^{a} \lambda_i n_{ij}\right)
$$

This is PDF 1 equation 2.9.

Important sign note: the pure-species free-energy term is negative. If code uses a variable named $\mu_j / RT$, then either:

$$
\frac{\mu_j}{RT} = \tilde g_j
$$

and the equation is:

$$
x_j = \exp\left(-\frac{\mu_j}{RT} + \sum_{i=1}^{a} \lambda_i n_{ij}\right)
$$

or the stored variable must already mean:

$$
-\tilde g_j
$$

## The Equations To Solve

The dual problem solves for:

$$
\vec y =
\begin{bmatrix}
\lambda_1 \\
\lambda_2 \\
\vdots \\
\lambda_a \\
\bar N_1 \\
\bar N_2 \\
\vdots \\
\bar N_p
\end{bmatrix}
$$

Given a guess for $\lambda_i$ and $\bar N_m$, calculate every $x_j$:

$$
x_j = \exp\left(-\tilde g_j + \sum_{i=1}^{a} \lambda_i n_{ij}\right)
$$

Then calculate two kinds of residuals.

### Element Balance Residuals

For each element $i$:

$$
H_i = \sum_{j=1}^{s} \bar N_{(j)} n_{ij} x_j - p_i
$$

where $\bar N_{(j)}$ means the total mole amount of the phase containing species $j$.

At equilibrium:

$$
H_i = 0
$$

### Phase Normalization Residuals

For each phase $m$:

$$
Z_m = \sum_{j \in m} x_j
$$

At equilibrium, mole fractions in each phase sum to one:

$$
Z_m - 1 = 0
$$

### Nonlinear System

The full nonlinear system is:

$$
F(\lambda, \bar N) =
\begin{bmatrix}
H_1 \\
H_2 \\
\vdots \\
H_a \\
Z_1 - 1 \\
Z_2 - 1 \\
\vdots \\
Z_p - 1
\end{bmatrix}
=
\vec 0
$$

This is the practical meaning of the dual problem.

## What The Dual Function W Is

PDF 1 defines a scalar function:

$$
W(\lambda, \bar N) = \sum_{m=1}^{p} \bar N_m (Z_m - 1) - \sum_{i=1}^{a} \lambda_i p_i
$$

This is the dual function.

Its derivatives are the residuals above:

$$
\frac{\partial W}{\partial \lambda_i} = H_i
$$

and:

$$
\frac{\partial W}{\partial \bar N_m} = Z_m - 1
$$

So a stationary point of $W$ satisfies:

$$
H_i = 0
$$

and:

$$
Z_m - 1 = 0
$$

That is exactly the equilibrium condition in the dual variables.

At the final equilibrium point, PDF 1 states:

$$
W^*_{\max} = -\frac{G}{RT}
$$

So maximizing the dual corresponds to minimizing the original Gibbs free energy.

## Why It Is Called Max-Min

The PDF describes a max-min problem:

$$
\max_{\bar N} \min_{\lambda} W(\lambda, \bar N)
$$

Conceptually:

1. For fixed phase mole totals $\bar N_m$, adjust $\lambda_i$ until element balances are satisfied.
2. Then adjust $\bar N_m$ until each phase's mole fractions sum to one.

The first step is the $\lambda$ side of the problem. The second step is the $\bar N$ side of the problem.

For implementation, it is usually easier to ignore the steepest-descent/ascent framing and directly solve the residual equations with Newton's method.

## Newton's Method

Define:

$$
D_{im} = \sum_{j \in m} n_{ij} x_j
$$

and:

$$
Q_{ik} = \sum_{j=1}^{s} \bar N_{(j)} n_{ij} n_{kj} x_j
$$

Then the Newton system has block form:

$$
\begin{bmatrix}
Q & D \\
D^T & 0
\end{bmatrix}
\begin{bmatrix}
\Delta \lambda \\
\Delta \bar N
\end{bmatrix}
=
-
\begin{bmatrix}
H \\
Z - 1
\end{bmatrix}
$$

Where:

$$
Q \text{ is } a \times a
$$

$$
D \text{ is } a \times p
$$

$$
D^T \text{ is } p \times a
$$

$$
0 \text{ is } p \times p
$$

After solving the linear system:

$$
\lambda \leftarrow \lambda + \Delta \lambda
$$

$$
\bar N \leftarrow \bar N + \Delta \bar N
$$

Then recalculate:

$$
x_j = \exp\left(-\tilde g_j + \sum_{i=1}^{a} \lambda_i n_{ij}\right)
$$

and repeat until:

$$
H_i \approx 0
$$

and:

$$
Z_m - 1 \approx 0
$$

Finally recover species moles:

$$
N_j = \bar N_{(j)} x_j
$$

## Single-Phase Case

If there is only one mixed reaction phase, the unknowns are:

$$
\lambda_1, \lambda_2, \ldots, \lambda_a, \bar N
$$

The mole fractions are still:

$$
x_j = \exp\left(-\tilde g_j + \sum_{i=1}^{a} \lambda_i n_{ij}\right)
$$

The phase-normalization equation is:

$$
\sum_{j=1}^{s} x_j = 1
$$

The element-balance equations are:

$$
\bar N \sum_{j=1}^{s} n_{ij} x_j = p_i
$$

for every element $i$.

This means the earlier idea of guessing total moles is close, but in STANJAN the total mole amount is a real dual variable:

$$
\bar N
$$

## Implementation Shape For Pile Simulator 3

Inputs:

$$
p_i = \text{element inventory}
$$

$$
n_{ij} = \text{species stoichiometry}
$$

$$
\tilde g_j = \text{dimensionless pure-species free energy at current state}
$$

Solver unknowns:

$$
\lambda_i
$$

$$
\bar N_m
$$

Derived quantities:

$$
x_j
$$

$$
N_j
$$

Core algorithm:

1. Guess $\lambda_i$ and $\bar N_m$.
2. Calculate all $x_j$.
3. Calculate $H_i$ and $Z_m - 1$.
4. Solve the Newton linear system for $\Delta \lambda$ and $\Delta \bar N$.
5. Update $\lambda_i$ and $\bar N_m$.
6. Repeat until residuals are small.
7. Convert mole fractions to species moles with $N_j = \bar N_{(j)} x_j$.

## Important Simplification

Multi-phase STANJAN has extra complexity because phases may be absent. A phase with $\bar N_m = 0$ cannot have ordinary mole fractions in the same way as a present phase.

For a first implementation, a single ideal mixed reaction phase is much easier:

$$
F(\lambda, \bar N) =
\begin{bmatrix}
H_1 \\
\vdots \\
H_a \\
\sum_j x_j - 1
\end{bmatrix}
=
\vec 0
$$

That is the smallest useful version of the dual problem.
