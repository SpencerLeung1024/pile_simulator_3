# Energy Minimization using the Element Potential Method

In theory, gradient descent on h(vec_x, vec_lambda) will eventually find an equilibrium solution
In practice, using epsilon approximation of the derivative and working on a species x elements system of equations would be incredibly unstable and slow

## The Element Potential Method

According to DeepSeek V4 Pro, most real software (NASA Chemical Equilibrium Applications, Cantera, etc.) uses the Element Potential Method

There is very little information online about it
- The only primary source is this 49 page PDF from 1986: https://web.stanford.edu/~cantwell/AA283_Course_Material/AA283_Resources/STANJAN_write-up_by_Bill_Reynolds.pdf
- This 13 page article from 2001 also exists: https://web.stanford.edu/~cantwell/AA284A_Course_Material/AA284A_Resources/Camberos%20and%20Moubry,%20Chemical%20Equilibrium%20Analysis%20with%20the%20Method%20of%20Element%20Potentials%202001-0873.pdf

All other search results involve some other potential method in mechanics / physics involving springs

### Element Potentials

let s = number of species and a = number of elements
Let j = a (grammatical a, not variable a) species and i = an element

The element potential of an element is λ_i
This is equal to the Lagrangian multiplier

Unfortunately, I am too dumb to follow the math on how the method of Lagrange multipliers working on (s + a) variables gets transformed into the Element Potential Method working on (a) variables

The Element Potential Method treats element potentials as variables to be solved, and species moles as derived quantities
The exact amount of a species is:

x_j = e^(-(μ_j / RT) + [ λ_i * n_ij for i in range(a) ])

(2.9 on page 6 in PDF 1. Some variables reworded to better correspond to Pile Simulator 3 docs)
n_ij is the number of atoms of element i in species j. n_H_H2O = 2

This is in contrast to methods that iterate on species moles

### Advantages

Because of how x_j is defined (as e^something), it is always positive
You may end up with extremely low amounts of rare species (CO in nearly complete combustion, etc.), but real life works like that too
Importantly, methods that iterate on species moles need to constantly check if they produced negative moles of a species and lift those back to zero, because the method of Lagrange multipliers works on unconstrained optimization problems
Because the number of elements (tens unless you're involving superheavy elements for some reason) is far lower than the number of species (potentially thousands), one step of the Element Potential Method is far faster and benefits more from cache locality

### Molar Ratio

x_j is the molar *ratio*. sum of all x_j = 1
To get moles of species j (n_j), n_j = x_j * total_n
But how do you get total_n?
total_n changes as species dissociate and reform. O, O2, O3, etc.
- Guess total_n
- Sum up total_n_elements by multiplying all x_j * total_n * formula
- Adjust total_n up and down until total_n_elements matches expected

### Ideal Gases and Ideal Solutions

Interestingly, PDF 1 also treats all liquids as an ideal solution
An alkane species and water will mix, forming a liquid phase with volume equal to what their volumes would have been if they formed separate clumps
Even solids form an ideal solution of solids
This means that the chemical potential of liquids and solids includes an entropy of mixing: RT ln(x_j_in_phase)
(2.9) above already includes this entropy of mixing in its equation

So, it appears that using the Element Potential Method requires me to change Pile Simulator 3's assumption in the other direction:
- All liquids form an ideal solution
- All solids form an ideal solution

The actual in-game equilibrium will be some weird incorrect combination of both ideal reaction and cubic EOS

I have no idea what the Element Potential Method with a non-ideal gas and non-ideal solutions would look like, or if it is even tractable

### Supercritical Fluid Is Just Gas

STANJAN does not treat supercritical fluid as a different phase from gas
Turbine simulations just use gas species

### Initial Guess

A dozen pages of PDF 1 are dedicated to STANJAN's algorithm for finding dominant species for each element, setting the initial guess to have their moles at maximum, and how this drastically reduces the number of steps needed for convergence to acceptable errors
Unfortunately I understand none of it
Hopefully the fact that the problem is convex means that my lazy guess (all λ_i initialized to 0) and 40 years of CPU improvement will still converge in an acceptable time

## Newton's Method with the Jacobian Matrix

### The Jacobian Matrix

Suppose you had a system of m non-linear equations, each accepting n variables:

f_1(vec_x) = ... = 0
f_2(vec_x) = ... = 0
...
f_m(vec_x) = ... = 0

These don't correspond to any equation above. They are the equations in the dual problem, mentioned below

           +                                   +
           | ∂f_1/∂x_1 ∂f_1/∂x_2 ... ∂f_1/∂x_n |
J(vec_x) = | ∂f_2/∂x_1 ∂f_2/∂x_2 ... ∂f_1/∂x_n |
           | ...       ...       ... ...       |
           | ∂f_m/∂x_1 ∂f_m/∂x_2 ... ∂f_m/∂x_n |
           +                                   +

The Jacobian matrix is an m by n matrix of partial derivatives

You can iteratively find a solution vec_x by doing:

next_vec_x = vec_x - J(vec_x)^-1 @ F(vec_x)

Where F(vec_x) is a column vector, where item 1a is f_a(vec_x)

But finding the inverse of a matrix takes a long time

Using some linear algebra tricks:

(next_vec_x - vec_x) = -J(vec_x)^-1 @ F(vec_x)
J(vec_x) @ (next_vec_x - vec_x) = J(vec_x) @ -J(vec_x)^-1 @ F(vec_x)
J(vec_x) @ (next_vec_x - vec_x) = -F(vec_x) because AA^-1 = I
J(vec_x) @ Δvec_x = -F(vec_x)

This is a linear system in the form Ax = b, which can be solved with linear algebra

next_vec_x = vec_x + Δvec_x

You need to recalculate F(vec_x) and J(vec_x) every step. J(vec_x) requires m * n partial derivatives

## The Dual Problem (The Thing Newton's Method Is Solving)

Pages 11 - 23 of PDF 1 go over the dual problem: a system of non-linear equations whose solutions correspond to element potentials, vec_lambda

Unfortunately I am completely unable to follow along
