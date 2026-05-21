# Element Potential Method: Implementation Notes for Pile Simulator 3

This document answers five specific questions about the current `Volume.cs` implementation. It assumes you have read `docs/chemistry/energy_minimization_3.md`, `docs/chemistry/dual_problem/gpt_5_5.md`, and `docs/chemistry/dual_problem/opus_4_7.md`.

## 1. What library do I import for linear algebra solving?

**Math.NET Numerics** is the standard choice for C# linear algebra.

```bash
dotnet add package MathNet.Numerics
```

```csharp
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

// Build the Jacobian
Matrix<double> J = DenseMatrix.OfArray(jArray);
Vector<double> F = DenseVector.OfArray(fArray);
Vector<double> delta = J.Solve(-F);
```

Math.NET supports LU decomposition, Cholesky, QR, etc. For the dual problem, the full $(a + 3) \times (a + 3)$ Jacobian is small (typically 8×8 to 18×18), so even naive Gaussian elimination would work. Math.NET is useful mainly for code clarity and reliability, not performance.

**However**, for a matrix this small, writing the solver by hand is ~30 lines and avoids adding a NuGet dependency to your Godot project. See Section 2 below.

---

## 2. Is there a way to use a library with vector and matrix objects so I don't have to do slow nested loops in SolveReactions?

**Short answer:** The nested loops are not slow. For your problem sizes, they cost microseconds. A library will not make it faster and may make it slower due to object allocation overhead.

**Long answer:**

In `Volume.cs` `SolveReactions` (line 82), the expensive operations are:

1. Building the Jacobian: $O(s \cdot a^2)$ operations
2. Solving the linear system: $O((a+3)^3)$ operations

For a typical case with $a = 8$ elements and $s = 30$ species:
- Jacobian assembly: ~2,000 multiply-adds
- Linear solve: ~1,300 multiply-adds
- Total per iteration: ~3,300 operations

At 3 GHz, 3,300 operations take roughly **one microsecond**. Even with 20 iterations per frame, the solver is negligible compared to Godot's overhead.

Math.NET `DenseMatrix` stores data in a flat array internally, so the memory layout is the same as your `double[,]`. The nested loops are not the bottleneck. **Cache misses from `Dictionary` lookups and `Array.IndexOf` are more expensive than the algebra.**

### What to do instead

Keep the raw arrays (`double[]`, `double[,]`) for speed. If you want cleaner code, extract helper methods:

```csharp
private static void MatVec(double[,] A, double[] x, double[] result)
{
    int n = x.Length;
    for (int i = 0; i < n; i++)
    {
        double sum = 0.0;
        for (int j = 0; j < n; j++)
            sum += A[i, j] * x[j];
        result[i] = sum;
    }
}
```

If you do add Math.NET, use it for the solve step only, not for the assembly loops. The assembly loops are faster with raw arrays because you avoid temporary object creation.

---

## 3. How do I modify the Element Potential Method to work with pre-existing moles?

**You do not need to modify the EPM mathematics.** The dual problem naturally accepts any total element inventory $p_i$ and computes the equilibrium species distribution. The modification is in *how you use the output*.

### The current bug

In `Volume.cs` line 252-254:

```csharp
foreach (SpeciesPhaseResource resource in Resources.Where(r => r.SpeciesPhase == speciesPhase))
{
    resource.n = n_j;  // BUG: replaces instead of adding
}
```

This replaces the species amount with the equilibrium amount of **only the free elements**. Since `freeElements` is ~0.1% of the total, `n_j` is tiny. You lose ~99.9% of your mass every frame. The mass conservation check at line 346 will never pass.

### The correct approach for kinetic control

Your game design uses dissociation as the rate-limiting step:

1. `Dissociate()` frees up 0.1% of moles, creating `freeElements`
2. These free elements rapidly equilibrate into species
3. The equilibrated products are **added** to the remaining (post-dissociation) species

The fix is simple: use `+=` instead of `=`.

```csharp
// At the end of SolveReactions, around line 252
SpeciesPhase speciesPhase = viewSpecies[j];
SpeciesPhaseResource resource = Resources.FirstOrDefault(r => r.SpeciesPhase == speciesPhase);
if (resource != null)
{
    resource.n += n_j;  // Add the recombined free elements
}
else if (n_j > 1e-12)
{
    // If this species didn't exist before, create it
    Resources.Add(new SpeciesPhaseResource { SpeciesPhase = speciesPhase, n = n_j });
}
```

### Fixing the chemical potential in the exponent

In the standard dual problem, the exponent uses the **pure species chemical potential** $\mu_j^\circ$ (no mixing term), because the mole fraction $x_j$ is what you are solving for:

$$x_j = \exp\left(-\frac{\mu_j^\circ}{RT} + \sum_i \lambda_i n_{ij}\right)$$

Your `SpeciesPhase.Getmu` (line 90 in `Species.cs`) includes the mixing term $RT \ln(x_j)$. When you pass `vec_moles_existing[j] / vec_n[phase]` as the `x_j` argument, you are baking an incorrect mole fraction into the exponent. This is conceptually wrong for the dual problem.

**Add a pure-chemical-potential method to `SpeciesPhase`:**

```csharp
public double GetmuPure(double T, double P)
{
    double gibbs_term = HeatCapacityFunction.GetH(T) - T * HeatCapacityFunction.GetS(T);
    double pressure_term = 0.0;
    if (Phase == Phase.Gas)
    {
        double v = EquationOfState.Getv(T, P);
        double phi = Math.Exp(EquationOfState.GetLogphi(T, P, v));
        pressure_term = Constants.R * T * Math.Log(phi * P / Constants.bar);
    }
    return gibbs_term + pressure_term;
}
```

Then in `SolveReactions` (around line 124):

```csharp
// OLD (wrong): includes mixing term with incorrect x_j
// double mu_j = viewSpecies[j].Getmu(T, P, vec_moles_existing[j] / vec_n[phase]);

// NEW: pure chemical potential only
double mu_j = viewSpecies[j].GetmuPure(T, P);
```

The `vec_moles_existing` array is no longer needed in `SolveReactions` at all. The existing species are "spectators" for the free-element equilibrium. Their only role is that they will receive the newly recombined moles via `+=`.

### Why this works

- Mass is conserved because `p_i = freeElements` and the EPM conserves elements by construction.
- The undissociated species remain untouched.
- Over many frames, the system gradually drifts toward the full equilibrium as more moles dissociate and recombine.
- The 0.1% dissociation rate is what controls the reaction speed, not the solver.

### Alternative: Full equilibrium with damping

If you want the *thermodynamically correct* equilibrium each frame, solve the EPM on the **total element inventory** ($p_i = \text{all elements in the box}$) and apply only a fraction of the change:

```csharp
// In Solve(), after computing full equilibrium n_j^eq:
foreach (SpeciesPhaseResource resource in Resources)
{
    double n_eq = /* equilibrium amount from EPM with total inventory */;
    double n_current = resource.n;
    resource.n = n_current + 0.1 * (n_eq - n_current);  // 10% step
}
```

This converges to the true equilibrium faster but requires changing how `p_i` is computed. The `+=` approach above is simpler and matches your current architecture.

---

## 4. How is the number of moles in liquid and solid phases handled?

### The mathematical problem

In the dual problem, each phase $m$ has a total mole amount $N_m$ and a normalization constraint:

$$Z_m = \sum_{j \in m} x_j = 1$$

If $N_m = 0$, the mole fractions $x_j = N_j / N_m$ are undefined (division by zero). The Jacobian block $D$ and the phase normalization rows become singular. Newton's method cannot handle $N_m = 0$.

### What happens in your code

You initialize all three phases to $N_m = 42$ (line 325). This prevents singularity, but it means:

- Even when the thermodynamic equilibrium has zero liquid, the solver is forced to distribute 42 moles across liquid species.
- The element balance residuals $H_i$ are polluted by these 42 "ghost" moles.
- When the solver tries to push $N_{liquid}$ toward zero, it undershoots, overshoots, or oscillates because the Jacobian is ill-conditioned near $N_m = 0$.

### The oscillation scenario you described

Frame $n$:
- Solver determines all elements should form gas: $N_{gas} \approx p_i^{total}$, $N_{liquid} \approx 0$, $N_{solid} \approx 0$.
- But because the Jacobian is singular near zero, the Newton step for $N_{liquid}$ might overshoot to a small negative number.
- You clamp it to zero (or near-zero).

Frame $n+1$:
- `SolvePhases()` checks fugacity. Gas has higher fugacity than liquid. It tries to condense moles into liquid.
- But $N_{liquid} = 0$. There is no liquid phase to receive them.
- `SolvePhases()` cannot create a phase from nothing.

Frame $n+2$:
- More dissociation. `SolveReactions()` again computes equilibrium. It sees no liquid phase exists (or it exists with 0 moles), so it puts everything in gas.

This loop continues until something external changes (temperature drops enough that the EPM itself tries to create liquid).

### How STANJAN handles it

STANJAN uses a **phase activation/deactivation outer loop**:

1. **Start** with only the gas phase active.
2. **Solve** the reduced dual problem.
3. **Check** all inactive condensed species. Compute:
   $$\text{drive}_j = \exp\left(-\frac{\mu_j^\circ}{RT} + \sum_i \lambda_i n_{ij}\right)$$
   If $\text{drive}_j > 1$ (or $\mu_j^\circ < \sum_i \lambda_i n_{ij}$), the species is thermodynamically favored to appear.
4. **Activate** the most favored condensed species. Add its phase with a small $N_m$ (e.g., $10^{-6}$). Re-solve.
5. **Deactivate** any phase whose $N_m$ becomes negative during Newton iteration. Remove it and re-solve.
6. Repeat until no phases appear or disappear.

### Recommended implementation for Pile Simulator 3

For a first version, you don't need the full outer loop. Use this pragmatic approach:

**During Newton iteration:**
- If $N_m$ drops below $10^{-12}$, clamp it to $10^{-12}$. Do not let it reach zero.
- The tiny amount acts as a "seed" that can grow if conditions favor it.
- After convergence, if $N_m \approx 10^{-12}$, treat the phase as empty for gameplay purposes.

**In `SolvePhases`:**
- If a species exists in two phases and one phase has $n_j < 10^{-12}$, allow `SolvePhases` to transfer a small amount ($10^{-12}$) to nucleate the phase.

**Code sketch for Newton damping with phase floor:**

```csharp
// After solving for delta_x, around line 220 in Volume.cs
for (int m = 0; m < 3; m++)
{
    vec_n[m] += delta_x[a + m];
    if (vec_n[m] < 1e-12)
        vec_n[m] = 1e-12;  // Prevent phase disappearance
}
```

**Why this works:**
- The Jacobian remains nonsingular.
- Mole fractions are always defined.
- If a phase is not thermodynamically favored, $x_j$ for its species will be astronomically small, so $N_j = N_m \cdot x_j \approx 10^{-12} \cdot 10^{-20} = 10^{-32}$, effectively zero.
- If conditions change and the phase becomes favored, the seed $10^{-12}$ can grow.

### Long-term: phase activation outer loop

Once the basic system works, implement the full STANJAN outer loop:

```csharp
public void SolveReactions(Dictionary<Element, double> freeElements)
{
    // Start with only gas active
    bool[] phaseActive = new bool[3] { true, false, false };
    
    while (true)
    {
        // Build and solve the dual problem using only active phases
        // (Resize J and vectors to a + numActivePhases)
        
        // Check if any inactive condensed species should appear
        SpeciesPhase candidate = FindMostFavorableInactiveSpecies(phaseActive);
        if (candidate == null) break;
        
        // Activate its phase
        phaseActive[(int)candidate.Phase] = true;
        vec_n[(int)candidate.Phase] = 1e-6;  // Seed
    }
}
```

---

## 5. How should ApplyHeat and conservation of energy work?

### The physics

Your `Solve()` method does:

1. `Dissociate()` breaks bonds → internal energy changes
2. `SolveReactions()` forms new bonds → internal energy changes
3. `SolvePhases()` changes phase → internal energy changes

These steps occur at constant temperature (the old $T$). The composition changes, so $U_{end} \neq U_{start}$. For an isolated box, this energy difference must go into changing the temperature:

$$U_{target} = U_{start}$$

Find $T_{new}$ such that:

$$U(T_{new}, \text{new composition}) = U_{target}$$

### The current bug

Your `ApplyHeat` receives `deltaU = U_{start} - U_{end}` but never uses it to find $T$. More importantly, the pressure update at line 345 is incorrect:

```csharp
P *= (newVolume - Volume) / Volume;  // BUG
```

If `newVolume = 1.1` and `Volume = 1.0`, this does `P *= 0.1`, which *decreases* pressure when the volume is too large. This is backwards. Even if you flip the sign, the formula doesn't correctly enforce mechanical equilibrium.

### Correct implementation

Replace `ApplyHeat` and the pressure update with a coupled temperature-pressure solve.

**Step 1: Temperature Newton iteration**

```csharp
private void ApplyHeat(double U_target)
{
    for (int iter = 0; iter < 10; iter++)
    {
        DeriveQuantities();
        double error = U_target - U;
        if (Math.Abs(error) < 1e-3) break;
        
        double Cv = GetCv(T);
        if (Cv < 1e-12) break;  // Safety
        
        T += error / Cv;
    }
}
```

**Step 2: Heat capacity at constant volume**

```csharp
private double GetCv(double T)
{
    double Cv = 0.0;
    foreach (SpeciesPhaseResource resource in Resources)
    {
        double c_p = resource.SpeciesPhase.HeatCapacityFunction.Getc_p(T);
        double c_v = c_p;
        if (resource.SpeciesPhase.Phase == Phase.Gas)
        {
            c_v -= Constants.R;  // Mayer's relation: c_p - c_v = R
        }
        // For condensed phases, c_p ≈ c_v (incompressible approximation)
        Cv += c_v * resource.n;
    }
    return Cv;
}
```

**Step 3: Pressure from mechanical equilibrium**

For a rigid box, gas must fit in `FreeVolume = Volume - UsedVolume`.

For ideal gas:
$$P = \frac{N_{gas} R T}{FreeVolume}$$

For cubic EOS, you need iteration because $v_j$ depends on $P$:

```csharp
private void UpdatePressure()
{
    double FreeVolume = Volume - UsedVolume;
    if (FreeVolume <= 0)
    {
        // Box is completely full of condensed phases. Pressure is ill-defined.
        // Set to a very high value or throw.
        P = 1e9;
        return;
    }

    // Ideal gas initial guess
    double n_gas = vec_n[(int)Phase.Gas];
    P = n_gas * Constants.R * T / FreeVolume;

    // Refine for cubic EOS
    for (int i = 0; i < 20; i++)
    {
        double gasVolume = GetGasVolume();
        double error = gasVolume - FreeVolume;
        if (Math.Abs(error) < 1e-9) break;
        
        // For ideal gas, dV/dP = -V/P. Use as approximate derivative.
        double dVdP = -gasVolume / P;
        if (Math.Abs(dVdP) < 1e-12) break;
        
        P -= error / dVdP;  // Newton step: P_new = P - (V(P) - V_target) / V'(P)
        
        if (P < 1e-6) P = 1e-6;  // Prevent negative or zero pressure
    }
}
```

**Step 4: Update `Solve()`**

```csharp
public void Solve()
{
    double MassEntry = Mass;
    double U_target = U;  // Energy before anything happens

    Dictionary<Element, double> freeElements = Dissociate();
    
    // ... bitmask check ...

    // Solve reactions once per frame, not in a loop
    SolveReactions(freeElements);
    SolvePhases();
    
    // Update U at the old T, then find the T that restores U_target
    DeriveQuantities();
    ApplyHeat(U_target);
    
    // Now that T is correct, find the pressure that fits gases in FreeVolume
    UpdatePressure();
    
    // Final derive at the correct T and P
    DeriveQuantities();

    double MassExit = Mass;
    if (Math.Abs(MassExit - MassEntry) / MassEntry > Constants.ConservationOfMassTolerance)
    {
        GD.PushError($"Mass not conserved: {MassEntry} -> {MassExit}");
    }
    
    double UExit = U;
    if (Math.Abs(UExit - U_target) > 1e-3)
    {
        GD.PushError($"Energy not conserved: {U_target} -> {UExit}");
    }
}
```

### Why remove the outer loop?

Your current `Solve()` loops up to `Constants.MaxSteps` (20), calling `SolveReactions`, `SolvePhases`, `DeriveQuantities`, `ApplyHeat`, and pressure update each time. This is confusing several different loops:

- **Newton loop** (inside `SolveReactions`): Iterates on `lambda` and `N_m` to find chemical equilibrium.
- **Temperature loop** (`ApplyHeat`): Iterates on `T` to find energy equilibrium.
- **Pressure loop** (`UpdatePressure`): Iterates on `P` to find mechanical equilibrium.
- **Your outer loop**: Seems to try to converge all three simultaneously.

These should be **separate, nested loops**, not one flat loop:

1. Outer: chemistry at fixed T, P (Newton on `lambda`, `N_m`)
2. Middle: energy at fixed composition (Newton on `T`)
3. Inner: mechanics at fixed T, composition (Newton on `P`)

For your game, you can simplify further:
- Run the chemistry solver **once** per frame (with the kinetic `+=` design, it only operates on 0.1% of moles).
- Run the temperature solver **once** per frame.
- Run the pressure solver **once** per frame.
- There is no need for an outer loop around all three.

If you later want to find the exact thermodynamic equilibrium (no kinetic damping), you would iterate:
- Guess T, P
- Solve chemistry at T, P
- Adjust T for energy conservation
- Adjust P for volume constraint
- Repeat until T and P stop changing (typically 2-5 outer iterations)

---

## Summary of required code changes

| Location | Change |
|----------|--------|
| `Species.cs` line 90 | Add `GetmuPure(T, P)` method without mixing term |
| `Volume.cs` line 124 | Use `GetmuPure` instead of `Getmu` in `SolveReactions` |
| `Volume.cs` line 252 | Change `resource.n = n_j` to `resource.n += n_j` |
| `Volume.cs` line 220 | Add clamp: `if (vec_n[m] < 1e-12) vec_n[m] = 1e-12` |
| `Volume.cs` line 289 | Rewrite `ApplyHeat` to find T via Newton iteration |
| `Volume.cs` line 345 | Replace with `UpdatePressure` method |
| `Volume.cs` line 329 | Remove or simplify the outer `for` loop in `Solve` |
| `Volume.cs` line 217 | Implement linear solve (Math.NET or hand-written LU) |

The most critical fix is **`resource.n += n_j`** and **`GetmuPure`**. Without these, the chemistry solver destroys mass and computes the wrong equilibrium.
