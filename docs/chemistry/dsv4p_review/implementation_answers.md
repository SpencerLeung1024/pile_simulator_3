# Implementation Answers

## 1. Linear Algebra Library

.NET 10 does not ship with a general-purpose dense linear solver. Your `.csproj` only references `Godot.NET.Sdk`. Options:

### Option A: Write a Gaussian elimination solver (~50 lines)

Your Jacobian is at most `(a + p) × (a + p)` where `a` ≤ 64 (elements) and `p` = 3 (phases). 67×67 is tiny. Naive Gaussian elimination with partial pivoting is O(n³) = 300,000 operations — well under a millisecond. No library needed.

```csharp
// Solves Ax = b, overwrites A with its LU decomposition and b with x
// Returns the solution in b
public static void GaussElimination(double[,] A, double[] b)
{
    int n = b.Length;
    for (int k = 0; k < n; k++)
    {
        // Partial pivoting
        int maxRow = k;
        double maxVal = Math.Abs(A[k, k]);
        for (int i = k + 1; i < n; i++)
        {
            double val = Math.Abs(A[i, k]);
            if (val > maxVal) { maxVal = val; maxRow = i; }
        }
        if (maxVal < 1e-30) throw new InvalidOperationException($"Singular matrix at column {k}");

        if (maxRow != k)
        {
            for (int j = k; j < n; j++) (A[k, j], A[maxRow, j]) = (A[maxRow, j], A[k, j]);
            (b[k], b[maxRow]) = (b[maxRow], b[k]);
        }

        // Eliminate below
        for (int i = k + 1; i < n; i++)
        {
            double factor = A[i, k] / A[k, k];
            A[i, k] = factor;
            for (int j = k + 1; j < n; j++) A[i, j] -= factor * A[k, j];
        }
    }

    // Forward substitution (using stored multipliers in lower triangle)
    for (int i = 1; i < n; i++)
    {
        double sum = b[i];
        for (int j = 0; j < i; j++) sum -= A[i, j] * b[j];
        b[i] = sum;
    }

    // Back substitution
    b[n - 1] /= A[n - 1, n - 1];
    for (int i = n - 2; i >= 0; i--)
    {
        double sum = b[i];
        for (int j = i + 1; j < n; j++) sum -= A[i, j] * b[j];
        b[i] = sum / A[i, i];
    }
}
```

### Option B: Add MathNet.Numerics (NuGet)

`dotnet add package MathNet.Numerics` gives you `Matrix<double>`, `Vector<double>`, and `matrix.Solve(vector)`. It uses optimized BLAS/LAPACK if available.

Given your project's minimal dependency philosophy and the tiny matrix size, Option A is recommended.

## 2. Vector and Matrix Objects To Avoid Nested Loops

### The nested loops are not the bottleneck

The double-nested loops in `SolveReactions` (e.g. `∑ N_m * n_ij * n_kj * x_j`) iterate `a × a × s` for the Q block. For 10 elements and 20 species, that's 2,000 multiplications. A CPU does billions per second. The compute is cheap — the bottleneck is actually `viewSpecies[j].Getmu(T, P, ...)` which calls `HeatCapacityFunction.GetH` and `GetS`, each doing several `Math.Exp`/`Math.Log` calls.

### Precompute the `view` outer product to avoid the innermost loop

The Q block builds:
```
Q_ik = sum over j of ( N_m * n_ij * n_kj * x_j )
```

You can precompute per-species contributions:

```csharp
// Precompute: for each species j, w_j = N_m * x_j
// Then: Q_ik = sum over j of w_j * n_ij * n_kj
double[] w = new double[s];
for (int j = 0; j < s; j++) w[j] = vec_n[speciesToPhase[j]] * vec_x[j];

for (int i = 0; i < a; i++)
{
    for (int k = 0; k < a; k++)
    {
        double Q_ik = 0.0;
        for (int j = 0; j < s; j++)
            Q_ik += w[j] * view[i,j] * view[k,j];
        J[i, k] = Q_ik;
    }
}
```

### C# doesn't have NumPy-style vectorized math built in

`System.Numerics.Vector<T>` does SIMD but is limited to primitive operations and works best on large uniform arrays. `MathNet.Numerics` has matrix/vector objects but still does the same loops internally (just with BLAS optimizations).

### The real approach: Structure the Jacobian assembly smarter

Notice that Q is the **symmetric** product `V @ X @ V^T` where V = view matrix (a×s) and X = diag(w_j). You can skip half the work:

```csharp
for (int i = 0; i < a; i++)
for (int k = 0; k <= i; k++) // only lower triangle
{
    double Q_ik = 0.0;
    for (int j = 0; j < s; j++)
        Q_ik += w[j] * view[i,j] * view[k,j];
    J[i, k] = Q_ik;
    J[k, i] = Q_ik; // symmetric
}
```

This halves the Q-block work. For a game with ~20 species and ~10 elements, these optimizations aren't worth the code complexity. The math is fast. Just write the clear nested loops.

## 3. Modifying the Element Potential Method for Partial Dissociation

### The problem

NASA CEA assumes every species fully dissociates. The element balances `p_i` are the *total* atoms of element i in the system, and the element potential method freely recombines them into any species.

Pile Simulator 3 dissociates **0.1% per frame**. Species like diamond persist at low T. The `freeElements` dict contains only the atoms liberated *this frame*, not the total inventory. The existing species still hold their moles.

### The fix: element pools must include locked-in atoms

The element balance `p_i` must be the **total atoms of element i across everything** — both free dissociated atoms AND atoms still bound in species that didn't dissociate this frame.

The element potential method then partitions `p_i` across all candidate species including the ones that are already present. The existing species' moles act as an initial condition, not an untouchable quantity.

### Concrete modification to the current code

In `Solve()`, currently:
- `freeElements` = atoms liberated by `Dissociate()`
- Only these freed atoms are given to `SolveReactions()`

The fix:

```csharp
public void Solve()
{
    double MassEntry = Mass;
    double UEntry = U;

    // Step A: Dissociate 0.1% this frame
    Dictionary<Element, double> freeElements = Dissociate();

    // Step B: Compute TOTAL element inventory
    // Count atoms in both freeElements (liberated this frame)
    // AND in surviving species (not dissociated)
    Dictionary<Element, double> totalElements = new Dictionary<Element, double>();
    // Copy free elements
    foreach (var kv in freeElements) totalElements[kv.Key] = kv.Value;
    // Add atoms locked in surviving species
    foreach (SpeciesPhaseResource resource in Resources)
    {
        foreach (var kv in resource.SpeciesPhase.Species.Formula)
        {
            if (!totalElements.ContainsKey(kv.Key)) totalElements[kv.Key] = 0;
            totalElements[kv.Key] += kv.Value * resource.n;
        }
    }

    // Step C: Pass TOTAL elements to the element potential solver
    // The solver will rebalance ALL moles, keeping existing species
    // as part of the initial condition (via vec_moles_existing)
    List<Element> elementList = totalElements.Keys.ToList();
    ulong newBitmask = FormulaTable.GetViewBitmask(elementList);
    if (newBitmask != bitmask)
    {
        bitmask = newBitmask;
        vec_lambda = new double[elementList.Count];
        for (int i = 0; i < vec_lambda.Length; i++) vec_lambda[i] = 0.0;
    }
    // DON'T reset vec_n to 42 on bitmask change!
    // vec_n should persist across frames — it's slow-changing
    // Only initialize on first frame:
    if (vec_n == null) { vec_n = new double[3] { 42, 42, 42 }; }

    for (uint step = 0; step < Constants.MaxSteps; step++)
    {
        double Ustart = U;
        SolveReactions(totalElements); // Pass TOTAL inventory, not just freeElements
        SolvePhases();
        DeriveQuantities();
        double Uend = U;
        ApplyHeat(Ustart - Uend);
        double gasVolume = GetGasVolume();
        double newVolume = UsedVolume + gasVolume;
        P *= (newVolume - Volume) / Volume;
        if (Math.Abs(Mass - MassEntry) / MassEntry < Constants.ConservationOfMassTolerance)
            break;
    }
}
```

### Why this works

`SolveReactions` already handles existing moles via `vec_moles_existing`:
- Species with existing moles contribute to `vec_x[j]` via the chemical potential (which depends on mole fraction)
- The element balance `H_i` includes all atoms across all species
- The solver moves moles between species to minimize Gibbs free energy

If diamond is stable given the element potentials (its μ is higher than the weighted sum), it keeps its moles. If conditions change (high T), atoms flow out of diamond and into gas species.

### The p_i vector sent to the solver must be totalElementInventory

Change `SolveReactions(Dictionary<Element, double> freeElements)` to `SolveReactions(Dictionary<Element, double> totalElementInventory)` and use this for `vec_p`. The current code at line 82-105 is correct in how it fills `vec_p` — just the input source is wrong.

## 4. Handling Moles in Liquid and Solid Phases (Oscillation Problem)

### The core issue

The element potential method uses `N_m` (total moles in phase m) as a dual variable. The species mole fraction `x_j = exp(...)` and species moles `n_j = N_m * x_j`. 

When `N_m` approaches zero, `x_j` becomes ill-defined. The Jacobian entries involving that phase approach zero, making the Newton step unstable for that phase. This can cause oscillation:

```
Frame n:   N_liquid large → solver puts species into liquid
Frame n+1: N_liquid small → solver puts species into gas
Frame n+2: N_liquid large → ... oscillates
```

### The phase disappearance problem from STANJAN

This is a known issue (PDF 1 discusses it, and `gpt_5_5.md` mentions it in the "Important Simplification" section). When a condensed phase has `N_m → 0`, that phase should be **removed from the active set**. It's not that the phase doesn't exist — it's that the dual problem formulation breaks when `N_m = 0`.

### Solution: Phase floor and phase deactivation

#### 1. Set a minimum phase total (floor)

```csharp
const double N_min = 1e-6; // 1 micromole — physically negligible

// In the Newton update step:
for (int m = 0; m < 3; m++)
{
    vec_n[m] = Math.Max(vec_n[m] + delta_x[a + m], N_min);
}
```

This prevents division by zero and keeps the Jacobian well-conditioned. The floor corresponds to a vanishingly small physical amount.

#### 2. Check if a phase should be deactivated

After each outer iteration (not inside Newton), check:

```csharp
// A phase should be deactivated if N_m has fallen to the floor
// and all its x_j are also negligible (the phase is empty)
for (int m = 0; m < 3; m++)
{
    if (vec_n[m] <= N_min * 2) // at the floor
    {
        double sum_x = 0;
        for (int j = 0; j < s; j++)
            if (speciesToPhase[j] == m) sum_x += vec_x[j];
        
        if (sum_x < 1e-3) // effectively no species in this phase
        {
            // Deactivate phase m:
            // - Set N_m = 0
            // - Remove the Z_m - 1 equation from the Newton system
            // - Remove N_m from the variable list
            // - Re-solve with phase removed
        }
    }
}
```

#### 3. Phase reactivation

At the end of each Newton loop, check if gas-phase element potentials suggest a condensed species should appear:

```csharp
// For each deactivated phase m:
// Check the "condensed phase test" from STANJAN:
// If any species j in phase m has:
//   mu_j_pure(T, P) < sum_i(lambda_i * n_ij)
// then species j wants to condense. Reactivate phase m.
```

When a phase is reactivated, set `N_m` to a small starting value (e.g. `42 * N_min`) and seed its species mole fractions uniformly: `x_j = 1.0 / count_of_species_in_phase_m`.

### The oscillation cycle in practice

With the floor in place, the system should converge monotonically rather than oscillating. In frame n, N_liquid might be pushed down to the floor. In frame n+1, more dissociation happens and the gas-phase element potentials shift. If conditions now favor liquid, the reactivation test will bring the phase back before it can oscillate.

For a game with 0.1% dissociation per frame, the snap-to-floor-plus-reactivation approach guarantees that at most one phase transition happens per frame, not dozens of oscillations.

### Alternative: 2-phase only treatment

If phase activation/deactivation logic is too complex for a first pass, just treat all three phases as always present with the floor. The floor moles (1e-6) are physically meaningless. A species with 1e-13 moles in a phase does nothing to energy, mass, or volume. The user will never see it. This is numerically safe and avoids the entire phase management problem.

## 5. ApplyHeat and Conservation of Energy

### What ApplyHeat must do

`ApplyHeat(double deltaU)` receives the net change in internal energy from reactions and phase changes in one solver step. Since the box is constant-volume, all this energy must show up as a change in temperature.

### The fundamental relation at constant volume

$$dU = T\,dS - P\,dV$$

At constant V: $dV = 0$, so $dU = T\,dS$.

This is why the current comment says "All dU goes into TdS." But you don't need to compute S explicitly. You need to find the T that satisfies:

$$U(T_\text{new}) = U(T_\text{old}) + \Delta U_\text{injected}$$

### Computing U(T)

For an ideal gas species:
$$U_i(T) = H_i(T) - RT$$

For an incompressible condensed species:
$$U_i(T) = H_i(T) \quad \text{(neglect PV term)}$$

Total internal energy:
$$U(T) = \sum_i n_i \cdot U_i(T)$$

Note: `n_i` (moles of each species-phase) have just been updated by `SolveReactions` and `SolvePhases`. The `DeriveQuantities` call already computed the `U` field. So `Ustart` and `Uend` are correct.

### But U is not a simple scalar field you can invert analytically

$U(T)$ is a sum of NASA9 polynomials — each $H_i(T)$ involves $T$, $T^2$, $T^3$, $T^4$, and $\ln(T)$ terms. You can't solve $U(T) = U_\text{target}$ algebraically.

### Newton's method for finding T from U

Since $dU/dT = \sum_i n_i \cdot c_{v,i}(T)$ where:
- $c_{v,i} = c_{p,i}(T) - R$ for gases
- $c_{v,i} = c_{p,i}(T)$ for condensed phases

```csharp
private void ApplyHeat(double deltaU)
{
    double U_target = U + deltaU; // U is current internal energy from DeriveQuantities
    double T_guess = T;

    for (int iter = 0; iter < 20; iter++)
    {
        double U_guess = 0.0;
        double C_v_total = 0.0;
        foreach (SpeciesPhaseResource resource in Resources)
        {
            SpeciesPhase sp = resource.SpeciesPhase;
            double n = resource.n;
            double H = sp.HeatCapacityFunction.GetH(T_guess);
            double c_p = sp.HeatCapacityFunction.Getc_p(T_guess);
            if (sp.Phase == Phase.Gas)
            {
                U_guess += n * (H - Constants.R * T_guess);
                C_v_total += n * (c_p - Constants.R);
            }
            else
            {
                U_guess += n * H;
                C_v_total += n * c_p;
            }
        }

        double error = U_guess - U_target;
        if (Math.Abs(error) / Math.Abs(U_target) < 1e-12)
        {
            T = T_guess;
            return;
        }

        double dT = -error / C_v_total;
        T_guess += dT;

        // Clamp: temperature cannot go negative
        if (T_guess < 1e-6) T_guess = 1e-6;
    }

    throw new InvalidOperationException("ApplyHeat did not converge");
}
```

### When ApplyHeat is called

In the current `Solve()` loop:
```csharp
for (uint step = 0; step < Constants.MaxSteps; step++)
{
    double Ustart = U;                        // Before step
    SolveReactions(freeElements);             // Changes n_j, and therefore U
    SolvePhases();                            // Moves moles between phases, changing U
    DeriveQuantities();                       // Computes new U, Mass, UsedVolume
    double Uend = U;                          // After step
    ApplyHeat(Ustart - Uend);                 // Inject energy difference back
    // ... pressure adjustment
}
```

Wait — the comment says "Conservation of energy" but the call is `ApplyHeat(Ustart - Uend)`. Let's trace:

1. `Ustart` = U before step = internal energy of the system
2. Reactions and phase changes happen. Bonds break (endothermic), bonds form (exothermic). The new composition has a different U.
3. `Uend` = U of the new composition **at the old temperature T**. But the released/absorbed energy hasn't been accounted for yet.
4. `Ustart - Uend` = the energy released (positive = exothermic, energy was released). This energy should heat the box.
5. `ApplyHeat(Ustart - Uend)` adds this energy, raising T.

Wait, that's wrong. If `Uend > Ustart` (energy increased because weaker bonds were broken), then `Ustart - Uend` is negative, meaning `ApplyHeat` gets negative deltaU, meaning the box cools. That is correct — endothermic reactions absorb heat.

If `Uend < Ustart` (energy decreased because stronger bonds formed), then `Ustart - Uend` is positive, meaning `ApplyHeat` adds heat. That is correct — exothermic reactions release heat.

**But there's a subtlety:** `Uend` was computed at the *old* T. After `ApplyHeat`, T changes. But the new T changes all species `U_i(T)`, which changes `Uend` again. This means the energy balance isn't closed in one pass.

### The correct sequence per step

```
1. Save T_old = T
2. SolveReactions(..) — updates n_j (at T_old)
3. SolvePhases()      — moves moles (at T_old)
4. Compute ΔU_reaction = U_new_composition(T_old) - U_old_composition(T_old)
   This is the chemical energy change. ΔU_reaction < 0 means exothermic.
5. T = FindT(U_old + ΔU_reaction)
   This finds the temperature where the system's internal energy matches
   the old energy plus the chemical energy change.
```

The current code does `ApplyHeat(Ustart - Uend)` where both are computed at T_old, which correctly computes `ΔU_reaction`. But then `ApplyHeat` changes T, and in the next loop iteration `DeriveQuantities` recomputes U at the new T. There's no double-counting because `SolveReactions` and `SolvePhases` run at the (new) T in the next iteration.

**The current flow is correct for an iterative scheme.** Each step:
1. React at current T
2. Phase-change at current T  
3. Compute the energy change at constant T (ΔU_chemical)
4. Apply ΔU_chemical as heat → new T
5. Repeat. The chemical equilibrium shifts at the new T in the next iteration.

### Conservation check

After the loop, the final check should be:
```csharp
double expectedU = UEntry + totalChemicalEnergyChange;
double actualU = U; // from DeriveQuantities at final T
// expectedU should equal actualU
```

If they don't match, the energy has been double-counted or leaked. The `Math.Abs(Mass - MassEntry) / MassEntry` check at line 346 currently checks mass conservation but should also check energy conservation.

### Side note: C_v calculation for cubic EOS

When cubic EOS are used, $U(T,v)$ includes the departure term from the EOS. The heat capacity $c_v = (\partial U / \partial T)_v$ picks up an extra term from the temperature derivative of the departure function. For the Newton solver, you'd need to compute this — or just use the numerical approximation $dU/dT ≈ (U(T+ε) - U(T-ε)) / 2ε$. For a game where exact thermodynamics isn't critical, the numerical derivative is fine.

## Summary

| Q | Answer |
|---|--------|
| 1. Linear algebra library | Write ~50 lines of Gaussian elimination. Matrices are small (≤67×67). |
| 2. Vector/matrix objects | Not needed. The loops are cheap. Precompute `w_j = N_m * x_j` and exploit symmetry. |
| 3. Partial dissociation + EPM | Pass **total** element inventory to the solver, not just freeElements. Existing species contribute atoms to the balance alongside dissociated ones. |
| 4. Phase mole oscillation | Set a floor `N_min = 1e-6` on all `N_m`. Deactivate/reactivate phases in an outer loop if you want correctness. The floor alone is fine for a game. |
| 5. ApplyHeat | Newton's method on U(T) using $C_v = \sum n_i c_{v,i}$. The current `Solve()` loop structure is correct for iterative energy conservation. |
