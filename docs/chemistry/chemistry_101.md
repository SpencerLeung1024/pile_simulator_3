# How Chemistry works in Pile Simulator 3

I was playing Stationeers after the Gases Update and burning methane didn't produce any water
This made me so tilted I recalled my Chem 12 knowledge and decided to make my own combustion simulator

## Phases

There are three states of matter: solid, liquid, and gas
Solids have regular structure and strong intermolecular forces
Liquids still have strong intermolecular forces, but no regular structure
Gases have weak intermolecular forces, allowing them to take up the volume of their container. In the limit of the ideal gas law, they do not interact and occupy zero volume

## The Ideal Gas Law

The bane of chemistry students, the ideal gas law:

PV = nRT

P = pressure, in Pascals
V = volume, in cubic meters
n = amount of substance, in moles
R = gas constant, 8.314 462 618 153 24 Joules per Kelvin per mole
T = temperature, in Kelvin

Let v = V/n, the molar volume. This will be easier to compare to other EOS below

v = RT / P
v - (RT / P) = 0

This is a linear equation and will always give one solution

The ideal gas law works well for gases at high temperature and low pressure

Stationeers uses the ideal gas law for gases, with a separate check for condensation to and evaporation from a liquid phase

## Cubic Equations of State

Wouldn't it be nice to represent liquids and gases?
The cubic equations of state, starting with the van der Waals equation, can do that
They also produce cool behaviors, like liquids gradually expanding until merging with the gas phase at the critical point into a supercritical fluid
See `docs/chemistry/cubic_eos.md` for more info

## Solids

Wouldn't it be nice to represent solids, liquids, and gases?
Unfortunately, there is no easy to work with quintic equation of state
Solids have long range structure. The ordering of molecules from a jumbled liquid to a nice solid results in an enthalpy change that never becomes continuous between the two phases, unlike the critical point between liquid and gas

## The Birds and the E's

Energy cannot be created or destroyed. It can only take different forms

U = internal energy: An accounting trick for whatever energy the substance released when it became its current state. Condensation of liquids, formation of crystal structure, etc. all release energy. Models may provide an internal energy but in real life this is very hard to determine. It doesn't matter what the internal energy of something is defined as, as long as all other substances have their internal energies moved up or down accordingly
H = enthalpy = U + PV: Includes the pressure-volume term, the energy needed to expand against an external environment at pressure P to make volume V for the substance. Note that even a sealed box floating in space has a pressure-volume term if there is gas inside, even though there is no external pressure. Changes in enthalpy require the initial and final pressure to be the same. In everyday life, changes in heat (burning wood in a fireplace, etc.) are changes in enthalpy, because the atmosphere provides constant pressure
S = entropy: A concept that can be explained like 10 different ways, none of which make sense. All you need to know is that processes that increase entropy are favored, processes that decrease entropy are disfavored, and entropy has units of Joules per Kelvin
G = Gibbs free energy = H - TS: At constant temperature and pressure, at equilibrium, Gibbs free energy is minimized in a system. If you leave substances out in a temperature controlled, atmospheric pressure lab, take a walk, and come back, they will be in the forms that minimize Gibbs free energy
A = Helmholtz free energy = U - TS: At constant temperature and volume, at equilibrium, Helmholtz free energy is minimized in a system. Note the lack of the pressure-volume term. If you repeat the above but in a sealed box, the substances will be in the forms that minimize Helmholtz free energy

Both G and A are examples of chemical potential, μ. Chemical potential is what is minimized in systems. The specific function corresponding to μ depends on whether the box can expand into surrounding pressure or is constant volume, whether it can exchange heat, whether it can exchange chemical species and what kinds, etc.

## Enthalpies

Processes that release energy, that are exothermic, have negative enthalpy. Burning carbon to make carbon dioxide releases energy, so the enthalpy of reaction is negative
Processes that absorb energy, that are endothermic, have positive enthalpy. Plants creating glucose from carbon dioxide and water require energy from the sun, so the enthalpy of reaction is positive
Every substance has an enthalpy of formation: energy that was released or required to produce the substance at 293.15 K and 100 kPa
Elements at 293.15 K and 100 kPa are defined to have zero enthalpy of formation
Graphite (carbon) and diatomic oxygen (oxygen) have zero enthalpy of formation
Carbon dioxide has negative enthalpy of formation, because (products - reactants) < 0, and reactants = 0, so products must be < 0
Note however that glucose still has negative enthalpy of formation, even though (products - reactants) > 0, because it was formed from carbon dioxide and water, both of which have their own negative enthalpies of formation, and the input energy was not enough to fully pay back that negative enthalpy. If you somehow had a catalyst that produced glucose directly from graphite, diatomic hydrogen, and diatomic oxygen, you would find that the catalyzed reaction is exothermic

From https://en.wikipedia.org/wiki/Standard_enthalpy_of_formation:
- Graphite, diatomic hydrogen, diatomic oxygen: 0 kJ / mol (by definition)
- Carbon dioxide: -393.509 kJ / mol
- Water (gas): -241.818 kJ / mol
- Water (liquid): -285.8 kJ / mol (note the lower enthalpy; water releases 43.982 kJ of latent heat per mole when it condenses)
- Glucose: -1 271 kJ / mol

6 CO2 + 6 H2O + energy -> 1 C6H12O6 + 6 O2
Solve for energy needed to run 1 mol of the above reaction:
energy = 1 (-1 271 kJ) + 6 (0 kJ) - 6 (-393.509 kJ) - 6 (-285.8 kJ) (using liquid water)
energy = 2 804.854 kJ
Starting from carbon dioxide and liquid water, a plant needs 2 804.854 kJ to make 1 mol of glucose

Phase change is also associated with enthalpy changes.
Solid -> liquid: enthalpy of fusion
Liquid -> gas: enthalpy of vaporization
Solid -> gas: enthalpy of sublimation

When using a cubic EOS, enthalpy of vaporization decreases as temperature increases, because the volume change is smaller, because liquids take up more volume
At the critical point, the volume change between liquid and gas (and the distinction between them) vanishes, so the enthalpy of vaporization is zero

In general, if you know the internal energy U and volume V the substance takes up, and the box pressure P, you can calculate the enthalpy of the substance
H = U + PV
The enthalpy of phase change is the change in enthalpy between the initial and final phases
ΔH = H_f - H_i
ΔH = (U_f + P * V_f) - (U_i + P * V_i)
ΔH = (U_f - U_i) + P * (V_f - V_i)
ΔH = ΔU + PΔV

## Energy Minimization

Energy minimization (Gibbs or Helmholtz) allows you to determine:
- Enthalpy changes during phase change (difference in enthalpies of the two phases)
- Equilibrium chemical species after chemical reactions
The natural world seems to run on energy, much as the human world seems to run on money. In Econ you should Follow The Money. In Chem you should Follow The Energy

See `docs/chemistry/energy_minimization.md` for how Pile Simulator 3's energy minimization works, and how it differs from real life

## Assumptions

- Condensed phases (solids, liquids) do not mix
- The entire box and all species in it has the same temperature and pressure
- Gases do not dissolve in liquids
- For cubic equations of state, the gas-accessible volume used to calculate their V has the volume of solids, liquids, and other gas species subtracted out
- Gases do not interact and each gas species appears to be the sole inhabitant of the gas-accessible volume, apart from a previous hidden subtraction
