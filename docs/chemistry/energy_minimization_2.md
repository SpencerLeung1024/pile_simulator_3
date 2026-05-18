# Energy Minimization using the Method of Lagrange Multipliers

So, basically, I actually read https://en.wikipedia.org/wiki/Lagrange_multiplier and realized it doesn't look impossible and solves exactly what I need to solve

Dissociation still uses the same rules as in `docs/chemistry/energy_minimization.md`
Each species has a dissociation temperature, below which it will not dissociate and provide elements to the reaction solver

There is also a phase solver, which doesn't work in terms of elements but in per-species moles. It has no minimum temperature but only moves moles to the specie's gas, liquid, supercritical fluid, and solid phases. This allows molecules like pentane to evaporate or condense in a box with oxygen without combusting

## Chemical Potential

DeepSeek V4 Pro said that instead of writing separate Gibbs free energy G and Helmholtz free energy A everywhere, write a single chemical potential, μ
Always write equations and code in terms of mu
A system always tries to minimize mu, and gets mu by asking its species phases
When mu is obtained at constant pressure, it is G. At constant volume, it is A

## Constrained Optimization

What's the best way to minimize chemical potential?
Make infinity moles of carbon dioxide and infinity moles of water
Except for a little pesky thing called conservation of mass
The box has n_H moles of atomic hydrogen, n_C moles of atomic carbon, and n_O moles of atomic oxygen
You need to find the right amount of species that both:
1. Minimizes chemical potential
2. Uses exactly all moles of elements in the box. No more, no less

## The Lagrangian Function

https://en.wikipedia.org/wiki/Lagrange_multiplier

The Lagrangian function (multiple inputs, single constraint) takes the form:

L(vec_x, lambda) = f(vec_x) + lambda * g(vec_x)

Or, if you only have a single input:

L(x, lambda) = f(x) + lambda * g(x)

lambda is a gradient scaling factor to make ∇f at vec_x equal to λ * ∇g at vec_x. This checks if ∇f(x) and ∇g(x) are parallel (possibly with a negative λ indicating opposite directions)

f(x) is the objective function. It is what you want to minimize, such as chemical potential
f(x) is any R^n -> R (scalar) function. Here, vec_x is moles of species phases and f(vec_x) is total chemical potential

g(x) is the constraint function. It has the form:
ax_1 + bx_2 + cx_3 + ... - constant = 0
It tells you what vec_x are valid solutions given a constraint (conservation of **one** element, see below for multiple elements)

### Multiple Constraints

Each g(x) can only constrain **one** element. There is a g(x) for hydrogen:

0 * n_CO2 + 2 * n_H2O + 4 * n_CH4 + 0 * n_graphite + 2 * n_H2 + 0 * n_O2 - n_H = 0

But we need to constrain carbon and oxygen too.

1 * n_CO2 + 0 * n_H2O + 1 * n_CH4 + 1 * n_graphite + 0 * n_H2 + 0 * n_O2 - n_C = 0

2 * n_CO2 + 1 * n_H2O + 0 * n_CH4 + 0 * n_graphite + 0 * n_H2 + 2 * n_O2 - n_O = 0

So in our reaction solver, we need multiple lambda and multiple g(x)

L(vec_x, vec_lambda) = f(vec_x) + [ vec_lambda[e] * vec_g[e](vec_x) for e in range(len(vec_g)) ]

The phase solver can be single constraint though, because each time it solves something it only has one conserved quantity: moles of the species

### From Constrained to Unconstrained

So why use the Lagrangian function?

It turns a constrained problem (... = 0) into an unconstrained problem (lambda can be anything; solve when certain derivatives are 0)

See the 2 input, 1 constraint function at https://en.wikipedia.org/wiki/Lagrange_multiplier#Example_1

L(x, y, lambda) = f(x, y) + lambda * g(x, y)
                = x + y + lambda * (x^2 + y^2 - 1)

We need (x, y, lambda) where ∇L(x, y, lambda) = [∂L/∂x, ∂L/∂y, ∂L/∂λ] = vec_0

If you can manipulate L algebraically, you can solve a system of three equations and get all (x, y, lambda) that satisfy this

Sounds great! Except...

### Solutions are Saddle Points, not Local Extrema

This is extremely difficult to explain without the figure at https://en.wikipedia.org/wiki/Lagrange_multiplier#Example_5_%E2%80%93_Numerical_optimization

The solutions (x, y) are local extrema of f(x, y), but with the addition of the third variable lambda they are *saddle points* of L(x, y, lambda)

Typical tools like gradient descent can't find saddle points. They look for local minima and roll along the L surface to negative infinity

We need to transform L so that gradient descent converges to local minima, which are saddle points of L, which are local minima of f

### h: Squared Gradient

Introducing h:

h(vec_x, vec_lambda) = (∂L/∂x_1)^2 + (∂L/∂x_2)^2 + (∂L/∂x_3)^2 + ... + (∂L/∂x_last_species)^2 + (∂L/∂λ_1)^2 + (∂L/∂λ_2)^2 + (∂L/∂λ_3)^2 + ... + (∂L/∂λ_last_element)^2

If you cannot algebraically manipulate L to get ∂L/∂x and ∂L/∂λ, you can use the epsilon approximation for the derivative:

∂L/∂x = (L(x + epsilon, lambda) - L(x, lambda)) / epsilon
∂L/∂λ = (L(x, lambda + epsilon) - L(x, lambda)) / epsilon

For each x_i and λ_i

It happens that the local minima of h are the saddle points of L

So do gradient descent on h, to find a saddle point of L, to find a local minimum of f

### Supplementary Videos

- Serpentine Integral: Understanding Lagrange Multipliers Visually: https://www.youtube.com/watch?v=5A39Ht9Wcu0
- MATLAB: Constrained Optimization: Intuition behind the Lagrangian: https://www.youtube.com/watch?v=GR4ff0dTLTw

Note that both videos solve slightly different problems (len(vec_x) = 2, only one g) from what we're interested in

## Convexity

DeepSeek V4 Pro called this a "constrained convex optimization problem"

The convexity comes from the fact that as you make more and more moles of a species in the box, the chemical potential, the "value" of making another mole of that species, becomes less negative

Assuming my knowledge isn't completely messed up from unuse, CMPT 410: Machine Learning says:

"A convex function means a single global minimum exists, any local minimum reached must be that global minimum, and gradient descent starting from anywhere will reach the same global minimum."

Convexity makes one solution out of a string of possible solutions on a line. Let's take a step back to something far more amenable to the average person: ECON 105: Principles of Macroeconomics

You have $10. A burger is $5. A fries and drink combo is $5. You must spend $10 because you are in an economics model. Will you:
- Buy 2 burgers and 0 combos?
- Buy 1 burger and 1 combo?
- Buy 0 burgers and 2 combos?

Most people will buy 1 burger and 1 combo. Their utility from 1 burger and 1 combo is higher than from 2 burgers and 0 combos or 0 burgers and 2 combos. Those two cases show the https://en.wikipedia.org/wiki/Marginal_utility#Law_of_diminishing_marginal_utility

Not me though. I would get 2 burgers. I am greedy

## Advantages

The method of lagrange multipliers takes into account tradeoffs. Should I buy one more burger or one more combo? Should this mole of nitrogen be used to make N2, N2O, NO, or NO2?
It also should be able to be written in a way that does a continuous pass through massive arrays. This leads to better cache performance
Using the "sort species phases by chemical potential and greedily make all of the first option" method requires far more steps per frame (10 or 20) and can lead to the order of species phases jumping around. Oxygen might alternately be consumed to make CO2 or H2O

One disadvantage is that Lagrange functions are hard to understand at first, but that's a moot point now

## Putting It All Together

Frame():
    dH = SolveReactions()
    dH += SolvePhases()
    ApplyHeat(dH)

### Reaction Solver

As stated before, the dissociation assumption remains in Pile Simulator 3

SolveReactions():
    H_before = GetH()

    freeElements = {}
    for speciesPhase in self:
        if T > speciesPhase.Species.DissociationTemperature:
            n_dissociated = speciesPhase.n * DissociationRatioFromTemperature(speciesPhase.Species, T)
            speciesPhase.n -= n_dissociated
            AddToFreeElements(speciesPhase.Species.Formula, n_dissociated)
    
    if freeElements.Count == 0:
        return 0
    
    float[] vec_x = new float[SpeciesPhases.Length]
    float[] vec_lambda = new float[Elements.Length]
    float[] f_coeffs = GetAllChemicalPotentials()
    List<float[]> vec_g_coeffs = PrecalculatedgCoeffs
    List<float[]> vec_g_constants = freeElements

    for solverIterations in range(REACTION_SOLVER_ITERATIONS):
        SolveLagrangianFunction(&f_coeffs, &vec_g_coeffs, &vec_g_constants, &vec_x, &vec_lambda)
        f_coeffs = GetAllChemicalPotentials()
    
    for speciesPhaseIndex in range(SpeciesPhases.Length):
        n_recombined = vec_x[i]
        AddResource(new Resource(SpeciesPhases[speciesPhaseIndex], n_recombined))
    
    H_after = GetH()

    return H_after - H_before

### Phase Solver

SolvePhases():
    H_before = GetH()

    List< List<SpeciesPhase> > groupedBySpecies = GetGroupedBySpecies()

    for speciesPhaseArray in groupedBySpecies:
        SolveOneSpeciesPhases(speciesPhaseArray)
    
    H_after = GetH()

    return H_after - H_before

SolveOneSpeciesPhase(speciesPhaseArray):
    float[] vec_x = new float[speciesPhaseArray.Length]
    float[] vec_lambda = new float[1] // Only one constraint: moles of the species
    float[] f_coeffs = GetChemicalPotentialsOfSpeciesPhases(speciesPhaseArray)
    List<float[]> vec_g_coeffs = new List( new float[speciesPhaseArray.Length] )
    for (i = 0; i < vec_g_coeffs.Length; i++) {
        vec_g_coeffs[i] = 1.0f // Each phase uses one mole of the species to make one mole of the phase
    }
    List<float[]> vec_g_constants = new List( new float[]{1.0f} )

    for solverIterations in range(PHASE_SOLVER_ITERATIONS):
        SolveLagrangianFunction(&f_coeffs, &vec_g_coeffs, &vec_g_constants, &vec_x, &vec_lambda)
        f_coeffs = GetChemicalPotentialsOfSpeciesPhases(speciesPhaseArray)
    
    for speciesPhaseIndex in range(speciesPhaseArray.Length):
        speciesPhaseArray[speciesPhaseIndex].n = vec_x[speciesPhaseIndex]
