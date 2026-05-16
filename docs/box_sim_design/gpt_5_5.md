Yes — your instinct is right: for this kind of toy model, a **database of species/phases plus free-energy minimization or reaction kinetics** is a much better approach than trying to invent one giant EOS that magically covers gas, liquid, ice, graphite, diamond, ammonia, carbon monoxide, etc.

The key correction is:

> Do **not** choose the “lowest internal energy” phase/species. Choose the lowest **free energy** under the current constraints.

For most game-like thermodynamics at specified $T$ and $P$, that means minimizing **Gibbs free energy**:

$$
g = h - Ts
$$

or more generally,

$$
g = u + Pv - Ts
$$

For a fixed-volume box, **Helmholtz free energy** is more natural:

$$
a = u - Ts
$$

But in practice, many toy models use Gibbs-style chemical potentials with ideal gas partial pressures and get good behavior.

---

# Short answer: what data each chemical needs

You want to represent each **phase/form** as its own thermodynamic species:

- `H2O(g)`
- `H2O(l)`
- `H2O(s, ice_Ih)`
- `C(s, graphite)`
- `C(s, diamond)`
- `CH4(g)`
- `CO(g)`
- `CO2(g)`
- `N2O4(g)`
- `NO2(g)`

For each one, you want roughly this data:

| Data | Needed for |
|---|---|
| Formula / element counts | Conservation of atoms |
| Phase/form | Gas, liquid, solid, polymorph |
| Standard enthalpy of formation $\Delta_f H^\circ$ | Energy release/absorption |
| Standard entropy $S^\circ$ or Gibbs formation energy $\Delta_f G^\circ$ | Equilibrium direction |
| Heat capacity $c_p(T)$ or polynomial coefficients | Temperature changes |
| Molar volume or density | Pressure/volume effects |
| Equation of state info | Gas pressure, supercritical behavior, condensed volume |
| Valid temperature range | Avoid nonsense extrapolation |
| Optional kinetic data | Activation barriers, metastability, ignition, catalysts |

The absolutely central quantity is the standard chemical potential:

$$
\mu_i^\circ(T) = h_i^\circ(T) - T s_i^\circ(T)
$$

Once you can compute $\mu_i^\circ(T)$ and $h_i^\circ(T)$ for each species/form, you can do a lot.

---

# Important: enthalpy alone is not enough

Your concern here is correct:

> “I don't think any chemicals with positive enthalpies of formation will end up existing because elements always have a pure form with zero enthalpy of formation.”

That would indeed happen if you greedily minimized **enthalpy**.

But nature does not minimize enthalpy alone. At fixed $T$ and $P$, it minimizes Gibbs free energy:

$$
G = H - TS
$$

So a species with positive formation enthalpy can still exist because:

1. It has entropy.
2. Gas mixing entropy matters.
3. High temperature favors dissociation.
4. Low concentration changes chemical potential.
5. Reactions reach partial equilibrium, not always completion.

For a gas species,

$$
\mu_i(T,p_i) = \mu_i^\circ(T) + RT \ln \left(\frac{p_i}{P^\circ}\right)
$$

where $p_i$ is the partial pressure. That logarithm term is crucial. It is why equilibrium mixtures contain small amounts of “unfavorable” species instead of deleting them completely.

For example, even if atomic oxygen has high positive formation enthalpy, at high temperature some amount of it appears because the entropy gain and equilibrium condition allow it.

---

# Recommended mental model

Instead of saying:

> “Liberate atoms, then form the most negative-enthalpy compounds first.”

say:

> “Given atoms, temperature, volume/pressure, and available species, find the composition that minimizes free energy, subject to element conservation.”

The equilibrium problem is:

$$
\text{minimize } G = \sum_i n_i \mu_i
$$

subject to atom conservation:

$$
\sum_i n_i a_{ij} = N_j
$$

and

$$
n_i \ge 0
$$

where:

- $n_i$ is the amount of species $i$,
- $a_{ij}$ is the number of atoms of element $j$ in species $i$,
- $N_j$ is the total amount of element $j$ in the box.

For an ideal gas mixture,

$$
\mu_i = \mu_i^\circ(T) + RT \ln \left(\frac{y_i P}{P^\circ}\right)
$$

where $y_i$ is mole fraction.

For a pure condensed phase, approximately,

$$
\mu_i \approx \mu_i^\circ(T)
$$

or, with pressure correction,

$$
\mu_i(T,P) \approx \mu_i^\circ(T) + v_i(P-P^\circ)
$$

For a toy model, the pressure correction for solids/liquids can often be ignored unless you want high-pressure ice phases, diamond formation, etc.

---

# Species data structure

Something like this is a good starting point:

```yaml
species:
  - id: "CO2_g"
    name: "carbon dioxide gas"
    formula:
      C: 1
      O: 2
    phase: "gas"
    molar_mass: 0.0440095
    thermo:
      model: "NASA7"
      T_min: 200
      T_max: 6000
      coeffs_low: [...]
      coeffs_high: [...]
    eos:
      model: "ideal_gas"

  - id: "C_graphite"
    name: "graphite"
    formula:
      C: 1
    phase: "solid"
    polymorph: "graphite"
    molar_mass: 0.012011
    thermo:
      model: "Shomate"
      T_min: 298
      T_max: 4000
      coeffs: [...]
    condensed:
      density: 2260
      molar_volume: 5.31e-6

  - id: "C_diamond"
    name: "diamond"
    formula:
      C: 1
    phase: "solid"
    polymorph: "diamond"
    molar_mass: 0.012011
    thermo:
      model: "Shomate"
      T_min: 298
      T_max: 4000
      coeffs: [...]
    condensed:
      density: 3515
      molar_volume: 3.42e-6
    kinetics:
      transform_to_graphite:
        activation_energy: very_large
```

For water:

```yaml
species:
  - id: "H2O_g"
    formula: {H: 2, O: 1}
    phase: "gas"
    thermo: ...
    eos:
      model: "ideal_gas"

  - id: "H2O_l"
    formula: {H: 2, O: 1}
    phase: "liquid"
    thermo: ...
    condensed:
      density: 997
      molar_volume: 1.806e-5

  - id: "H2O_ice_Ih"
    formula: {H: 2, O: 1}
    phase: "solid"
    polymorph: "ice_Ih"
    thermo: ...
    condensed:
      density: 917
      molar_volume: 1.964e-5
```

Notice that ice is less dense than liquid water. You do not need to model the crystal lattice explicitly. You just encode its thermodynamic properties.

---

# Minimum thermodynamic functions each species/form needs

At minimum, each species/form should be able to answer:

```text
h(T)      molar enthalpy
s(T)      molar entropy
g(T,P)    molar Gibbs free energy / chemical potential
u(T)      molar internal energy, if using constant-volume energy
v(T,P)    molar volume
cp(T)     heat capacity
```

The most convenient storage format is often not direct tables, but coefficients.

For example, with NASA polynomial data, you can compute:

$$
c_p^\circ(T)
$$

$$
h^\circ(T)
$$

$$
s^\circ(T)
$$

then:

$$
g^\circ(T) = h^\circ(T) - T s^\circ(T)
$$

That gives you equilibrium and heat release.

---

# Phase choice

For a pure substance at given $T$ and $P$, the stable phase is the one with lowest molar Gibbs free energy:

$$
g_\alpha(T,P)
$$

For water, compare:

$$
g_{\mathrm{ice}}(T,P)
$$

$$
g_{\mathrm{liquid}}(T,P)
$$

$$
g_{\mathrm{gas}}(T,P)
$$

Whichever is lowest is stable.

At a phase transition, two are equal:

$$
g_{\mathrm{ice}} = g_{\mathrm{liquid}}
$$

or

$$
g_{\mathrm{liquid}} = g_{\mathrm{gas}}
$$

or

$$
g_{\mathrm{ice}} = g_{\mathrm{gas}}
$$

Then latent heats are just enthalpy differences:

$$
\Delta h_{\mathrm{fus}} = h_l - h_s
$$

$$
\Delta h_{\mathrm{vap}} = h_g - h_l
$$

$$
\Delta h_{\mathrm{sub}} = h_g - h_s
$$

So yes: if your species/form database contains $h(T)$ and $g(T,P)$, you automatically get fusion, vaporization, sublimation, polymorph changes, etc.

---

# Condensation in an ideal gas mixture

If you assume gases are ideal and condensed phases are immiscible, you can implement condensation very cleanly.

For gas species $i$:

$$
p_i = y_i P
$$

or in a fixed-volume cell:

$$
p_i = \frac{n_iRT}{V_g}
$$

where $V_g$ is gas-accessible volume.

For a pure condensed phase, equilibrium with its gas occurs when:

$$
\mu_i^{g}(T,p_i) = \mu_i^{c}(T)
$$

Using the ideal gas expression:

$$
\mu_i^{g}(T,p_i)
=
\mu_i^{g,\circ}(T)
+
RT \ln \left(\frac{p_i}{P^\circ}\right)
$$

Therefore the saturation vapor pressure is:

$$
p_{\mathrm{sat}}(T)
=
P^\circ
\exp\left(
\frac{\mu_i^c(T)-\mu_i^{g,\circ}(T)}{RT}
\right)
$$

Then your rule can be:

- if $p_i < p_{\mathrm{sat}}(T)$, all of species $i$ can remain gas;
- if $p_i > p_{\mathrm{sat}}(T)$, condense until $p_i = p_{\mathrm{sat}}(T)$;
- if $T$ is above the critical temperature, disable ordinary liquid condensation.

This gives you water vapor, liquid water, ice, sublimation, etc., without needing a complicated EOS.

---

# Chemical equilibrium

For a reaction,

$$
\sum_i \nu_i A_i = 0
$$

the standard reaction Gibbs energy is:

$$
\Delta_r G^\circ(T) = \sum_i \nu_i \mu_i^\circ(T)
$$

The equilibrium constant is:

$$
K(T) = \exp\left(-\frac{\Delta_r G^\circ(T)}{RT}\right)
$$

At equilibrium,

$$
\sum_i \nu_i \mu_i = 0
$$

For ideal gases, this becomes the familiar mass-action equation.

This is what gives you behavior like:

$$
\mathrm{N_2O_4 \rightleftharpoons 2NO_2}
$$

High pressure favors $\mathrm{N_2O_4}$ because it has fewer gas molecules. High temperature favors $\mathrm{NO_2}$ if dissociation is entropically/thermally favored.

Similarly:

$$
\mathrm{N_2 + 3H_2 \rightleftharpoons 2NH_3}
$$

High pressure favors ammonia because the reaction reduces gas mole count from $4$ to $2$.

---

# Activation energy and metastability

This is separate from thermodynamics.

Thermodynamics says whether a change is favorable:

$$
\Delta G < 0
$$

Kinetics says whether it actually happens on your timescale.

Diamond turning into graphite is thermodynamically favorable at standard conditions, but kinetically blocked. Methane formation from graphite and hydrogen is also favorable under some conditions, but does not rapidly happen without a reaction pathway/catalyst.

So you need reaction kinetics data separately.

For each reaction, store something like:

```yaml
reactions:
  - id: "CO_oxidation"
    equation:
      reactants:
        CO_g: 2
        O2_g: 1
      products:
        CO2_g: 2
    kinetics:
      model: "arrhenius"
      A: ...
      b: ...
      Ea: ...
    reversible: true
```

A common Arrhenius form is:

$$
k(T) = A T^b \exp\left(-\frac{E_a}{RT}\right)
$$

For reversibility, ideally store the forward rate and compute the reverse rate from equilibrium:

$$
K(T) = \frac{k_f(T)}{k_r(T)}
$$

so:

$$
k_r(T) = \frac{k_f(T)}{K(T)}
$$

This preserves thermodynamic consistency.

For a toy model, you can simplify activation barriers into categories:

| Barrier type | Behavior |
|---|---|
| None | Equilibrates immediately |
| Low | Happens over seconds/ticks |
| Medium | Needs high temperature |
| High | Needs ignition/catalyst |
| Huge | Effectively metastable forever |

Diamond-to-graphite can have a huge barrier. Combustion can require ignition. Ammonia synthesis can require a catalyst.

---

# Do not greedily form compounds by enthalpy

This algorithm:

```text
1. Free atoms.
2. Sort compounds by most negative enthalpy of formation.
3. Form them greedily.
```

will give bad results.

It will overproduce the deepest enthalpy sink and miss equilibrium mixtures. It will also mishandle pressure effects, entropy, dissociation, radicals, and incomplete combustion.

Instead, either:

## Option A: equilibrium minimization

At each tick, find the composition that minimizes free energy.

This gives realistic equilibrium but ignores activation barriers unless you impose them.

Good for:

- hot combustion products,
- dissociation,
- gas equilibria,
- phase equilibria,
- final settled state.

Bad for:

- metastable fuels,
- explosives,
- diamond,
- delayed ignition,
- uncatalyzed ammonia synthesis.

## Option B: reaction network with kinetics

Store explicit reactions and rates.

This gives better game behavior.

Good for:

- ignition,
- incomplete combustion,
- catalysts,
- slow oxidation,
- diamond not instantly becoming graphite.

Bad for:

- needs more data,
- can be numerically stiff,
- harder to author.

## Option C: hybrid

This is probably best.

Use equilibrium for “fast groups” and kinetics for slow transformations.

For example:

- Phase changes: fast, near-equilibrium.
- Acid/base or simple gas association: fast.
- Combustion: kinetic, requires ignition.
- Diamond to graphite: kinetic, enormous barrier.
- $\mathrm{N_2 + H_2 \leftrightarrow NH_3}$: kinetic, slow without catalyst.
- High-temperature dissociation: equilibrium or fast kinetic.

---

# Energy accounting

If your box is adiabatic and fixed-volume, conserve internal energy:

$$
U = \sum_i n_i u_i(T)
$$

If your box is constant-pressure, conserve enthalpy for reaction heat:

$$
H = \sum_i n_i h_i(T)
$$

For a game cell, a practical loop is:

```text
Given:
  element amounts
  total volume
  total internal energy

Guess T.

At this T:
  1. Find equilibrium or integrate reactions.
  2. Determine gas/condensed split.
  3. Compute pressure from gas moles and gas volume.
  4. Compute total internal energy U_calc.

Adjust T until:
  U_calc = U_target.
```

For an ideal gas species:

$$
u_i(T) = h_i(T) - RT
$$

For condensed phases, approximately:

$$
u_i(T) \approx h_i(T) - P^\circ v_i
$$

Usually $P^\circ v_i$ is tiny compared with chemical energies, so for a toy model:

$$
u_i(T) \approx h_i(T)
$$

for liquids and solids.

---

# Pressure in your immiscible toy model

If condensed phases are immiscible and gases are ideal, pressure can be:

$$
P = \frac{n_gRT}{V_g}
$$

where:

$$
V_g = V_{\mathrm{box}} - \sum_{\mathrm{condensed}} n_i v_i
$$

and $n_g$ is total gas moles.

Each gas partial pressure is:

$$
p_i = \frac{n_iRT}{V_g}
$$

This is enough to get many useful effects:

- compression raises gas pressure,
- higher pressure favors fewer gas molecules,
- condensation removes gas moles,
- liquids/solids occupy volume,
- boiling depends on vapor pressure,
- sublimation depends on vapor pressure,
- high pressure can favor denser solid phases if you include $v_i(P-P^\circ)$.

---

# Handling solids and allotropes

Represent each solid form as a separate species with the same formula but different thermodynamic data.

Example:

```yaml
- id: "C_graphite"
  formula: {C: 1}
  phase: "solid"
  polymorph: "graphite"
  delta_f_H_298: 0
  delta_f_G_298: 0
  density: 2260

- id: "C_diamond"
  formula: {C: 1}
  phase: "solid"
  polymorph: "diamond"
  delta_f_H_298: 1900   # J/mol-ish above graphite
  delta_f_G_298: 2900   # J/mol-ish above graphite
  density: 3515
  kinetic_barrier_to_graphite: huge
```

Then the thermodynamic model will prefer graphite at ordinary pressure, but the kinetic layer prevents diamond from disappearing instantly.

For water ice polymorphs, do the same:

```yaml
- id: "H2O_ice_Ih"
  formula: {H: 2, O: 1}
  phase: "solid"
  density: 917
  thermo: ...

- id: "H2O_ice_III"
  formula: {H: 2, O: 1}
  phase: "solid"
  density: ...
  thermo: ...

- id: "H2O_ice_VII"
  formula: {H: 2, O: 1}
  phase: "solid"
  density: ...
  thermo: ...
```

At high pressure, the $Pv$ term makes denser phases more favorable.

---

# Supercritical behavior

If you keep gases ideal and condensed phases separate, “supercritical” will be approximate.

For a toy model, you can do:

```text
If T > Tc:
  disable liquid phase.
  species can only be gas/supercritical fluid.
```

Store per condensable chemical:

| Data | Meaning |
|---|---|
| $T_c$ | Critical temperature |
| $P_c$ | Critical pressure |
| $\omega$ | Acentric factor, optional |
| $T_b$ | Normal boiling point |
| $\Delta h_{\mathrm{vap}}$ | Vaporization enthalpy |
| Antoine/Wagner coefficients | Vapor pressure curve |

If you want better fluid behavior, use Peng-Robinson or Soave-Redlich-Kwong for gas/liquid fugacity. But for a game, ideal gas plus saturation pressure curves is much cheaper.

---

# Good practical minimal database

For each species/form:

```yaml
id: string
formula: element-count map
phase: gas/liquid/solid
polymorph: optional string
molar_mass: kg/mol

thermo:
  reference_pressure: 100000
  T_min: number
  T_max: number
  Hf_298: J/mol
  S_298: J/mol/K
  Cp_model: constant | polynomial | NASA7 | Shomate
  Cp_coeffs: [...]

volume:
  model: ideal_gas | incompressible | density_polynomial
  density: kg/m3
  molar_volume: m3/mol
  thermal_expansion: optional
  compressibility: optional

phase_limits:
  critical_temperature: optional
  critical_pressure: optional
  vapor_pressure_model: optional
  vapor_pressure_coeffs: optional

kinetics:
  metastable: optional bool
  transformation_barriers: optional
```

For each reaction:

```yaml
id: string
stoichiometry:
  species_id: coefficient   # negative reactants, positive products
kinetics:
  model: equilibrium | arrhenius | disabled | instant_above_temperature
  A: optional
  b: optional
  Ea: optional
  catalyst: optional
  ignition_temperature: optional
```

---

# Where to get data

Useful sources/formats:

- **NASA thermodynamic polynomials** for gas species.
- **JANAF tables** for $h(T)$, $s(T)$, $g(T)$.
- **NIST Chemistry WebBook** for $\Delta_f H^\circ$, $S^\circ$, heat capacities, phase-change data.
- **Burcat database** for combustion species.
- **GRI-Mech** for methane combustion kinetics.
- **Cantera** input files as examples of species + reactions + NASA polynomials.
- **NASA CEA** as a model for equilibrium composition calculation.

If you want inspiration, look at how Cantera represents species and reactions. Your toy model can be much simpler, but the structure is very similar.

---

# What your listed behaviors require

| Desired behavior | Data/species needed |
|---|---|
| Ice/water/steam | `H2O_s`, `H2O_l`, `H2O_g`, phase thermodynamics, vapor pressure |
| Supercritical steam | $T_c$, $P_c$, either simple cutoff or real-fluid EOS |
| Water dissociation | `H2`, `O2`, `H`, `O`, `OH`, maybe `HO2`, `H2O2` |
| $\mathrm{N_2/H_2/NH_3}$ equilibrium | `N2`, `H2`, `NH3`, Gibbs data, pressure dependence, kinetics/catalyst |
| $\mathrm{N_2O_4/NO_2}$ equilibrium | `N2O4`, `NO2`, Gibbs data |
| CO vs CO2 incomplete combustion | `C`, `O2`, `CO`, `CO2`, kinetics or equilibrium |
| Methane combustion | `CH4`, `O2`, `CO2`, `CO`, `H2O`, `H2`, `OH`, `O`, `H` |
| Diamond metastability | `C_graphite`, `C_diamond`, Gibbs data, large kinetic barrier |

---

# My recommended architecture

Use this three-layer model:

## 1. Thermodynamic species layer

Every phase/form is a species with:

$$
h_i(T),\quad s_i(T),\quad \mu_i^\circ(T),\quad v_i(T,P)
$$

## 2. Equilibrium layer

For fast processes, minimize free energy subject to element conservation.

Use chemical potentials:

$$
\mu_i = \mu_i^\circ(T) + RT \ln\left(\frac{p_i}{P^\circ}\right)
$$

for gases, and pure-phase $\mu_i^\circ(T)$ for condensed phases.

## 3. Kinetic layer

For slow or blocked processes, use reactions with Arrhenius rates:

$$
k(T)=AT^b\exp\left(-\frac{E_a}{RT}\right)
$$

This prevents diamond from instantly becoming graphite and prevents cold hydrogen plus carbon from instantly becoming methane.

---

# Bottom line

For each chemical, do not store just “internal energy and volume.” Store a small thermodynamic model for each **phase/form**:

$$
h(T),\quad s(T),\quad g(T,P),\quad c_p(T),\quad v(T,P)
$$

Then use free energy, not enthalpy, to decide equilibrium.

For realism with metastability and ignition, add a separate reaction-kinetics layer with activation energies.

That combination — **species free energies + ideal gas mixing + immiscible condensed phases + optional reaction kinetics** — is probably the sweet spot between “real thermodynamics” and something fast enough to run in a game loop.
