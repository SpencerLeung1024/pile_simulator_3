# Architecture: Resource, Volume, and Phase Change

## What fields vs. methods belong where

### Species (unchanged)
```
Species
  - Name, Formula, MolarMass, omega
  - Phases: List<SpeciesPhase>
```
This is a **data-only** class. It describes a chemical substance. It has no methods except maybe a constructor/loader.

### SpeciesPhase (data + getters)
```
SpeciesPhase
  - Species, Phase, Name (polymorph for solids)
  - StandardEnthalpyOfFormation, StandardEntropy
  - StandardTemperature, StandardPressure
  - HeatCapacityFunction  → gives H(T), S(T), c_p(T)
  - EquationOfState       → gives P(T,v), v(T,P)

  Methods (computed from data):
    GetH(T)              → HeatCapacityFunction.GetH(T)
    GetS(T)              → HeatCapacityFunction.GetS(T)
    Getc_p(T)            → HeatCapacityFunction.Getc_p(T)
    GetChemicalPotential(T, P, partialPressure)  → see below
    GetVaporPressure(T)  → see phase change section
    GetMolarVolume(T, P) → EquationOfState.Getv(T, P)
    GetPressure(T, v)    → EquationOfState.GetP(T, v)
    GetInternalEnergy(T, P) → H(T) - P*v(T,P)
```
`SpeciesPhase` is where the thermodynamic **computation** lives. It takes data from `HeatCapacityFunction` and `EquationOfState` and produces the quantities the simulator needs.

### Resource
```
Resource
  - SpeciesPhase          → which phase of which species
  - Amount                → float, mol

  Properties (derived):
    Moles                 → Amount
    Mass                  → Amount * SpeciesPhase.Species.MolarMass
    Volume(float T, float P) → Amount * SpeciesPhase.GetMolarVolume(T, P)
    Enthalpy(float T)     → Amount * SpeciesPhase.GetH(T)
    Entropy(float T)      → Amount * SpeciesPhase.GetS(T)
    InternalEnergy(float T) → Amount * SpeciesPhase.GetInternalEnergy(T)
    ChemicalPotential(T, P, partialPressure) → SpeciesPhase.GetChemicalPotential(T, P, partialPressure)
```

`Resource` knows its amount and can compute total properties by multiplying molar properties by amount. No more, no less. The `TODO` in your code about "where to store temperature" — temperature belongs to the **Volume**, not to individual Resources. The whole box has one T and one P (per your assumption).

### Volume
```
Volume
  - T: float                                  → K, box temperature
  - P: float                                  → Pa, box pressure (or 0 if fixed-volume mode)
  - V: float                                  → m^3, total box volume
  - V_gas: float (computed)                  → V - sum(solid+liquid volumes)
  - Resources: List<Resource>                 → all resources
  - TotalInternalEnergy: float (computed)     → U_tot = sum(n_i * u_i)
  - TotalEnthalpy: float (computed)           → H_tot = sum(n_i * h_i)
  - TotalMoles: float (computed)              → sum of all mole amounts
  - GasMoles: float (computed)                → sum of gas-phase mole amounts

  Methods:
    ComputeVolumes()        → calculates V_gas from condensed phase volumes
    ComputePressure()       → P = n_gas * R * T / V_gas (ideal gas)
    ComputeTotalInternalEnergy() → U_tot
    ComputeTotalEnthalpy()  → H_tot
    ApplyHeat(float dQ)     → adds energy, finds new T
    DoPhaseChangeStep()     → one step of phase adjustment
    Simulate(float dt)      → runs one tick
```

## Control flow: "If I apply heat, how does the control flow?"

This is the central question. Here is the complete flow for one simulation step (one frame/tick):

### Overview (fixed volume box, adiabatic — the BoxSim)

```
INPUT:  Volume with initial {T, V, Resources[], SpeciesDatabase}
OUTPUT: Volume with updated {T, Resources[]} after dt seconds

Step 1: Apply external heat (if any)
  dQ = heat_input_watts * dt  // Joules added via heater, sunlight, etc.
  // This increases U_tot. We need to find the new T that satisfies:
  // U_tot(T_new) = U_tot(T_old) + dQ

Step 2: Phase change (equilibrium at new T)
  For each SpeciesPhase in Resources:
    Determine the stable phase at current T, P
    If stable phase != current phase:
      Compute phase change energies
      Move moles toward the stable phase

Step 3: Chemical reaction (later — NOT in this document)
```

### Step 2 in detail: Phase change

Given a box at temperature T and pressure P (computed from gas moles and V_gas), for each species:

```
For each species (e.g. H2O) present in the box:
  1. Get all its possible phases:
     - Solid  (H2O_ice_Ih)
     - Liquid (H2O_liquid)
     - Gas    (H2O_gas)

  2. For each phase, compute its molar Gibbs free energy at (T, P):
     mu_solid  = G_solid(T)  // pure condensed phase, ≈ H_s(T) - T*S_s(T)
     mu_liquid = G_liquid(T) // pure condensed phase
     mu_gas    = G_gas(T) + RT*ln(p_partial / P_std)  // IDEAL GAS MIXING TERM

     The partial pressure p_partial matters! For a species that hasn't condensed,
     p_partial = n_gas_species * R * T / V_gas

     For a species that's mainly condensed with a small gas equilibrium:
     the gas chemical potential equals the condensed chemical potential.

  3. The phase with lowest mu is the thermodynamically favored phase.

  4. Apply phase change:
     - If liquid has lowest mu and we currently have gas:
       Condense until either all gas is gone OR
       mu_gas rises (due to decreasing p_partial) to equal mu_liquid.

     - If gas has lowest mu and we currently have liquid:
       Evaporate until either all liquid is gone OR
       mu_gas falls (due to increasing p_partial) to equal mu_liquid.

     - If solid has lowest mu:
       Freeze/melt similarly.

  5. Energy accounting during phase change:
     When a mol moves from phase A to phase B:
     delta_U = U_B(T) - U_A(T)
     delta_H = H_B(T) - H_A(T)

     Add delta_U to U_tot (latent heat absorbed or released).
     This changes T for the next iteration.
```

### The actual algorithm (pseudocode)

```csharp
public void Simulate(float dt, float heatInputWatts = 0)
{
    // 1. Add external heat
    float dQ = heatInputWatts * dt;
    float U_target = ComputeCurrentTotalInternalEnergy() + dQ;

    // 2. Find T that satisfies energy balance
    T = FindTemperature(U_target);

    // 3. Compute gas-accessible volume
    V_gas = V;
    float gasMoles = 0;
    foreach (Resource r in Resources)
    {
        if (r.SpeciesPhase.Phase != Phase.Gas)
            V_gas -= r.GetVolume(T); // condensed phases displace volume
        else
            gasMoles += r.Amount;
    }

    // 4. Compute pressure from gas
    P = (gasMoles > 0) ? (gasMoles * Constants.R * T / V_gas) : 0;

    // 5. Phase equilibration (iterate to convergence)
    bool changed;
    do
    {
        changed = false;
        foreach (var species in GetUniqueSpeciesPresent())
        {
            changed |= EquilibratePhases(species);
            // Recompute V_gas, P after each change
            RecomputeV_gasAndP();
        }
    } while (changed);

    // 6. Final energy balance (phase changes changed U_tot)
    U_target = ComputeCurrentTotalInternalEnergy();
    T = FindTemperature(U_target);
}
```

### The phase change logic per species

```csharp
bool EquilibratePhases(Species species)
{
    var phases = species.Phases; // solid, liquid, gas
    var presentPhases = Resources.Where(r => r.SpeciesPhase.Species == species).ToList();

    // Compute chemical potentials for each phase
    float mu_solid = float.MaxValue, mu_liquid = float.MaxValue, mu_gas = float.MaxValue;
    Resource r_solid = null, r_liquid = null, r_gas = null;

    foreach (var r in presentPhases)
    {
        float mu = r.GetChemicalPotential(T, P, partialPressure);
        if (r.SpeciesPhase.Phase == Phase.Solid) { mu_solid = mu; r_solid = r; }
        if (r.SpeciesPhase.Phase == Phase.Liquid) { mu_liquid = mu; r_liquid = r; }
        if (r.SpeciesPhase.Phase == Phase.Gas) { mu_gas = mu; r_gas = r; }
    }

    // Find which phase is thermodynamically preferred
    // (lowest chemical potential)
    float mu_min = MathF.Min(mu_solid, MathF.Min(mu_liquid, mu_gas));

    // Move material toward the lowest-mu phase
    // Fraction of excess moles above equilibrium moves per step
    float fraction = 0.1f; // tuning parameter

    if (mu_min < mu_gas && r_gas != null)
    {
        // Gas should condense/freeze
        // But only if partial pressure exceeds saturation pressure
    }
    // ... etc for each phase pair
}
```

## The smarter approach: Implicit phase choice

Instead of iteratively moving mols between phases, for each species at the current T, P, you can directly determine where the material should be:

### For species where condensed phase exists:

**Step A: Compute saturation vapor pressure**

$$P_\text{sat}(T) = P^\circ \cdot \exp\!\left(\frac{\mu_\text{cond}(T) - \mu_\text{gas}^\circ(T)}{RT}\right)$$

where $\mu_\text{cond}(T)$ is the Gibbs energy of the most stable condensed phase at T.

**Step B: Compare with partial pressure**

```
P_partial = (moles_gas * R * T) / V_gas

if P_partial > P_sat(T):
    // Gas partial pressure exceeds saturation → condense
    // Condense until P_partial = P_sat(T)
    moles_to_condense = moles_gas - (P_sat(T) * V_gas) / (R * T)

else if P_partial < P_sat(T) and condensed_exists:
    // Gas partial pressure below saturation → evaporate
    // Evaporate until P_partial = P_sat(T) or condensed runs out
    moles_to_evaporate = min(condensed_moles,
                             (P_sat(T) * V_gas) / (R * T) - moles_gas)
```

This gives you the equilibrium directly without iteration per species. You still need an outer loop because changing one species' moles changes $V_\text{gas}$, which changes $P_\text{partial}$ for all other species.

### For species where only one phase is stable:

At T > T_c (critical), no liquid exists. Just use solid if T is low enough, gas otherwise. Compare molar Gibbs energies: pick whichever is lower.

## How internal energy determines temperature

After phase changes, the total internal energy $U_\text{tot}$ is known (it was conserved plus any added heat). To find T:

```
function FindTemperature(U_target):
    T_guess = current T
    for i in 1..max_iter:
        U_calc = 0
        for each resource:
            U_calc += resource.Amount * resource.SpeciesPhase.GetInternalEnergy(T_guess)
        error = U_calc - U_target
        if |error| < tolerance: return T_guess

        // dU/dT = sum(n_i * c_p_i) ≈ total heat capacity at constant volume
        // More precisely: dU/dT = sum(n_i * (c_p_i - R)) for gases
        //                         + sum(n_i * c_p_i) for condensed
        float heatCapacity = total_heat_capacity_at_constant_volume(T_guess)
        T_guess -= error / heatCapacity  // Newton step
    throw "Temperature did not converge"
```

## Finding temperature: The subtlety

When using ideal gas + incompressible phases:

- For ideal gas: $U(T) = H(T) - RT$
- For condensed: $U(T) = H(T) - PV \approx H(T)$

So $dU/dT$ for a gas is $C_p - R$, and for condensed phases is $C_p$.

You can compute this on the fly:

```csharp
float GetTotalHeatCapacity_V(float T)
{
    float sum = 0;
    foreach (var resource in Resources)
    {
        if (resource.SpeciesPhase.Phase == Phase.Gas)
            sum += resource.Amount * (resource.SpeciesPhase.Getc_p(T) - Constants.R);
        else
            sum += resource.Amount * resource.SpeciesPhase.Getc_p(T);
    }
    return sum;
}
```

## Summary: What MolarGibbsFreeEnergy (Chemical Potential) looks like

This is the core function you'll call everywhere. It lives on `SpeciesPhase`:

```csharp
public float GetChemicalPotential(float T, float P, float partialPressure)
{
    float G_std = GetH(T) - T * GetS(T); // standard state Gibbs energy

    if (Phase == Phase.Gas)
    {
        // Ideal gas mixing entropy term
        float p_ratio = partialPressure / StandardPressure;
        if (p_ratio <= 0) p_ratio = 1e-30f; // avoid log(0)
        return G_std + Constants.R * T * MathF.Log(p_ratio);
    }
    else
    {
        // Condensed phase (solid or liquid)
        // Pure phase, no mixing term
        // Add small Poynting correction: v * (P - P_std)
        float Poynting = GetMolarVolume(T, P) * (P - StandardPressure);
        return G_std + Poynting;
    }
}
```

For the toy model, the Poynting correction for condensed phases can be dropped (set to 0). It's negligible at low pressures.

## Complete list of methods by class

| Class | Fields | Methods |
|-------|--------|---------|
| **Species** | Name, Formula, MolarMass, omega, Phases | LoadFromDatabase() |
| **SpeciesPhase** | Species, Phase, Name, StandardEnthalpyOfFormation, StandardEntropy, StandardTemperature, StandardPressure, HeatCapacityFunction, EquationOfState | Getc_p(T), GetH(T), GetS(T), GetChemicalPotential(T, P, p_partial), GetMolarVolume(T, P), GetInternalEnergy(T), GetVaporPressure(T) |
| **Resource** | SpeciesPhase, Amount | Volume(T,P), Enthalpy(T), Entropy(T), InternalEnergy(T), ChemicalPotential(T, P, p_partial) |
| **Volume** | T, P, V, Resources | ComputeV_gas(), ComputeP(), ComputeTotalInternalEnergy(), GetTotalHeatCapacity(), FindTemperature(U_target), EquilibratePhases(), Simulate(dt) |
| **HeatCapacityFunction** | (varies by model) | Getc_p(T), GetH(T), GetS(T) |
| **EquationOfState** | (varies by model) | GetP(T, v), Getv(T, P), [for cubic: GetvRoots(T,P), LogFugacityCoefficient(T,v,P), FindSaturationPressure(T)] |
