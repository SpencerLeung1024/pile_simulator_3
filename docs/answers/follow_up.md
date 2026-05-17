# Follow-up Answers

## 1. Is `a[4]*T*T*T*T/5f` faster than `a[4] * MathF.Pow(T, 4) / 5f`?

**Yes, much faster.** `MathF.Pow` handles arbitrary real exponents using `exp(exponent * log(base))`, which costs two transcendental function calls plus multiplications. On modern x86, `Pow` might use optimized paths for integer exponents, but you can't rely on it — the spec doesn't guarantee it.

For small integer exponents (2, 3, 4), repeated multiplication is always faster and equally precise:

```
T*T          ≈ 1 mul       vs  log+exp+mul  for Pow(T, 2)
T*T*T        ≈ 2 mul       vs  log+exp+mul  for Pow(T, 3)
T*T*T*T      ≈ 3 mul       vs  log+exp+mul  for Pow(T, 4)
```

Even better, compute powers once and reuse:

```csharp
float T2 = T * T;
float T3 = T2 * T;
float T4 = T3 * T;
// Then use T, T2, T3, T4 in your polynomials
```

## 2. Why use reduced variables and compressibility factor Z?

### Reduced variables make EOS universal

Define dimensionless quantities:

$$T_r = \frac{T}{T_c}, \quad P_r = \frac{P}{P_c}$$

The Principle of Corresponding States says that if two substances are at the same $T_r$ and $P_r$, they have the same $Z = \frac{Pv}{RT}$. This emerges naturally from cubic EOS that derive a and b from $T_c$ and $P_c$.

Example: vdW in reduced form:
$$P_r = \frac{8T_r}{3v_r - 1} - \frac{3}{v_r^2}$$

Everything cancels: no $R$, no $a$, no $b$, no species-specific constants remain. The same equation describes **every** substance. This is elegant and makes the EOS feel more like a fundamental law and less like a fit to data.

### Compressibility factor Z centers the math around physically meaningful deviations

$$Z = \frac{Pv}{RT}$$

- Ideal gas: $Z = 1$
- Real gas: $Z$ deviates from 1 by an amount that tells you about non-ideality
- $Z < 1$: attractions dominate, the fluid is "smaller" than ideal
- $Z > 1$: repulsion dominates, the fluid is "larger" than ideal

The cubic in $Z$ form is:

**vdW:** $Z^3 - (1 + B)Z^2 + A Z - AB = 0$
**SRK:** $Z^3 - Z^2 + (A - B - B^2)Z - AB = 0$
**PR:** $Z^3 - (1 - B)Z^2 + (A - 2B - 3B^2)Z - (AB - B^2 - B^3) = 0$

Where $A = \frac{aP}{R^2T^2}$ (or $\frac{\alpha a P}{R^2T^2}$ for SRK/PR) and $B = \frac{bP}{RT}$.

Advantages of Z-form:

1. **Condensed notation.** $A$ and $B$ pack multiple physical constants into two dimensionless numbers. You don't write `a*P/(R*R*T*T)` everywhere — it's just `A`.
2. **Roots are meaningful.** If you get three Z roots, $Z_{max}$ is the gas, $Z_{min}$ is the liquid. Supercritical gives one Z ≈ 1.
3. **Fugacity formulas get simpler.** Compare:
   - Dimensional: $\ln\phi = \frac{b}{v-b} - \frac{2a}{RTv} - \ln(\frac{P(v-b)}{RT})$
   - Z-form: $\ln\phi = Z - 1 - \ln(Z-B) - \frac{A}{Z}$
   
   The Z-form is shorter, faster to compute (fewer operations), and less error-prone to type.
4. **Molar volume errors are obvious.** If Z comes out as 0.002 or 5000, you know something is wrong immediately. If v comes out as 1.2e-6 or 0.5, that's harder to judge without knowing the species.

### Practical approach for your code

Store both forms. Solve the cubic in Z, then convert back:

```csharp
public abstract class CubicEquationOfState
{
    public float T_c, P_c;
    
    // Compute A and B for a given T and P
    // Each subclass overrides these
    protected abstract float A(float T, float P);
    protected abstract float B(float T, float P);
    
    // Solve Z-cubic
    public float[] GetZRoots(float T, float P)
    {
        float A_val = A(T, P);
        float B_val = B(T, P);
        // coefficients of Z^3 + c2*Z^2 + c1*Z + c0 = 0
        // See formulas above per EOS type
        ...
        return SolveCubicRealRoots(1, c2, c1, c0);
    }
    
    // Convert Z to v
    public float ZtoV(float Z, float T, float P)
    {
        return Z * Constants.R * T / P;
    }
    
    // The user-facing method
    public override float Getv(float T, float P)
    {
        float[] Zroots = GetZRoots(T, P);
        // ... select root by phase as before
        return ZtoV(selectedZ, T, P);
    }
}
```

## 3. What is fugacity?

### The name

"Fugacity" comes from the same Latin root as "fugitive." It's the *tendency of a substance to escape or flee* from its current phase. A gas at high pressure has high fugacity because it really wants to expand. A liquid whose vapor pressure exceeds ambient has high fugacity because it really wants to boil.

### The definition

For an **ideal gas** at constant T, the chemical potential changes with pressure as:

$$\mu(T,P) = \mu^\circ(T) + RT \ln\left(\frac{P}{P^\circ}\right)$$

The term $RT\ln(P/P^\circ)$ is the pressure contribution to the Gibbs energy. When you compress a gas, you raise its $\mu$ — it takes more chemical "work" to add another mole.

For a **real gas**, pressure is not the correct driver. The gas experiences forces that make it behave differently from an ideal gas at the same pressure. Fugacity $f$ replaces pressure in the formula:

$$\mu(T,P) = \mu^\circ(T) + RT \ln\left(\frac{f}{P^\circ}\right)$$

Fugacity has units of pressure. For an ideal gas, $f = P$. For a real gas, $f = \phi P$ where $\phi$ is the fugacity coefficient. $\phi$ captures all the non-ideality.

### Why fugacity dictates phase equilibrium

Two phases (liquid and gas) at the same T are in equilibrium when:

$$\mu_\text{liquid}(T,P) = \mu_\text{gas}(T,P)$$

Substituting the fugacity form:

$$\mu_\text{liquid}^\circ + RT\ln\left(\frac{f_l}{P^\circ}\right) = \mu_\text{gas}^\circ + RT\ln\left(\frac{f_g}{P^\circ}\right)$$

The $P^\circ$ terms cancel, giving:

$$\boxed{f_\text{liquid} = f_\text{gas}}$$

**Phase equilibrium = equal fugacity.** This is the most compact way to state it. At saturation, liquid and gas have the same "escaping tendency."

### Maxwell construction vs. fugacity

The Maxwell equal-area rule on a P-v diagram and the fugacity equality condition are **mathematically equivalent**. They are two ways of enforcing the same thermodynamic constraint ($\mu_l = \mu_g$).

The fugacity approach is easier because:
1. You compute a number ($\ln\phi$) and compare it between phases.
2. The equal-area approach requires numeric integration of $P(v)$ — and you don't know the integration bounds ($v_l, v_g$) until you know $P_\text{sat}$.
3. Fugacity gives you a direction: if $\phi_l > \phi_g$, increase $P$ (pushes more gas into liquid). If $\phi_l < \phi_g$, decrease $P$.

### Fugacity in your steps loop

Your proposed approach — "guess P, compute all volumes, compare to V_box, adjust P" — is a volume-constrained iteration. It's valid. The fugacity method is another way: guess P, compute fugacity equality, adjust P. With your steps loop, you can fold both checks into the same iteration:

```
for each step:
    guess P
    for each species:
        compute v_liquid(P), v_gas(P) from EOS
        check: f_liquid ≈ f_gas ?  (phase equilibrium)
    sum all volumes → V_computed
    check: V_computed ≈ V_box ?   (volume constraint)
    adjust P up or down based on which way reduces both errors
```

This converges to both mechanical equilibrium (P consistent with V_box) and phase equilibrium (P = P_sat).

## 4. Internal energy for non-ideal gases in cubic EOS

### The general formula

For a cubic EOS, the internal energy departure from ideal gas is:

$$U(T,v) = U^\text{ideal}(T) + U^\text{departure}(T,v)$$

where $U^\text{ideal}(T) = H^\text{ideal}(T) - RT$ (from your heat capacity function) and the departure comes from the attractive term in the EOS:

$$U^\text{departure}(T,v) = \int_\infty^v \left[ T\left(\frac{\partial P}{\partial T}\right)_v - P \right] dv$$

This integral has a closed form for each cubic EOS:

### van der Waals

$$U(T,v) = U^\text{ideal}(T) - \frac{a}{v}$$

The departure is simply $-a/v$. The attractive forces lower the internal energy — closer molecules are more bound. As $v \to \infty$ (ideal gas limit), the departure vanishes.

### Redlich-Kwong

$$U(T,v) = U^\text{ideal}(T) - \frac{3a}{2\sqrt{T}\,b}\ln\!\left(\frac{v+b}{v}\right)$$

The $1/\sqrt{T}$ factor means the attraction weakens at high temperature, which is the main improvement over vdW.

### Soave-Redlich-Kwong

$$U(T,v) = U^\text{ideal}(T) - \frac{a}{b} \cdot \frac{d(\alpha T)}{dT} \cdot \ln\!\left(\frac{v+b}{v}\right)$$

where $\frac{d(\alpha T)}{dT} = \alpha - m\sqrt{\alpha T_r}$ (derivative of $\alpha(T) \cdot T$). In code:

```csharp
float dAlphaT_dT = Alpha - m * MathF.Sqrt(Alpha * T / T_c);
// m = 0.48508 + 1.55171*w - 0.15613*w*w
U_departure = -(a / b) * dAlphaT_dT * MathF.Log((v + b) / v);
```

### Peng-Robinson

$$U(T,v) = U^\text{ideal}(T) - \frac{a}{2\sqrt{2}\,b} \cdot \frac{d(\alpha T)}{dT} \cdot \ln\!\left(\frac{v + (1+\sqrt{2})b}{v + (1-\sqrt{2})b}\right)$$

```csharp
float dAlphaT_dT = Alpha - m * MathF.Sqrt(Alpha * T / T_c);
// m = 0.37464 + 1.54226*w - 0.26992*w*w
float sqrt2 = MathF.Sqrt(2f);
U_departure = -(a / (2f * sqrt2 * b)) * dAlphaT_dT
            * MathF.Log((v + (1f + sqrt2) * b) / (v + (1f - sqrt2) * b));
```

### The important pattern

All departures are of the form:

$$U^\text{ideal} + (\text{negative term depending on } a, T, v, b)$$

The departure is always negative — real fluids have lower internal energy than ideal gases at the same T because molecules attract. At high T or large v, the departure goes to zero.

### Code structure

```csharp
// In SpeciesPhase:
public float GetInternalEnergy(float T, float v)
{
    float U_ideal = GetH(T) - Constants.R * T; // H(T) from HCF, minus RT
    float U_departure = EquationOfState.GetUDeparture(T, v);
    return U_ideal + U_departure;
}
```

Where `GetUDeparture` is a new abstract method on `EquationOfState` that returns 0 for ideal gas and incompressible, and the formulas above for cubic EOS.

## 5. Gibbs vs. Helmholtz — sealed box should use A, right?

**Yes, at constant V and T, equilibrium minimizes A (Helmholtz), not G (Gibbs).** But this doesn't matter for the way you compute things.

### The practical answer

What you actually compute for each species is its **chemical potential** $\mu_i$. Chemical potential is the same quantity whether you're minimizing G or A — it's the partial derivative:

$$\mu_i = \left(\frac{\partial G}{\partial n_i}\right)_{T,P,n_{j \neq i}} = \left(\frac{\partial A}{\partial n_i}\right)_{T,V,n_{j \neq i}}$$

The relationship is $G = A + PV$. When you differentiate:

$$\left(\frac{\partial G}{\partial n_i}\right)_{T,P} = \left(\frac{\partial A}{\partial n_i}\right)_{T,V} + P\left(\frac{\partial V}{\partial n_i}\right)_{T,P}$$

For species in an ideal-gas mixture, $\frac{\partial V}{\partial n_i} = \frac{RT}{P}$, so you'd think they differ by $RT$. But the *partial molar volume* term is already baked into the definition: when people say "Gibbs free energy minimization" they mean minimizing $\sum n_i \mu_i$ subject to constraints, where $\mu_i$ is the **same** chemical potential regardless of ensemble. The difference between G and A minimization is in the *constraints* (constant P vs constant V), not in what $\mu_i$ means.

### For your code

- Phase equilibrium: compare $\mu(T, P)$ between phases. Works the same.
- $\mu_\text{gas} = \mu_\text{gas}^\circ(T) + RT\ln(p/P^\circ)$ — uses partial pressure, which comes from $V$ and $T$ via the ideal gas law (or real gas EOS).
- $\mu_\text{cond} = \mu_\text{cond}^\circ(T)$ for pure condensed phase.

You're solving for a T and P that satisfy:
1. $V_\text{total} = V_\text{box}$ (volume constraint)
2. $\mu_\text{liquid} = \mu_\text{gas}$ for each species at phase boundary (phase equilibrium)
3. $U_\text{total}(T) = U_\text{target}$ (energy conservation)

This is equivalent to minimizing A for a fixed-volume adiabatic box. The reason everyone says "Gibbs" is that $\mu_i$ is defined as the partial molar Gibbs energy, and that's the function you evaluate. Don't worry about the terminology — your algorithm is correct.

## 6. What does Gibbs free energy minimization look like?

The dissociation+recombination approach is a valid way to get there. Here's what "full Gibbs minimization" would look like conceptually so you know what you're not missing.

### The problem statement

You have:
- A set of elements with total amounts $N_j$ (e.g. 10 mol C, 20 mol O, 40 mol H)
- A set of possible species $i$ each with known $\mu_i(T,P)$
- Fixed T and P (or T and V)

Find the mole numbers $n_i \geq 0$ that minimize:

$$G = \sum_i n_i \mu_i$$

subject to for each element $j$:

$$\sum_i n_i \cdot a_{ij} = N_j$$

where $a_{ij}$ is the number of atoms of element $j$ in species $i$.

This is a **constrained convex optimization problem**. (Convex because $\mu$ includes $RT\ln(p_i)$ terms, and $-RT\ln(x)$ is convex.)

### The algorithm (as NASA CEA does it)

NASA's Chemical Equilibrium with Applications (CEA) uses a **Lagrange multiplier method**:

1. Form the Lagrangian:
   $$\mathcal{L} = \sum_i n_i\mu_i + \sum_j \lambda_j\left(N_j - \sum_i n_i a_{ij}\right)$$

2. At the minimum, $\frac{\partial\mathcal{L}}{\partial n_i} = 0$, giving:
   $$\mu_i - \sum_j \lambda_j a_{ij} = 0$$

   In other words: the chemical potential of each species must equal the sum of the Lagrange multipliers (element potentials) weighted by its atom counts.

3. For ideal gas species:
   $$\mu_i^\circ(T) + RT\ln\left(\frac{n_i}{n_\text{total}}\right) + RT\ln\left(\frac{P}{P^\circ}\right) = \sum_j \lambda_j a_{ij}$$

4. Solve for $n_i$:
   $$n_i = n_\text{total} \cdot \frac{P^\circ}{P} \cdot \exp\left(\frac{\sum_j \lambda_j a_{ij} - \mu_i^\circ(T)}{RT}\right)$$

5. Start with a guess for the $\lambda_j$ (one per element). Compute all $n_i$. Check if $\sum_i n_i a_{ij} = N_j$. If not, adjust $\lambda_j$ using Newton's method and repeat.

6. For condensed phases (pure solids/liquids), the same equation holds but without the $RT\ln(n_i/n_\text{total})$ mixing term. The algorithm checks: if a condensed species would have negative moles, it's not stable and gets removed from consideration.

### What it looks like operationally

```
1. Guess element potentials λ_H, λ_C, λ_O, λ_N, ... (say, all zero)
2. Compute n_i for every species in the database:
   - Gas species: n_i = n_total * exp((sum(λ_j*a_ij) - μ_i°)/(RT)) * P°/P
   - Condensed: check if μ_i_cond < sum(λ_j*a_ij); if not, n_i = 0
3. Check element balances: does sum(n_i * a_iC) = N_C?
4. If not, nudge each λ_j: λ += step * (shortage of that element)
5. Repeat until all balances within tolerance
```

The hard parts:
- The λ updates need a Jacobian matrix of partial derivatives
- Handling phase appearance/disappearance (a phase entering or leaving the solution)
- Numerical stability when species amounts span many orders of magnitude

### Why your approach is actually fine

Your "dissociate some fraction, then reform in order of stability, exponential step size reduction" is **gradient descent** on the Gibbs surface. It's not as efficient as Newton's method on the Lagrange system, but:

1. It's easy to understand and debug.
2. It naturally handles phase changes (you're already checking which phase is more stable per species).
3. The exponential step reduction gives controlled convergence.
4. You can stop early for performance.
5. Real game thermodynamics (like Stationeers) uses cruder approximations and still works fine.

NASA CEA needed a direct solver because they were computing equilibrium for thousands of species in rocket exhaust at microsecond resolution. You're making a game. Your approach is the right tradeoff.

### One improvement to your approach

Instead of sorting by "most negative enthalpy of formation," sort by **most negative $\Delta G$ of formation at the current T**:

$$\Delta_f G_i(T) = \mu_i(T) - \sum_j a_{ij} \cdot \mu_{\text{element},j}(T)$$

Or equivalently, sort by the species with lowest $\mu_i$ *after accounting for how many atoms it consumes*. This is essentially what the Lagrange multipliers do — they price each element, and you form the species that gives the best "bang for buck" (lowest Gibbs per atom consumed). You're doing this greedily per step, which is reasonable when step sizes are small.
