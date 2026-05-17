# Thermodynamic Fundamentals

## Why is internal energy U a function of entropy S and volume v?

From the fundamental thermodynamic relation for a simple compressible system:

$$dU = T dS - P dV$$

This means the *natural variables* of U are S and V. If you know the entropy and volume, you know the internal energy. More practically: $U(S,V)$ is a thermodynamic potential. Its partial derivatives give you other state variables:

$$\left(\frac{\partial U}{\partial S}\right)_V = T$$
$$\left(\frac{\partial U}{\partial V}\right)_S = -P$$

So `GetU(S, v)` is the "master function" from which you can, in principle, derive everything else. But in practice, **this is the wrong function to implement directly.** You don't typically know S; you know T.

## Why the current EquationOfState.GetU(S, v) signature is wrong

Your `IdealGasEquation.GetU(float S, float v)` references `c_p` and `T` but takes `S` as a parameter — and doesn't use it. Something's off. The signature should be `GetU(float T, float v)` or `GetU(float T)`.

For an ideal gas, internal energy depends **only on temperature**, not on volume:

$$U(T) = \int C_v(T) \, dT + U_\text{ref}$$

And $C_v = C_p - R$ (Mayer's relation), so for constant $C_p$:

$$U(T) = U_\text{ref} + (C_p - R)(T - T_\text{ref})$$

Note: $U_\text{ref}$ is **not** $\Delta H_f^\circ$. The relationship between U and H is:

$$H = U + PV$$

For an ideal gas: $PV = RT$, so:

$$U(T) = H(T) - RT$$

## How U, H, G, A relate in your code

Your `HeatCapacityFunction.GetH(T)` already gives you $H(T)$ — the molar enthalpy at temperature T, anchored to $\Delta H_f^\circ$ at $T_\text{ref}$.

From that you can compute everything else:

```
H(T)   = HeatCapacityFunction.GetH(T)            // J/mol, from your existing code
S(T)   = HeatCapacityFunction.GetS(T)            // J/(K mol), from your existing code
U(T,v) = H(T) - P * v                           // J/mol (general)
       = H(T) - R*T                             // J/mol (ideal gas, per mole)
       ≈ H(T)                                    // J/mol (condensed phase, PV negligible)
G(T,P) = H(T) - T * S(T)                        // J/mol, at standard pressure P°
       = H(T) - T * S(T) + RT*ln(P/P°)          // J/mol, for ideal gas at pressure P
A(T,v) = U(T) - T * S(T)                        // J/mol, Helmholtz free energy
```

## What the EquationOfState should actually provide

The EOS tells you the relationship between P, T, and v. It should provide:

| Method | Returns | Purpose |
|--------|---------|---------|
| `GetP(T, v)` | Pa | Pressure from temperature and molar volume |
| `Getv(T, P)` | m³/mol | Molar volume from temperature and pressure |
| `GetU(T, v)` or `GetU(T)` | J/mol | Internal energy at given conditions |

The EOS should **not** need to know S. Entropy comes from the HeatCapacityFunction, completely independently. This is a key insight: for the pure substances you're modeling, the EOS handles P-T-v relationships, and the heat capacity handles T-S-H-U relationships.

## For the incompressible phase EOS

A solid or liquid with constant molar volume $v_0$:

```
GetP(T, v) = anything, since v is fixed // Pressure is determined by the gas phase in the box
Getv(T, P) = v_0                        // Always returns the constant molar volume
GetU(T)    = H(T) - P*v_0               // But P*v_0 is negligible, so ≈ H(T)
```

The incompressible phase doesn't *generate* pressure. Pressure in the box comes from the gas species. Solids and liquids just displace volume.
