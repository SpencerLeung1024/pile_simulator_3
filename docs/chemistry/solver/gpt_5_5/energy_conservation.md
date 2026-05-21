# `ApplyHeat` And Conservation Of Energy

## Rigid Box Rule

For a closed rigid `Volume`:

$$
\Delta V = 0
$$

Boundary work is:

$$
W = P \Delta V = 0
$$

So the first law becomes:

$$
\Delta U = Q
$$

If no external heat is applied this frame, then total internal energy should be conserved:

$$
U_{after} = U_{before}
$$

Reactions and phase changes can change composition at the current `T` and `P`. When that happens, the recomputed internal energy at the old temperature can be different. The fix is not to store an arbitrary correction in `U`; the fix is to find the new temperature whose recomputed thermodynamic state has the target internal energy.

## What `ApplyHeat` Should Do

`ApplyHeat(Q)` should mean:

```text
targetU = current U + Q
find T such that U(composition, T, P_that_fits_volume(T)) = targetU
set T and P to that solution
DeriveQuantities()
```

For internal conservation after a reaction/phase step, use:

```text
targetU = U before reaction and phase changes
run reactions and phases at current T/P
find T such that new composition has U = targetU
```

The current call shape:

```csharp
ApplyHeat(Ustart - Uend);
```

has the right sign if `ApplyHeat(deltaU)` adds `deltaU` to the current internal energy target. If reactions lowered `U` at fixed `T`, then `Ustart - Uend` is positive and temperature should rise. If reactions raised `U` at fixed `T`, then temperature should fall.

## Root Solve For Temperature

Define:

$$
f(T) = U(T, P(T), composition) - U_{target}
$$

Find `T` where:

$$
f(T) = 0
$$

Use bisection first. It is slower than Newton, but easier to make reliable and fail-loud.

For each candidate `T`:

1. Find the pressure `P` that makes the material fit the rigid box.
2. Recompute every species-phase molar volume `v(T, P)`.
3. Recompute total internal energy.
4. Return `U - targetU`.

The pressure solve is nested inside the temperature solve because `U` for real gases can depend on both `T` and `v`, and `v` depends on `P`.

## Pressure In A Rigid Volume

For a given `T` and composition, find `P` such that:

$$
V_{condensed}(T,P) + V_{gas}(T,P) = V_{box}
$$

For the current ideal-gas placeholder EOS:

$$
V_{gas} = \frac{n_{gas} R T}{P}
$$

so the pressure can be computed directly:

$$
P = \frac{n_{gas} R T}{V_{box} - V_{condensed}}
$$

For cubic EOS, use a pressure root solve. The current update:

```csharp
P *= (newVolume - Volume) / Volume;
```

is not a safe pressure solve. If `newVolume < Volume`, it can make `P` negative. If the relative error is small, it can drive `P` toward zero. Use bracketing instead.

## Internal Energy Must Include Chemical Energy

Energy conservation will only work if `GetU(T, v)` includes formation enthalpy and temperature-dependent sensible energy.

For ideal gas:

$$
U(T) = H(T) - R T
$$

because one mole of ideal gas has:

$$
PV = RT
$$

and:

$$
H = U + PV
$$

So:

$$
U = H - RT
$$

For the NASA9 functions, `GetH(T)` already includes the integration constant for formation enthalpy. That is exactly what lets exothermic and endothermic reactions affect temperature.

Therefore the current ideal-gas pattern:

```csharp
return (c_p - Constants.R) * T;
```

is not sufficient for chemistry. It loses formation enthalpy and most of the NASA polynomial's integrated history. It should be conceptually:

```csharp
return SpeciesPhase.HeatCapacityFunction.GetH(T) - Constants.R * T;
```

For cubic EOS gases:

```text
U = H_ideal(T) - R T + U_departure(T, v)
```

For incompressible condensed phases in the current simple model:

```text
U ≈ H(T)
```

That is already the intent in `IncompressiblePhaseEquation`.

## Heat Capacity Shortcut

After `GetU` is correct, a simple approximate `ApplyHeat` can use total heat capacity for small energy changes:

$$
\Delta T \approx \frac{\Delta U}{C_V^{total}}
$$

For ideal gases:

$$
C_V = C_P - R
$$

For condensed phases:

$$
C_V \approx C_P
$$

This is useful as an initial guess, but not as the final conservation method. The final method should still recompute `U` and root-solve or iterate until:

$$
\frac{|U - U_{target}|}{\max(1, |U_{target}|)} < tolerance
$$

## Suggested Control Flow

One robust frame solve is:

```text
DeriveQuantities()
targetU = U + externalHeat

for step in MaxSteps:
    oldMass = Mass

    freeElements = Dissociate()
    SolveReactions(freeElements)   // pool products are added to locked remainder
    SolvePhases()                  // move moles among phases

    SolveTemperatureForU(targetU)  // calls pressure solve internally
    DeriveQuantities()

    if mass and energy errors are small:
        break
```

If no external heat enters the volume, `externalHeat = 0`, so `targetU` is the entry `U`.

If a heater adds energy, pass that as positive `externalHeat`.

If a cooler removes energy, pass that as negative `externalHeat`.

## Fail-Loud Conditions

`ApplyHeat` or `SolveTemperatureForU` should throw rather than invent a fallback if:

1. No positive temperature bracket can be found.
2. The required pressure would be negative.
3. Condensed phases occupy more than the box volume.
4. A species returns NaN or infinity for `U`, `v`, or `P`.
5. The final energy error exceeds tolerance after the maximum iterations.

Those failures mean the thermodynamic model or state is inconsistent and should be visible.
