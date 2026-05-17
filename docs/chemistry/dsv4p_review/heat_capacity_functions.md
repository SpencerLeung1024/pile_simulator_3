# Heat Capacity Function Implementations

All three polynomial models use dimensionless coefficients and give $C_p / R$. To get $C_p$ in J/(K·mol), multiply by $R = 8.314462618$ J/(K·mol).

## Common approach for all three

Given coefficients `a[0..N]`, compute $C_p(T)$:

$$C_p(T) = R \cdot (\text{polynomial sum})$$

Then integrate for $H(T)$ and $S(T)$:

$$H(T) = H_\text{ref} + \int_{T_\text{ref}}^T C_p(\tau) \, d\tau$$
$$S(T) = S_\text{ref} + \int_{T_\text{ref}}^T \frac{C_p(\tau)}{\tau} \, d\tau$$

All integrals have closed-form solutions from the polynomial. No numerical integration needed.

## NASA7 Polynomial (200 K – 6000 K)

Has **two temperature ranges**: low (usually 200–1000 K) and high (1000–6000 K). 7 coefficients each.

$$ \frac{C_p}{R} = a_1 + a_2 T + a_3 T^2 + a_4 T^3 + a_5 T^4$$

$$ \frac{H}{RT} = a_1 + \frac{a_2}{2} T + \frac{a_3}{3} T^2 + \frac{a_4}{4} T^3 + \frac{a_5}{5} T^4 + \frac{a_6}{T}$$

$$ \frac{S}{R} = a_1 \ln T + a_2 T + \frac{a_3}{2} T^2 + \frac{a_4}{3} T^3 + \frac{a_5}{4} T^4 + a_7$$

Key point: $H/RT$ gives enthalpy **per mole**, but you want $H$ itself. So:

```
H(T) = R * T * (a1 + a2*T/2 + a3*T^2/3 + a4*T^3/4 + a5*T^4/5 + a6/T)
S(T) = R * (a1*ln(T) + a2*T + a3*T^2/2 + a4*T^3/3 + a5*T^4/4 + a7)
```

**The $a_6$ term integrates $\Delta H_f^\circ$.** The NASA polynomials use a "potential method" where $a_6$ encodes the formation enthalpy and $a_7$ encodes the formation entropy so that standard tables are reproduced. You do NOT need to separately store $\Delta H_f^\circ$ when using NASA7 — it's baked into $a_6$ and $a_7$.

### Code structure

```csharp
public class NASA7HeatCapacityFunction : HeatCapacityFunction
{
    public float[] CoeffsLow;  // 7 coefficients for low T range
    public float[] CoeffsHigh; // 7 coefficients for high T range
    public float TL;           // switch temperature (usually 1000 K)
    public float Tmin, Tmax;

    private float[] CoeffsForT(float T)
    {
        if (T <= TL) return CoeffsLow;
        else return CoeffsHigh;
    }

    public override float Getc_p(float T)
    {
        float[] a = CoeffsForT(T);
        return Constants.R * (a[0] + a[1]*T + a[2]*T*T + a[3]*T*T*T + a[4]*T*T*T*T);
    }

    public override float GetH(float T)
    {
        float[] a = CoeffsForT(T);
        return Constants.R * T * (a[0] + a[1]*T/2f + a[2]*T*T/3f + a[3]*T*T*T/4f + a[4]*T*T*T*T/5f + a[5]/T);
    }

    public override float GetS(float T)
    {
        float[] a = CoeffsForT(T);
        return Constants.R * (a[0]*MathF.Log(T) + a[1]*T + a[2]*T*T/2f + a[3]*T*T*T/3f + a[4]*T*T*T*T/4f + a[6]);
    }
}
```

## NASA9 Polynomial (200 K – 6000 K)

Extended version with 9 coefficients, better for high-temperature accuracy:

$$ \frac{C_p}{R} = a_1 T^{-2} + a_2 T^{-1} + a_3 + a_4 T + a_5 T^2 + a_6 T^3 + a_7 T^4$$

$$ \frac{H}{RT} = -a_1 T^{-2} + a_2 T^{-1}\ln T + a_3 + \frac{a_4}{2} T + \frac{a_5}{3} T^2 + \frac{a_6}{4} T^3 + \frac{a_7}{5} T^4 + \frac{a_8}{T}$$

$$ \frac{S}{R} = -\frac{a_1}{2} T^{-2} - a_2 T^{-1} + a_3 \ln T + a_4 T + \frac{a_5}{2} T^2 + \frac{a_6}{3} T^3 + \frac{a_7}{4} T^4 + a_9$$

Same split at $T_L$. The extra $T^{-2}$ and $T^{-1}$ terms capture low-temperature quantum effects (rotational/vibrational modes freezing out).

### Code structure

```csharp
public class NASA9HeatCapacityFunction : HeatCapacityFunction
{
    public float[] CoeffsLow;
    public float[] CoeffsHigh;
    public float TL;
    public float Tmin, Tmax;

    private float[] CoeffsForT(float T) { ... }

    public override float Getc_p(float T)
    {
        float[] a = CoeffsForT(T);
        return Constants.R * (a[0]/(T*T) + a[1]/T + a[2] + a[3]*T + a[4]*T*T + a[5]*T*T*T + a[6]*T*T*T*T);
    }

    public override float GetH(float T)
    {
        float[] a = CoeffsForT(T);
        return Constants.R * T * (-a[0]/(T*T) + a[1]*MathF.Log(T)/T + a[2] + a[3]*T/2f + a[4]*T*T/3f + a[5]*T*T*T/4f + a[6]*T*T*T*T/5f + a[7]/T);
    }

    public override float GetS(float T)
    {
        float[] a = CoeffsForT(T);
        return Constants.R * (-a[0]/(2f*T*T) - a[1]/T + a[2]*MathF.Log(T) + a[3]*T + a[4]*T*T/2f + a[5]*T*T*T/3f + a[6]*T*T*T*T/4f + a[8]);
    }
}
```

## Shomate Equation (298 K – 6000 K)

Used by NIST. Simpler, single temperature range:

$$ C_p = A + B t + C t^2 + D t^3 + \frac{E}{t^2} $$

where $t = T / 1000$. Note these coefficients give $C_p$ directly in J/(K·mol), not divided by R.

$$ H(T) - H(298.15) = A t + B t^2/2 + C t^3/3 + D t^4/4 - E/t + F - H$$

$$ S(T) = A \ln t + B t + C t^2/2 + D t^3/3 - E/(2t^2) + G$$

Two peculiarities:
1. $F$ is the negative of $H(298.15)$, used to shift the enthalpy curve. $H$ in the table is the measured enthalpy at 298.15 K (often $\Delta H_f^\circ$).
2. The coefficients A–G are given for specific phases and T ranges.

### Code structure

```csharp
public class ShomateHeatCapacityFunction : HeatCapacityFunction
{
    public float[] Coeffs; // [A, B, C, D, E, F, G, Hf298]
    public float Tmin, Tmax;

    public override float Getc_p(float T)
    {
        float t = T / 1000f;
        float[] c = Coeffs;
        return c[0] + c[1]*t + c[2]*t*t + c[3]*t*t*t + c[4]/(t*t);
    }

    public override float GetH(float T)
    {
        float t = T / 1000f;
        float[] c = Coeffs;
        // H(T) from Shomate gives H(T) - H(298.15) + Hf(298) = (H(T) - H(298)) + Hf(298)
        // The polynomial A*t + ... + F gives H(T) - H(298.15) in kJ/mol
        // Add Hf(298) to get absolute H(T)
        return 1000f * (c[0]*t + c[1]*t*t/2f + c[2]*t*t*t/3f + c[3]*t*t*t*t/4f - c[4]/t + c[5]) + c[7];
    }

    public override float GetS(float T)
    {
        float t = T / 1000f;
        float[] c = Coeffs;
        return c[0]*MathF.Log(t) + c[1]*t + c[2]*t*t/2f + c[3]*t*t*t/3f - c[4]/(2f*t*t) + c[6];
    }
}
```

## Which to use?

| Model | Source | Best for | Coeffs |
|-------|--------|----------|--------|
| NASA7 | Burcat/Cantera databases | Gases, combustion, wide T range | 14 (7×2) |
| NASA9 | Extended Burcat | Better accuracy at extremes | 18 (9×2) |
| Shomate | NIST WebBook | Solids, liquids, narrower T range | 8 |
| Constant | Your own | Quick prototyping | 3 ($c_p$, $\Delta H_f$, $S^\circ$) |

**Recommendation:** Use NASA7 for all gases. Use Shomate for solids and liquids (NIST has better condensed-phase data). Use Constant for prototyping species you haven't looked up yet.
