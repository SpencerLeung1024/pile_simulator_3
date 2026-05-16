# Energy Minimization

## Dissociation

In Pile Simulator 3, every chemical species has a dissociation temperature
At the dissociation temperature, 0.1% of moles will dissociate into the elements every frame
Below the dissociation temperature, moles will not dissociate
This allows diamond (which has a positive enthalpy of formation and would spontaneously turn into graphite) to persist at low temperatures
The dissociation temperature is converted to an activation energy for an Arrhenius equation

https://en.wikipedia.org/wiki/Arrhenius_equation:

k = A * e ^ (-E_a / RT)

This equation tells you how much of the species will dissociate at temperatures higher than the dissociation temperature
A is the limit as T goes to infinity. I've set A = 1 / frame
At the dissociation temperature T_d, k = 0.001

Solving for E_a:

ln(0.001) = -E_a / RT
RTln(0.001) = -E_a

E_a = -ln(0.001) * RT = 6.907 755 * RT

So, if organic molecules begin dissociating and participating in combustion at 600 K (326.85 C), E_a is 34.46 kJ / mol

### Advantages and Disadvantages

It is a limitation of Pile Simulator 3 that molecules either completely dissociate or don't dissociate at all

You can't make bisphenol A in a Volume by putting 2 mol phenol and 1 mol acetone. As soon as dissociation starts happening, the carbon, hydrogen, and oxygen will reform into the lowest free energy species: carbon dioxide, water, methane, and graphite
For organic chemistry and other complex species, you need a Device

On the other hand, dissociation is a lot easier to code than fully simulating functional groups
It also gives an "easy" way to burn random organic molecules for heat and power that doesn't require hardcoding every combustion reaction

## Recombination

Now that the solver has liberated some elements, we need to recombine them

In a sealed box, the variable that is minimized is Helmholtz free energy

- Go through every species and every phase of that species
- - TODO: Use some smart data structures and algorithms to skip impossible species. No need to evaluate uranium hexafluoride if the box has no uranium or fluorine
- Calculate the molar Helmholtz free energy
- Sort the species phases, with lowest molar Helmholtz free energy first
- - TODO: What if you eat too much of one element? N2, N2O, NO, NO2, O2, etc. Real life always finds an equilibrium, but I might need to adjust molar Helmholtz free energy
- Consume elements to reform those species phases, in that order

## Conservation of Energy

- Add up the change in energy from turning dissociated molecules into elements
- Add up the change in energy from turning elements into recombined molecules
- This is the change in heat
- Apply this change in heat to the box
- The species in the box should distribute the heat among themselves to find a new temperature

## Exponential Convergence

So you have a sealed box of 25% N2 and 75% H2
In one step, you dissociated 99% of the species and formed a lot of NH3
But what if this overshoots the equilibrium?

Pile Simulator 3 uses exponentially smaller steps in a frame to find the equilibrium
Opus 4.7 called this "simulated annealing on the Gibbs surface"
- In step 1, A = r^0 / frame. Dissociate and recombine
- In step 2, A = r^1 / frame. Dissociate and recombine
- ...
- In step n, A = r^(n-1) / frame. Dissociate and recombine

Note that this is basically doing the simulation n times per frame. A single Volume might need hundreds of steps per second
Stationeers does a tick every 500 milliseconds across thousands of pipes, and they use exact solutions (ideal gas law, hardcoded combustion, etc.)
Directly finding the equilibrium would be much faster but I'm too dumb to figure that out

There will always be some remaining error compared to the true equilibrium. It is proportional to r^(n-1)
For n = 10, r = 0.5, the error is 0.195 312%
For n = 20, r = 0.5, the error is 0.000 191%
You can't amortize this over multiple frames, because in the next frame, A = 1 / frame and the solver starts with a big change
