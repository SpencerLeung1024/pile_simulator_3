# Finishing the First Version of the Chemistry System

Answers to five questions that came up while writing `DSA/Chemistry/Volume.cs`. Numbered as the user asked them.

The TL;DR:
1. MathNet.Numerics
2. Yes, the same library
3. Treat the dissociated atoms as the only thing the solver operates on, and keep un-dissociated species locked out. That subsetting is a one-line change to how `vec_p` is built
4. Don't try to make EPM handle condensed phases. Solve gas equilibrium with EPM, then run `SolvePhases` on its own. Carry tiny "seed" moles in every (species, phase) so x_j is always defined
5. Operator-split. Run isothermal reactions, find the U change, then adjust T at constant U with a Newton step on T using total heat capacity. Latent heat is handled by the phase loop, not by the T-Newton

## 1. Linear Algebra Library

**Use [MathNet.Numerics](https://numerics.mathdotnet.com/).** Add it as a NuGet package to `Pile Simulator 3.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="MathNet.Numerics" Version="5.0.0" />
</ItemGroup>
```

Why MathNet.Numerics:

- Pure managed C#, MIT license. No native binaries to ship with the Godot export.
- Has `Matrix<double>` and `Vector<double>` with operator overloading (`A * x`, `x + y`, `M.Solve(b)`).
- Has LU, QR, Cholesky, and SVD decompositions exposed as `A.Solve(b)` (LU by default).
- Optional pluggable provider for Intel MKL / OpenBLAS if you ever need it. The default managed provider is already fast enough for a + p ≤ 30 matrices.

What I rejected:

- **`System.Numerics.Tensors`** (built into .NET 10): has element-wise tensor ops but no `Solve()`. You would have to write the LU decomposition yourself, defeating the point.
- **`System.Numerics.Vector<T>`**: SIMD primitives only. Not a matrix solver.
- **`CSparse.NET`**: only sparse matrices. The EPM Jacobian is dense and small.
- **`MKL.NET`**: fast but native binaries are a pain in Godot's export pipeline.

For your problem size (a typically 3-10, p = 3, so the Jacobian is at most 13×13), even naive Gaussian elimination would be fine. The library gives you readability, not speed. Speed comes from how you assemble the matrix, not how you solve it.

## 2. Vector and Matrix Objects

Yes, MathNet.Numerics solves this directly. Replace your `double[,]` and `double[]` with:

```csharp
using MathNet.Numerics.LinearAlgebra;

Vector<double> vec_p     = Vector<double>.Build.Dense(a);
Vector<double> vec_x     = Vector<double>.Build.Dense(s);
Vector<double> vec_lambda = Vector<double>.Build.Dense(a);
Matrix<double> J         = Matrix<double>.Build.Dense(a + 3, a + 3);
```

### What can actually be vectorized

Look at your Jacobian quadrant Q in `SolveReactions`:

```
Q_ik = sum over j of (N_phase(j) * n_ij * n_kj * x_j)
```

That is exactly the matrix product

```
Q = A · diag(w) · A^T
```

where `A` is the `a × s` stoichiometry matrix (the `view` table you already build), and `w` is the length-`s` weight vector with `w_j = N_phase(j) * x_j`. With MathNet.Numerics:

```csharp
// Build A as an a x s sparse-ish matrix (it's small, just use Dense)
Matrix<double> A = Matrix<double>.Build.Dense(a, s, (i, j) => (double)view[i, j]);

// Build w
Vector<double> w = Vector<double>.Build.Dense(s,
    j => vec_n[speciesToPhase[j]] * vec_x[j]);

// Q = A * diag(w) * A^T, done as A * (A^T scaled by w)
Matrix<double> Adiag = A.PointwiseMultiply(
    Matrix<double>.Build.Dense(a, s, (_, j) => w[j]));
Matrix<double> Q = Adiag * A.Transpose();
```

That replaces your triple nested loop with one matrix product. For small `a` and `s` the speedup is unimpressive, but the *code* is dramatically clearer, and once you grow beyond ~50 species the speedup is real because MathNet falls through to a BLAS routine.

Similarly the D block:

```
D_im = sum over j in phase m of (n_ij * x_j)
```

is just `D = A · X_phase` where `X_phase` is the `s × p` matrix with `X_phase[j, m] = x_j if speciesToPhase[j] == m else 0`. One matrix product.

And the element-balance residual `H_i` is `H = A * (w_full) - p`, where `w_full[j] = N_phase(j) * x_j` (same `w` as above).

### Solving the linear system

```csharp
Vector<double> delta_x = J.Solve(vec_F.Negate());
// Or, more idiomatically:
Vector<double> delta_x = -J.Solve(vec_F);
```

That's it. No hand-rolled Gaussian elimination.

### What stays as nested loops

Computing each `x_j = exp(-mu_j / RT + sum_i lambda_i * n_ij)` is still per-species, because `mu_j` comes from `viewSpecies[j].Getmu(...)` which is not a vectorizable function (it dispatches into a HeatCapacityFunction and an EquationOfState). That loop is `O(s)` and is the dominant cost per Newton step. Live with it.

If you wanted to vectorize that too, you would precompute a vector `mu` once per Newton iteration (each species reads T and P, which are fixed during a single iteration) and then do `vec_x = (-mu / RT + A^T * lambda).PointwiseExp()`. That is worth doing once the species count grows past ~50.

## 3. Modifying the Element Potential Method for Pre-Existing Moles

This is the most important question, and the one where NASA CEA / STANJAN cannot be copied verbatim.

### What NASA CEA does

NASA CEA assumes you hand it a vector `p_i` = "total moles of element i in the box", and asks for the equilibrium distribution across all species. Everything that was a molecule becomes free atoms first, then re-assembles into whatever minimizes G. No species is "protected".

### What Pile Simulator 3 does

Per `AGENTS.md`, only 0.1% of moles dissociate per frame, and any species below its `DissociationTemperature` cannot dissociate at all. Diamond at 300 K never enters the atomic pool. This means equilibrium is approached gradually, in game time.

The mismatch is that EPM as written assumes **the whole inventory is up for grabs**. If you feed it `p_i = total atoms in the box`, it will tell you "in equilibrium, all that diamond should be CO2 and graphite" and helpfully recombine your diamond. Bad.

### The fix: two pools

Split every species' moles into two buckets:

- **Locked pool**: the un-dissociated portion. Untouchable this frame.
- **Free pool**: the atoms freed by `Dissociate()`. These are what EPM operates on.

`Dissociate()` is already doing the split correctly. After it runs:
- `resource.n` is the locked pool (what didn't dissociate)
- `freeElements[element]` is the free pool (the atoms that did dissociate, summed across all species that contributed them)

The EPM should then equilibrate **only the free pool** and write its results into a **new set of resource moles**, which get **added** to the locked pool at the end.

### Concrete algorithm

```
foreach (resource in Resources):
    resource.n_locked = resource.n  // the un-dissociated portion

freeElements = Dissociate()  // already subtracts from resource.n

// EPM operates on the freeElements as p_i
// It is allowed to populate any SpeciesPhase in viewSpecies, including ones
// that already have a locked amount in Resources

vec_p[i] = freeElements[viewElements[i]]   // ONLY the freed atoms

// Newton iteration produces vec_x and vec_n (phase totals)
// Then n_j = vec_n[phase(j)] * vec_x[j]
// This is the "freshly equilibrated" amount

// Write back: ADD to the locked portion
foreach (j in viewSpecies):
    resource_for_j.n = resource_for_j.n_locked + n_j_fresh
```

### What this means physically

- A species at 100 mol with 0.1% dissociation per frame loses 0.1 mol to the free pool. Whatever EPM does with those atoms (recombines back into the same species, makes new species, etc.) gets *added back* on top of the remaining 99.9 mol.
- Diamond at 300 K never dissociates → never enters the free pool → never disappears.
- Reactions take many frames to reach completion because only 0.1% participates each frame. This is the whole point of `DissociationThreshold`.
- The mole fractions used inside `Getmu(T, P, x_j)` for the mixing term must be computed against the full present amount (locked + fresh), not just the fresh part, because that's the actual mole fraction in the phase. See the next bullet.

### Mole fraction in `Getmu`

`Getmu(T, P, x_j)` uses `x_j` to compute `RT ln(x_j)`, the entropy of mixing. The "x_j" that goes in here is the species' actual fraction in its phase right now, which depends on locked + fresh:

```
x_j_for_mu = (resource_j.n_locked + n_j_fresh_previous_iteration) / N_phase_total
```

`N_phase_total` includes the locked moles too:

```
N_phase_total = sum over all resources in this phase of (n_locked + n_fresh)
```

This is a chicken-and-egg: `n_fresh` is what you're solving for. The clean way out is:

- Use the previous frame's `n_fresh` (or just 0 on the first frame) when calling `Getmu`. Since you're already amortizing the solve across frames, this is consistent. The system converges as the previous-frame guess becomes the same as the current-frame answer.

This is the "frozen-composition mu" approximation. It's exactly what NASA CEA does internally to make the Newton step well-defined.

### Code shape

Add a `LockedN` field to `SpeciesPhaseResource`:

```csharp
public class SpeciesPhaseResource
{
    public SpeciesPhase SpeciesPhase;
    public double n;        // total moles = locked + fresh
    public double LockedN;  // what survived dissociation this frame
}
```

`Dissociate` populates `LockedN = n` minus what it pulled out (so just leave `LockedN = n` after subtraction). `SolveReactions` writes the fresh moles. Final `n = LockedN + n_fresh`.

### What about reactions between locked species?

A subtle thing. Suppose you have locked H2O (gas) at 99.9 mol and locked CO2 (gas) at 99.9 mol. They are not free to react because nothing has dissociated. That's correct game behavior: reactions are gated by dissociation. At low T, water and carbon dioxide coexist forever.

But what if 0.1 mol of each does dissociate, and EPM decides the free atoms should make a tiny bit of CH4 + O2? Then yes, CH4 appears, and the locked H2O and CO2 stay put. Next frame, another 0.1% dissociates (including some of the newly-formed CH4), and the slow march toward equilibrium continues.

This is exactly the rate-limited equilibrium the game wants. The Arrhenius gate enforces it.

## 4. Phase Appearance and Disappearance (the Oscillation Problem)

The hypothetical worry: in frame n, EPM says all atoms should be gas. `vec_n[Liquid] = 0`. In frame n+1, conditions change slightly and EPM should make liquid. But with `vec_n[Liquid] = 0`, `x_j_liquid = n_j / 0` is undefined, and the Newton step can't gradually grow it.

### The recommended fix: don't put condensation in EPM at all

Re-read your `Volume.Solve` loop:

```
SolveReactions(freeElements);  // chemical equilibrium
SolvePhases();                  // phase equilibrium
DeriveQuantities();
```

These are already separate. The cleanest thing you can do is **never have EPM equilibrate liquid and solid species**. EPM equilibrates only gases. `SolvePhases` handles all phase transfers via fugacity.

That means:

- The `viewSpecies` passed to EPM is filtered to `Phase == Gas` only.
- There is one phase normalization residual (`Z_gas - 1 = 0`), not three.
- The Jacobian is `(a + 1) × (a + 1)`, not `(a + 3) × (a + 3)`.
- Liquid and solid species in the FormulaTable view are still tracked, just not solved by EPM. They sit in the locked pool until `SolvePhases` moves moles between (gas, liquid, solid) variants of the same species.

This decouples the two problems. EPM never has to worry about a phase having zero moles, because the only phase EPM touches is "gas", which is always non-empty in any interesting simulation.

### How condensation appears

`SolvePhases` already works the right way for this: it looks at fugacity, picks the lowest-fugacity phase, and moves a fraction of moles into it from the others. If liquid water has 0 mol but gas water is at high pressure, gas water has higher fugacity, so some moles move into liquid. Now liquid water has > 0 mol. Next frame, EPM continues equilibrating only gases, but the liquid is real and growing.

### Tiny seed values

Even with the decoupling, `SolvePhases` divides by 0 if a phase starts at exactly 0 mol. It uses `phases[indexOfSrc].n` in the ratio formula. The fix is to keep a floor:

```csharp
const double MoleFloor = 1e-15;  // effectively zero, but defined

// In SolvePhases:
if (phases[indexOfSrc].n < MoleFloor) phases[indexOfSrc].n = MoleFloor;
```

That gives every (species, phase) combination a stub amount that lets fugacity calculations proceed. The actual game logic treats anything below, say, `1e-9 mol` as "not present" for display purposes.

### What about EPM with multi-phase reactions (the original ambitious plan)?

If you ever want EPM to solve all three phases simultaneously, the STANJAN approach is a phase-activation outer loop:

1. Start with active set = {gas}. Inactive phases are excluded from the Jacobian.
2. Solve EPM for the active set.
3. After convergence, compute the hypothetical `x_j` for inactive species using the current λ. If for any inactive phase `sum_j x_j > 1`, that phase wants to appear: add a small seed (`N_m = 1e-6 mol`) and add the phase to the active set. Re-solve.
4. If during Newton iteration any active `N_m` goes negative, deactivate it and re-solve.

This is what STANJAN does. It is messy and an outer loop. For a first version, **don't.** The decoupled approach above is good enough.

### Concrete: oscillation cannot happen

With the decoupled approach:

- EPM only solves gas chemistry. It cannot say "all atoms should be gas" because liquid and solid species aren't even in its view.
- `SolvePhases` moves a fraction of moles based on fugacity ratios. It's monotonic toward equilibrium, not oscillatory.
- Each phase transfer per frame is at most `(logphi_src - logphi_min) / logphi_min` of the source. That's a smooth rate, not a bang-bang switch.

The cases where you would see oscillation in a naive implementation (gas-condensed transition right at the saturation pressure) are damped by the per-frame fractional update.

## 5. ApplyHeat and Conservation of Energy

The current `ApplyHeat` stub correctly identifies the problem: reactions change U at fixed T (because species change), so after a reaction step you need to re-find T such that U is conserved (or U = U_old + Q_external if heat was added).

### The physical setup

Your box is constant volume, closed system. The first law gives:

```
dU = δQ - δW
W = P dV = 0 (constant volume)
dU = δQ
```

So:

- Externally added heat `Q` becomes `ΔU = Q` directly.
- Internally driven reactions are isolated: total `U` is conserved across the reaction (no heat in or out), but `T` and species composition can change.

This is a **UV-flash**: hold U and V constant, find T and species moles. NASA CEA solves this directly. For a game, operator splitting works:

```
1. Start of frame:        U_initial, T_initial known
2. Apply external heat:   U_target = U_initial + Q_external
3. Isothermal reactions:  hold T = T_initial, solve EPM. Composition changes.
                          U is no longer at U_target.
4. Adjust T:              solve U(T_new, current composition) = U_target for T_new.
5. With new T, repeat reactions (composition changes again, less so this time).
6. Iterate 3-5 until converged.
```

### Step 4: finding T from U

At constant composition, `dU/dT = sum over j of n_j * c_v_j`. For gases `c_v = c_p - R` (Mayer's relation). For incompressible condensed phases, `c_v ≈ c_p`.

Newton step on T:

```csharp
double Cv_total = 0.0;
foreach (var r in Resources)
{
    double c_p = r.SpeciesPhase.HeatCapacityFunction.Getc_p(T);
    double c_v = (r.SpeciesPhase.Phase == Phase.Gas) ? (c_p - Constants.R) : c_p;
    Cv_total += r.n * c_v;
}
double deltaT = (U_target - U_current) / Cv_total;
T += deltaT;
```

Cap `|deltaT|` at, say, 50 K per inner iteration to prevent overshoot. Latent heat across phase transitions can make `Cv_total` momentarily small (almost zero, since H jumps at a transition), which would give an enormous Newton step.

### Where the existing code goes wrong

In `Volume.Solve`:

```csharp
double Ustart = U;
SolveReactions(freeElements);
SolvePhases();
DeriveQuantities();
double Uend = U;
ApplyHeat(Ustart - Uend);  // Conservation of energy
```

The intent is right: the reaction changed U by `Uend - Ustart`, so to conserve energy you need to "give back" `Ustart - Uend` to the system. But `ApplyHeat` as written has no body. Fill it in like this:

```csharp
private void ApplyHeat(double deltaU)
{
    // Energy that must be absorbed by changing T at the current composition
    double U_target = U + deltaU;

    const int MaxInnerSteps = 10;
    const double Tolerance = 1.0; // J
    for (int step = 0; step < MaxInnerSteps; step++)
    {
        DeriveQuantities();  // recompute U at current T
        double error = U_target - U;
        if (Math.Abs(error) < Tolerance) break;

        double Cv_total = 0.0;
        foreach (var r in Resources)
        {
            double c_p = r.SpeciesPhase.HeatCapacityFunction.Getc_p(T);
            double c_v = (r.SpeciesPhase.Phase == Phase.Gas) ? (c_p - Constants.R) : c_p;
            Cv_total += r.n * c_v;
        }
        if (Cv_total < 1e-6) break;  // phase transition region, abort

        double deltaT = error / Cv_total;
        // Damp
        if (Math.Abs(deltaT) > 50.0) deltaT = Math.Sign(deltaT) * 50.0;
        T += deltaT;
    }
}
```

After `ApplyHeat`, T has changed. That means the reaction is no longer at equilibrium for the new T. The outer loop in `Solve` should re-run `SolveReactions` and `SolvePhases` at the new T. Your existing `for (uint step = 0; step < Constants.MaxSteps; step++)` already does this. Good.

### The convergence criterion

The loop break condition is `Math.Abs(Mass - MassEntry) / MassEntry < Constants.ConservationOfMassTolerance`. Mass should be exactly conserved by construction (the EPM enforces element balance, and `SolvePhases` just moves moles around), so this should converge in 1-2 iterations as long as your EPM is itself converging. If it does *not* converge, that's a sign EPM is broken, not that the outer loop needs more iterations.

A better break condition is `|ΔT| < epsilon AND |ΔU| < epsilon AND EPM converged`. Mass conservation is too easy a target to be informative.

### External heat input

For an external pump or a player action that adds Q joules:

```csharp
public void AddHeat(double Q)
{
    ApplyHeat(Q);  // T rises, then next Solve() call will re-equilibrate reactions
}
```

You don't need to call `Solve()` immediately; the next frame's `Solve()` call will rebalance everything. This is the same amortization that lets EPM converge across frames.

### Latent heat / phase transitions: leave it to the phase solver

The dangerous case is when energy is added to a system at exactly the boiling point. Naive `ΔT = ΔU / Cv` gives the wrong T because the energy should go into vaporization, not into kinetic motion.

But: `SolvePhases` runs every frame, so if `ApplyHeat` overshoots T past the saturation point, `SolvePhases` will then see "gas should be more, liquid less" and move moles. The next `ApplyHeat` call will see a different `Cv_total` because there's more gas now. Over a few frames, T settles back at the boiling point while liquid gradually evaporates.

This is sloppy compared to a proper VLE flash, but it's stable and converges. For a first version, accept that boiling will take many frames in game time. That's not even wrong: real boiling does take time.

### Recap of the order of operations

For one frame of `Volume.Solve`:

```
1. Save MassEntry, UEntry
2. Dissociate()             → freeElements
3. Loop up to MaxSteps:
   a. Ustart = U
   b. SolveReactions(freeElements)   → species composition changes at fixed T
   c. SolvePhases()                  → moles move between (gas, liquid, solid)
   d. DeriveQuantities()             → recompute U
   e. ApplyHeat(Ustart - U)          → adjust T so total U is conserved
   f. Update P from gas volume
   g. break if mass conservation and T change are both within tolerance
4. Sanity check: mass and U should be close to MassEntry and UEntry
```

External heat or work would be added once, before step 3, as a one-shot `U_target` shift.
