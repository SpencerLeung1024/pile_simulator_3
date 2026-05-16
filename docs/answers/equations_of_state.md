# Equations of State & Saturation Pressure

## Overview

You have four EOS to implement, plus the ideal gas and incompressible phase. They share the pattern:

```
P(T, v) → pressure from temperature and molar volume
v(T, P) → molar volume from temperature and pressure (may have multiple roots)
U(T, v) or U(T) → internal energy
```

Cubic EOS all produce a cubic polynomial in v when you know T and P:
$$v^3 + c_2 v^2 + c_1 v + c_0 = 0$$

## The Cubic Equation of State abstract class (fix)

Your abstract class is mostly right. The main fix: compute `GetvRoots` by solving a cubic. Here's a robust cubic solver:

```csharp
// Returns real roots in ascending order (1 or 3 roots)
public static float[] SolveCubicRealRoots(float a, float b, float c, float d)
{
    // Normalize: x^3 + A*x^2 + B*x + C = 0
    float A = b / a;
    float B = c / a;
    float C = d / a;

    // Depress: substitute x = t - A/3 → t^3 + p*t + q = 0
    float p = B - A*A / 3f;
    float q = (2f*A*A*A / 27f) - (A*B / 3f) + C;

    // Discriminant: Δ = (q/2)^2 + (p/3)^3
    float disc = (q*q / 4f) + (p*p*p / 27f);

    if (disc > 0)
    {
        // One real root
        float sqrtDisc = MathF.Sqrt(disc);
        float u = MathF.Cbrt(-q/2f + sqrtDisc);
        float v = MathF.Cbrt(-q/2f - sqrtDisc);
        float t1 = u + v;
        return new float[] { t1 - A/3f };
    }
    else if (disc < 0)
    {
        // Three real roots (trigonometric solution)
        float r = MathF.Sqrt(-p*p*p / 27f);
        float phi = MathF.Acos(-q / (2f * MathF.Sqrt(-p*p*p / 27f)));
        float t0 = 2f * MathF.Cbrt(-p/3f) * MathF.Cos(phi/3f);
        float t1 = 2f * MathF.Cbrt(-p/3f) * MathF.Cos((phi + 2f*MathF.PI)/3f);
        float t2 = 2f * MathF.Cbrt(-p/3f) * MathF.Cos((phi + 4f*MathF.PI)/3f);
        float[] ts = new float[] { t0, t1, t2 };
        System.Array.Sort(ts);
        float shift = A/3f;
        return new float[] { ts[0] - shift, ts[1] - shift, ts[2] - shift };
    }
    else
    {
        // disc == 0: multiple root at the critical point
        float t1 = 2f * MathF.Cbrt(-q/2f);
        float t2 = -MathF.Cbrt(-q/2f);
        return new float[] { MathF.Min(t1, t2) - A/3f, MathF.Max(t1, t2) - A/3f };
    }
}
```

## 1. Incompressible Phase (solids and, optionally, liquids)

The simplest EOS. Fixed molar volume $v_0$ (from density: $v_0 = M / \rho$).

```csharp
public class IncompressiblePhaseEquation : EquationOfState
{
    public float v; // m^3/mol, constant molar volume

    public override float GetP(float T, float v)
    {
        // An incompressible phase doesn't generate pressure.
        // Pressure is set externally (by gas in the box).
        // Return 0 as a sentinel; the caller should understand this means
        // "use ambient/box pressure."
        return 0.0f;
    }

    public override float Getv(float T, float P)
    {
        return v;
    }

    public override float GetU(float T)
    {
        // U = H - PV ≈ H (PV is tiny for condensed phases)
        // GetH comes from HeatCapacityFunction
        // The equation doesn't need to implement this itself;
        // the SpeciesPhase or Volume computes it from the HCF.
        return 0.0f; // Placeholder: compute U = H(T) - P*v from outside
    }
}
```

## 2. van der Waals

You already have the structure. Just implement `GetvRoots`:

```csharp
public class VanDerWaalsEquation : CubicEquationOfState
{
    public float a, b;

    public void CalculateCriticalConstants()
    {
        a = (27f/64f) * Constants.R * Constants.R * T_c * T_c / P_c;
        b = (1f/8f) * Constants.R * T_c / P_c;
    }

    public override float GetP(float T, float v)
    {
        return Constants.R * T / (v - b) - a / (v * v);
    }

    public override float[] GetvRoots(float T, float P)
    {
        // v^3 - (RT/P + b)*v^2 + (a/P)*v - (a*b/P) = 0
        float c3 = 1f;
        float c2 = -(Constants.R * T / P + b);
        float c1 = a / P;
        float c0 = -(a * b) / P;
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }

    public override float GetU(float S, float v) { ... } // deprecated signature
}
```

## 3. Redlich-Kwong

```csharp
public class RedlichKwongEquation : CubicEquationOfState
{
    public float a, b;

    public void CalculateCriticalConstants()
    {
        a = 0.42748f * Constants.R * Constants.R * MathF.Pow(T_c, 2.5f) / P_c;
        b = 0.08664f * Constants.R * T_c / P_c;
    }

    public override float GetP(float T, float v)
    {
        return Constants.R * T / (v - b) - a / (MathF.Sqrt(T) * v * (v + b));
    }

    public override float[] GetvRoots(float T, float P)
    {
        // v^3 - (RT/P)*v^2 + (1/P)*(a/sqrt(T) - bRT - Pb^2)*v - (a*b)/(P*sqrt(T)) = 0
        float c3 = 1f;
        float c2 = -(Constants.R * T) / P;
        float c1 = (a / MathF.Sqrt(T) - b * Constants.R * T - P * b * b) / P;
        float c0 = -(a * b) / (P * MathF.Sqrt(T));
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }
}
```

## 4. Soave-Redlich-Kwong (SRK)

```csharp
public class SoaveRedlichKwongEquation : CubicEquationOfState
{
    public float a, b;
    public float omega; // acentric factor

    public void CalculateCriticalConstants()
    {
        a = 0.42748f * Constants.R * Constants.R * T_c * T_c / P_c;
        b = 0.08664f * Constants.R * T_c / P_c;
    }

    private float Alpha(float T)
    {
        float m = 0.48508f + 1.55171f * omega - 0.15613f * omega * omega;
        float sqrtTr = MathF.Sqrt(T / T_c);
        float factor = 1f + m * (1f - sqrtTr);
        return factor * factor;
    }

    public override float GetP(float T, float v)
    {
        float alphaA = Alpha(T) * a;
        return Constants.R * T / (v - b) - alphaA / (v * (v + b));
    }

    public override float[] GetvRoots(float T, float P)
    {
        float alphaA = Alpha(T) * a;
        // Multiply P = RT/(v-b) - alphaA/(v(v+b)) by (v-b)(v)(v+b)/P:
        // v^3 - (RT/P)*v^2 + (alphaA/P - bRT/P - b^2)*v - (alphaA*b)/P = 0
        float c3 = 1f;
        float c2 = -(Constants.R * T) / P;
        float c1 = (alphaA - b * Constants.R * T - P * b * b) / P;
        float c0 = -(alphaA * b) / P;
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }
}
```

## 5. Peng-Robinson

```csharp
public class PengRobinsonEquation : CubicEquationOfState
{
    public float a, b;
    public float omega;

    public void CalculateCriticalConstants()
    {
        a = 0.45724f * Constants.R * Constants.R * T_c * T_c / P_c;
        b = 0.07780f * Constants.R * T_c / P_c;
    }

    private float Alpha(float T)
    {
        float m = 0.37464f + 1.54226f * omega - 0.26992f * omega * omega;
        float sqrtTr = MathF.Sqrt(T / T_c);
        float factor = 1f + m * (1f - sqrtTr);
        return factor * factor;
    }

    public override float GetP(float T, float v)
    {
        float alphaA = Alpha(T) * a;
        return Constants.R * T / (v - b) - alphaA / (v*v + 2f*b*v - b*b);
    }

    public override float[] GetvRoots(float T, float P)
    {
        float alphaA = Alpha(T) * a;
        // v^3 + (b - RT/P)*v^2 + (alphaA/P - 3b^2 - 2bRT/P)*v + b*(b^2 + bRT/P - alphaA/P) = 0
        float c3 = 1f;
        float c2 = b - Constants.R * T / P;
        float c1 = alphaA / P - 3f * b * b - 2f * b * Constants.R * T / P;
        float c0 = b * (b * b + b * Constants.R * T / P - alphaA / P);
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }
}
```

## Saturation Pressure (Maxwell Construction)

### The problem

Below $T_c$, a cubic EOS gives 3 roots for v at certain pressures. The middle root is unphysical (it has $(\partial P/\partial v)_T > 0$, meaning compressing gas makes pressure drop — unstable). The liquid and gas roots are stable.

But: **which pressure is the saturation pressure?** At saturation, the liquid and gas phases coexist: they have the same T and P, and the same chemical potential (Gibbs free energy). The Maxwell equal-area rule says:

$$\int_{v_l}^{v_g} P(v,T) \, dv = P_\text{sat} \cdot (v_g - v_l)$$

Or equivalently: $P_\text{sat}$ is the pressure where the areas above and below the saturation line on the P-v diagram are equal.

### How to actually compute it

Don't integrate areas. Use the chemical potential equality:

$$\mu(T, v_l) = \mu(T, v_g)$$

For a cubic EOS, the chemical potential at given $T,v$ is:

$$\mu(T,v) - \mu^\text{ideal}(T,P^\circ) = \int_{\infty}^v \left[ \frac{RT}{v'} - P(T,v') \right] dv' + RT\ln\!\left(\frac{v}{RT/P^\circ}\right)$$

The integral $\int_{\infty}^v [\frac{RT}{v'} - P(T,v')] dv'$ has a closed form for each EOS. Compute $\mu(T,v_l) - \mu(T,v_g)$. Adjust $P_\text{sat}$ until this difference is zero.

**But there's a much easier iterative method:**

```
function FindSaturationPressure(T):
    // Get initial guess from vapor pressure correlation
    P_sat = AntoineOrClausiusClapeyron(T)

    for iteration in 1..max_iter:
        // Find the largest and smallest roots at this P_sat
        roots = GetvRoots(T, P_sat)
        if roots.Length < 3:
            // Above critical point or P_sat guess is bad
            adjust and retry
            continue

        v_l = roots[0]  // liquid molar volume
        v_g = roots[2]  // gas molar volume

        // Compute fugacity coefficients
        phi_l = FugacityCoefficient(T, v_l, P_sat)
        phi_g = FugacityCoefficient(T, v_g, P_sat)

        // At equilibrium: P_sat * phi_l = P_sat * phi_g, so phi_l = phi_g
        ratio = phi_l / phi_g

        if |ratio - 1| < tolerance:
            return P_sat

        // Adjust P_sat: if phi_l > phi_g, increase P_sat (more liquid)
        P_sat *= ratio

    return P_sat
```

### Fugacity coefficient formulas (the key part)

For each cubic EOS, $\ln\phi$ has a closed form:

**van der Waals:**
$$\ln\phi = \frac{b}{v-b} - \frac{2a}{RTv} - \ln\!\left(\frac{P(v-b)}{RT}\right)$$

**Redlich-Kwong:**
$$\ln\phi = \frac{b}{v-b} - \frac{2a}{RT^{1.5}b}\ln\!\left(\frac{v+b}{v}\right) - \ln\!\left(\frac{P(v-b)}{RT}\right)$$

**Soave-Redlich-Kwong:**
$$\ln\phi = \frac{b}{v-b} - \frac{2\alpha a}{RTb}\ln\!\left(\frac{v+b}{v}\right) - \ln\!\left(\frac{P(v-b)}{RT}\right)$$

**Peng-Robinson:**
$$\ln\phi = \frac{b}{v-b} - \frac{2\alpha a}{RT}\ln\!\left(\frac{v + (1+\sqrt{2})b}{v + (1-\sqrt{2})b}\right) \cdot \frac{1}{2\sqrt{2}b} - \ln\!\left(\frac{P(v-b)}{RT}\right)$$

Wait — that last one is messy. Let me write cleaner:

For Peng-Robinson:
$$\ln\phi = \frac{b}{v-b} - \ln\!\left(\frac{P(v-b)}{RT}\right) - \frac{\alpha a}{2\sqrt{2}RTb} \ln\!\left(\frac{v + (1+\sqrt{2})b}{v + (1-\sqrt{2})b}\right)$$

### The iterative algorithm, concretely

```csharp
public float FindSaturationPressure(float T)
{
    // Initial guess: roughly P_c * exp(A*(1 - T_r))
    float Tr = T / T_c;
    float P_guess = P_c * MathF.Exp(5.373f * (1f + omega) * (1f - 1f / Tr));

    for (int i = 0; i < 20; i++)
    {
        float[] roots = GetvRoots(T, P_guess);
        if (roots.Length < 3)
        {
            P_guess *= 0.95f; // reduce pressure to get into two-phase region
            continue;
        }

        float v_l = roots[0];
        float v_g = roots[2];
        float fugRatio = FugacityCoefficient(T, v_l, P_guess) / FugacityCoefficient(T, v_g, P_guess);

        if (MathF.Abs(fugRatio - 1.0f) < 1e-6f)
            return P_guess;

        P_guess *= fugRatio;
    }

    throw new Exception("Saturation pressure did not converge");
}
```

### Code pattern for fugacity in each class

Add an abstract method to `CubicEquationOfState`:

```csharp
public abstract float FugacityCoefficient(float T, float v, float P);
```

Then implement in each subclass. Example for van der Waals:

```csharp
public override float FugacityCoefficient(float T, float v, float P)
{
    float Z = P * v / (Constants.R * T); // compressibility factor
    float term1 = b / (v - b);
    float term2 = 2f * a / (Constants.R * T * v);
    float term3 = MathF.Log(P * (v - b) / (Constants.R * T));
    return term1 - term2 - term3;
    // Return ln(phi), not phi itself
}
```

Wait — I need to be more careful. The expression should return $\ln\phi$:

For van der Waals:
$$\ln\phi = \frac{v}{v-b} - 1 - \frac{a}{RTv} + \ln\!\left(\frac{RT}{P(v-b)}\right)$$

Or equivalently (cleaner):
$$\ln\phi = Z - 1 - \ln(Z - B) - \frac{A}{Z}$$

Where $A = aP/(R^2T^2)$, $B = bP/(RT)$, $Z = Pv/(RT)$.

**Better approach:** Use the dimensionless form with $A, B, Z$:

```csharp
// In CubicEquationOfState base class, a helper:
protected float LogPhiFromABZ(float A, float B, float Z, float v_plus, float v_minus, float sigma, float epsilon)
{
    // General form for cubic EOS:
    // ln(phi) = Z - 1 - ln(Z - B) - (A/B)*q*I
    // where I depends on the EOS, q is a constant factor
    throw new NotImplementedException(); // implement per subclass
}
```

Actually, let me give the **cleanest implementation** — compute $\ln\phi$ for each EOS using $Z$ compressibility form:

### Dimensionless fugacity formulas

Define:
```
B = b*P / (R*T)
A = a*P / (R^2*T^2)     (for vdW, RK)
  = alpha*a*P / (R^2*T^2) (for SRK, PR)
Z = P*v / (R*T)
```

**van der Waals:**
$$\ln\phi = Z - 1 - \ln(Z - B) - \frac{A}{Z}$$

**Redlich-Kwong:**
$$\ln\phi = Z - 1 - \ln(Z - B) - \frac{A}{B}\ln\!\left(\frac{Z + B}{Z}\right)$$

**Soave-Redlich-Kwong:**
$$\ln\phi = Z - 1 - \ln(Z - B) - \frac{A}{B}\ln\!\left(\frac{Z + B}{Z}\right)$$

**Peng-Robinson:**
$$\ln\phi = Z - 1 - \ln(Z - B) - \frac{A}{2\sqrt{2}B}\ln\!\left(\frac{Z + (1+\sqrt{2})B}{Z + (1-\sqrt{2})B}\right)$$

This is the cleanest way. Implement in each class:

```csharp
// Van der Waals
public override float LogFugacityCoefficient(float T, float v, float P)
{
    float Z = P * v / (Constants.R * T);
    float B = b * P / (Constants.R * T);
    float A = a * P / (Constants.R * Constants.R * T * T);
    return Z - 1 - MathF.Log(Z - B) - A / Z;
}

// SRK
public override float LogFugacityCoefficient(float T, float v, float P)
{
    float Z = P * v / (Constants.R * T);
    float B = b * P / (Constants.R * T);
    float A = Alpha(T) * a * P / (Constants.R * Constants.R * T * T);
    return Z - 1 - MathF.Log(Z - B) - (A / B) * MathF.Log((Z + B) / Z);
}

// Peng-Robinson
public override float LogFugacityCoefficient(float T, float v, float P)
{
    float Z = P * v / (Constants.R * T);
    float B = b * P / (Constants.R * T);
    float A = Alpha(T) * a * P / (Constants.R * Constants.R * T * T);
    float sqrt2 = MathF.Sqrt(2f);
    return Z - 1 - MathF.Log(Z - B)
        - A / (2f * sqrt2 * B) * MathF.Log((Z + (1f + sqrt2) * B) / (Z + (1f - sqrt2) * B));
}
```

Then `FugacityCoefficient(T, v, P)` is `MathF.Exp(LogFugacityCoefficient(T, v, P))`.

## When to use cubic EOS vs ideal gas + incompressible

**For your toy model, you do NOT need cubic EOS.** The recommended approach (endorsed by all four design docs):

1. Use **ideal gas** for the gas phase
2. Use **incompressible phase** for condensed phases (solids, liquids)
3. Use vapor pressure correlations (Antoine or derived from $\mu$) to handle phase transitions

Cubic EOS are nice to have but not essential. The ideal gas assumption makes saturated vapor pressure calculation trivial:

$$P_\text{sat}(T) = P^\circ \cdot \exp\!\left(\frac{\mu_\text{cond}(T) - \mu_\text{gas}^\circ(T)}{RT}\right)$$

Where $\mu_\text{cond}(T) = H_\text{cond}(T) - T \cdot S_\text{cond}(T)$ (for pure condensed phase) and $\mu_\text{gas}^\circ(T) = H_\text{gas}(T) - T \cdot S_\text{gas}(T)$ (standard state).

This is **vastly simpler** than Maxwell construction and gives correct results because your condensed phases are immiscible. You still get ice/water/steam, sublimation, etc.

Cubic EOS become needed if/when you want:
- Gas non-ideality at high pressure
- Supercritical fluid behavior (continuous transition)
- Gas solubility in liquids (which you've assumed away)
