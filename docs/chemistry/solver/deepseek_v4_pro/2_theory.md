# Thermodynamic Theory for Pile Simulator 3

This document explains how cubic equations of state connect to internal energy, heat capacity, and phase equilibrium. The code references are to `DSA/Chemistry/EquationsOfState.cs` and `DSA/Chemistry/Volume.cs`.

---

## 1. Fugacity and U_departure Come from the Same Equation of State

Every cubic EOS (van der Waals, Redlich-Kwong, Soave-Redlich-Kwong, Peng-Robinson) takes the form:

```
P = (R T) / (v - b) - a / (v² + u b v + w b²)
```

where `a` and `b` depend on the critical constants and (for SRK and PR) the acentric factor. The constants `u` and `w` distinguish the equations.

### The EOS gives us everything

Given `T` and `v`, the EOS gives us `P` directly. But it also gives us — through thermodynamic integrals — every other property:

**Fugacity coefficient** `φ` (used in `GetLogphi`):
```
ln φ = ∫₀^P (Z - 1) dP / P   =   Z - 1 - ln(Z - B) - (g(Z, A, B))
```
where `Z = P v / (R T)` is the compressibility factor and `A, B` are the reduced EOS parameters. The term `g(Z, A, B)` differs by EOS:
- vdW: `A / Z`
- RK/SRK: `(A / B) · ln((Z + B) / Z)`
- PR: `A / (2√2 B) · ln((Z + (1+√2)B) / (Z + (1-√2)B))`

**Departure internal energy** `U_dep` (used in `GetUDeparture`):
```
U_dep(T, v) = ∫_v^∞ [T · (∂P/∂T)_v - P] dv
```
This integral has a closed form for each cubic EOS because the integrand simplifies:
```
T · (∂P/∂T)_v - P = (a - T · da/dT) / (v² + u b v + w b²)
```
- vdW: `U_dep = -a / v` (no T dependence since `da/dT = 0`)
- RK: `U_dep = -(3a) / (2√T · b) · ln((v + b) / v)` (a ∝ T_c^2.5, constant)
- SRK: `U_dep = -(a / b) · d(α T) / dT · ln((v + b) / v)` (a ∝ α(T) · T_c²)
- PR: `U_dep = -(a / (2√2 b)) · d(α T) / dT · ln((v + (1+√2)b) / (v + (1-√2)b))`

### Why they're connected

Both `ln φ` and `U_dep` are thermodynamic integrals of the same `P(T, v)` function. They're not the same thing — one is a pressure integral and the other is a volume integral — but they share the same departure from ideal behavior. This means:

1. **If you find equilibrium using non-ideal fugacities** (via `Getmu` which contains `GetLogphi`), the equilibrium composition reflects real-gas behavior.

2. **If you then compute internal energy using only ideal-gas formulas** `U = H(T) - RT`, you're computing the energy of a different system. The composition you found is the equilibrium of a real gas, but your energy calculation assumes an ideal gas. Energy won't balance.

3. **The fix**: always compute `U(T, v) = U_ideal(T) + U_dep(T, v)` where `U_ideal(T) = H(T) - RT`. The departure must match the same EOS used for fugacity.

In `CubicEquationOfState.GetU` (`EquationsOfState.cs:166`):
```csharp
public override double GetU(double T, double v)
{
    return GetUIdeal(T, v) + GetUDeparture(T, v);
}
```

And `GetUIdeal` is properly `H(T) - RT` (not `(c_p - R) · T`):
```csharp
public double GetUIdeal(double T, double v)
{
    return SpeciesPhase.HeatCapacityFunction.GetH(T) - Constants.R * T;
}
```

This works because `H(T) = H_f° + ∫_0^T c_p(T') dT'` — the integral accounts for variable `c_p(T)` and the formation enthalpy is embedded in the NASA9 polynomial coefficients (the a₇ constant of integration in `H/RT`).

---

## 2. C_v for a Cubic Equation of State

### Definition

`C_v = (∂U/∂T)_v` — the partial derivative of internal energy with respect to temperature, at constant volume.

### For an ideal gas

`U_ideal(T) = H(T) - RT`, so:
```
C_v_ideal = dU_ideal / dT = c_p(T) - R
```
This is Mayer's relation: `c_p - c_v = R` for an ideal gas.

### For a cubic EOS

```
U(T, v) = U_ideal(T) + U_dep(T, v)
C_v(T, v) = C_v_ideal + (∂U_dep / ∂T)_v
```

The departure contribution is computed numerically in `CubicEquationOfState.DeriveUDepartureByT` (`EquationsOfState.cs:146`):

```csharp
private double DeriveUDepartureByT(double T, double v)
{
    const double dT = 0.01;
    double U_plus = GetUDeparture(T + dT, v);
    double U_minus = GetUDeparture(T - dT, v);
    return (U_plus - U_minus) / (2.0 * dT);
}
```

A centered finite difference with ΔT = 0.01 K gives excellent numerical accuracy. For vdW, `U_dep = -a/v` is T-independent, so this correctly returns zero. For RK, SRK, and PR, the T-dependence of `U_dep` is captured automatically.

The full `GetCv` in `CubicEquationOfState` (`EquationsOfState.cs:156`):
```csharp
public override double GetCv(double T, double v)
{
    double c_p = SpeciesPhase.HeatCapacityFunction.Getc_p(T);
    return c_p - Constants.R + DeriveUDepartureByT(T, v);
}
```

### For an incompressible condensed phase

PV work is negligible, so `dU ≈ dH = C_p dT`, giving `C_v ≈ C_p`:
```csharp
public override double GetCv(double T, double v)
{
    return SpeciesPhase.HeatCapacityFunction.Getc_p(T);
}
```

### Use in SolveUT

`C_v` is used in the Newton iteration for temperature (Volume.cs:447):
```
ΔT = -(U - U_target) / C_v_total
```
where `C_v_total = Σ n_j · C_v,j(T, v_j)`. This is the exact Newton step for `f(T) = U(T) - U_target`, since `f'(T) = C_v_total`.

The Newton step replaces the previous proportional guess (`T *= 1 + U_error`), which assumed a linear relationship between U and T that breaks down near phase transitions and with variable heat capacities.

---

## 3. Exact Solution of Equilibrium Condensed/Gas Moles from Fugacity

### The equilibrium condition

For a species existing in both gas and condensed phases, at equilibrium the **fugacity** is equal across all phases:

```
f_j^gas = f_j^cond
```

### Fugacity for each phase

Under Pile Simulator 3's immiscible-gas, immiscible-condensed assumption:

**Gas phase** (species `j` as sole inhabitant of gas volume `V_gas`):
```
P_j = n_j^gas · R · T / V_gas    (partial pressure)
φ_j^gas = exp(GetLogphi(T, P_j, v_j^gas))    where v_j^gas = V_gas / n_j^gas
f_j^gas = φ_j^gas · P_j
```

**Condensed phase** (pure, immiscible, x_j = 1):
```
φ_j^cond = exp(GetLogphi(T, P, v_j^cond))    where P is system pressure
f_j^cond = φ_j^cond · P
```

### Solving for equilibrium n_j^gas

Setting `f_j^gas = f_j^cond`:
```
φ_j^gas · (n_j^eq · R · T / V_gas) = φ_j^cond · P
n_j^eq = (φ_j^cond / φ_j^gas) · P · V_gas / (R · T)
```

**For ideal gas + incompressible** (current setup):
- `φ_j^gas = 1` (ideal gas)
- `φ_j^cond = 1` (incompressible)
- `n_j^eq = P · V_gas / (R · T)`

This is the saturation amount: when `n_j^gas` exceeds this, the excess condenses. When below, more evaporates.

**For cubic EOS** (future, when wired up):
- Both `φ_j^gas` and `φ_j^cond` depend on `v` and therefore on `n`
- The equation `n_j^eq = (φ_cond / φ_gas) · P · V_gas / (R · T)` is implicit because `φ_gas` depends on `n_j` through `v_gas`
- Since we damp the movement (move only 50% per frame via `PhaseDamping`), an explicit solve with the current values is sufficient for convergence over multiple frames

### Implementation in SolvePhases

`Volume.SolvePhases` (`Volume.cs:496`) computes:
1. `φ_cond = exp(GetLogphi(T, P, v_cond))` — condensed at system pressure
2. `v_gas = V_gas / n_gas` — molar volume for this species in the gas phase
3. `P_gas = GetP(T, v_gas)` — partial pressure from EOS (equals `n_gas · R · T / V_gas` for ideal gas)
4. `φ_gas = exp(GetLogphi(T, P_gas, v_gas))`
5. `n_gas_eq = (φ_cond / φ_gas) · P · V_gas / (R · T)`
6. Damped step toward equilibrium: `n_gas_new = n_gas + 0.5 · (n_gas_eq - n_gas)`

The damping factor `PhaseDamping = 0.5` prevents overshoot in a single frame. Over successive frames, the system converges to the correct equilibrium.

### Condensed-to-condensed transfers

When no gas phase exists (e.g., diamond and graphite), the phase with the lower fugacity is more stable. Moles are moved from higher-fugacity to lower-fugacity phases with damping:
```csharp
double n_target = resources[i].n * 0.5;
resources[i].n -= n_target;
resources[indexOfMin].n += n_target;
```

### The relationship between fugacity equality and chemical potential equality

Fugacity equality `f_A = f_B` is equivalent to chemical potential equality `μ_A = μ_B` because:
```
μ = μ° + R T · ln(f / P°)
```
The standard state terms `μ°` are identical for the same species, so `f_A = f_B ⇒ μ_A = μ_B`. The fugacity formulation is computationally simpler because we don't need to compute `μ°` explicitly for the equilibrium split — only the *ratio* `φ_cond / φ_gas` matters.

---

## Summary of Code Flow

```
Solve()
  ├─ Dissociate()           — Arrhenius bond breaking
  ├─ RebuildIndexes()       — update species-element lookup tables
  ├─ DeriveQuantities()     — U, V, mass, etc.
  ├─ UTarget = U            — energy to conserve
  ├─ SolveReactions()       — Element Potential Method (μ at T, P)
  ├─ SolveUT()              — Newton on T: find T where U = UTarget using C_v
  ├─ SolveVP()              — Newton on P: find P where V = V_box
  ├─ RebuildIndexes()       — composition may have changed
  └─ SolvePhases()          — fugacity equilibrium for phase transfers
```

Non-idealities are consistent throughout: `Getmu` for reactions and `GetLogphi` for phases use the same EOS departure functions. `GetU` for energy conservation includes the same `U_dep` from the same EOS.
