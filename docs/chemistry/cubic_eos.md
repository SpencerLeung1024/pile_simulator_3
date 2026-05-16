# Cubic Equations of State

The specific EOS and their forms are taken from https://courses.ems.psu.edu/png520/node/1405
PNG 520: Phase Behavior of Natural Gas and Condensate Fluids, Department of Energy and Mineral Engineering, College of Earth and Mineral Sciences, PennState
Some forms also taken from https://en.wikipedia.org/wiki/Cubic_equations_of_state

## The van der Waals Equation

One day in 1873, some guy named Johannes Diderik van der Waals stood up in class and declared "maybe molecules do interact and occupy volume"
For his incredible insight, he is immortalized in chemistry textbooks with his van der Waals Equation:

P = ((RT) / (v-b)) - (a / v^2)

v = V/n, the molar volume
a and b, substance-specific constants representing attraction and displaced volume

Rearranging to solve for v:

v^3 - ((RT + Pb) / P) v^2 + (a / P) v - (ab / P) = 0

This is a cubic equation and may give up to three solutions

The great thing about the vdW equation, and other cubic EOS, is that they can represent liquids and supercritical fluids too
At low temperatures, three solutions (roots) for v are found for a given saturation pressure
- small v: liquid state
- middle v: fake state with a positive slope, needed because the PV diagram needs to have a negative slope in the liquid and gas regions
- big v: gas state
As temperature increases and a new saturation pressure is established, the small v and big v move closer to each other, until they become identical at the critical temperature and pressure
Beyond the critical temperature, no amount of pressure will make it condense into a liquid

### The Principle of Corresponding States

How do you get a and b?
You can do a bunch of experiments on a substance and fit a and b to the data
But the vdW equation and the fact that it has a critical point means that a and b are linked to the critical temperature (T_c) and pressure (P_c)

a = (27/64) * (R^2 * T_c^2) / P_c
b = (1/8) * (R * T_c) / P_c

Temperature and pressure are somewhat easier to observe

Another way to express this is in reduced variables (T_r, P_r, v_r) (ratios based on the critical temperature (T / T_c), pressure, and volume).
From https://en.wikipedia.org/wiki/Van_der_Waals_equation:

P_r = (8T_r / (3v_r-1)) - (3 / v_r^2)

Rearranging to solve for v_r:

v_r^3 - ((8T_r + P_r) / 3P_r) v_r^2 + (3 / P_r) v_r - (1 / P_r) = 0

Mathematically, every substance's vdW equation can be transformed into any other substance's equation by scaling T, P, and v
Or, if you know T_c, P_c, and v_c of a substance, you can calculate T_r, P_r, and v_r, then use them in the same, dimensionless vdW function

This is the principle of corresponding states

Real substances don't quite correspond like that, but here in Pile Simulator 3 the model is reality

The vdW equation is a big improvement, but still not enough to stop pipes and tanks from exploding. A lot of better EOS have been invented over the past century

## Redlich-Kwong EOS

Redlich and Kwong (1949) proposed this:

(P + (a / (T^0.5 * v * (v + b))))(v - b) = RT

The attraction now depends on temperature, and displaced volume too

a = 0.427 48 * (R^2 * T_c^2.5) / P_c
b = 0.086 64 * (R * T_c) / P_c

Rearranging to solve for P:

P = (RT / (v - b)) - (a / (T^0.5 * v * (v + b)))

Rearranging to solve for v:

v^3 - (RT / P) v^2 + ((1 / P) * ((a / T^0.5) - bRT - Pb^2)) v - (ab / PT^0.5) = 0

## Soave-Redlich-Kwong EOS

Soave (1972) proposed this:

P = (RT / (v - b)) - ((alpha * a) / (v * (v + b)))
a = 0.427 48 * (R^2 * T_c^2) / P_c
b = 0.086 64 * (R * T_c) / P_c

New variables:

alpha = (1 + (0.485 08 + 1.551 71 * omega - 0.156 13 * omega^2)(1 - (T / T_c)^0.5))^2

Note how temperature dependence was changed and folded into the alpha term

omega is the acentric factor, a species-specific number

Rearranging to solve for v is left as an exercise to the reader

### The Acentric Factor

Pitzer (1955) defined the acentric factor as:

omega = -log10(P_sat / P_c) - 1 at T = 0.7 * T_c

A positive omega means the saturation pressure at 70% of the critical temperature is lower than expected (less than 10% of the critical pressure)
The bigger omega is, the less spherical the molecule is. At least, that's the theory
From https://en.wikipedia.org/wiki/Acentric_factor:
Neon, argon, krypton, and xenon have omega = 0.000
Helium is a weird one. It has omega = -0.390
Hydrogen has omega = -0.220
All other molecules have positive omega

## Peng-Robinson EOS

Peng and Robinson (1976) proposed this:

P = (RT / (v - b)) - ((alpha * a) / (v^2 + 2bv -b^2))
a = 0.457 24 * (R^2 * T_c^2) / P_c
b = 0.077 80 * (R * T_c) / P_c

alpha = (1 + (0.374 64 + 1.542 26 * omega - 0.269 92 * omega^2)(1 - (T / T_c)^0.5))^2

Note the denominator v^2 + 2bv -b^2 has + bv and - b^2 compared to SRK
Also the omega coefficients are different

Rearranging to solve for v is left as an exercise to the reader
