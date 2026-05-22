# Implementation & Performance Analysis

The `Volume.Solve()` pipeline and all supporting code.

---

## 1. The Solve Stack — What Runs Per Frame

```
Volume.Solve()
  ├─ Dissociate()                        O(R)    once
  ├─ RebuildIndexes()                    O(R)    once
  ├─ DeriveQuantities()                  O(R · EOS)  once
  │   └─ per resource: Getv(T,P), GetU(T,v), GetS(T)
  ├─ SolveReactions()                    see §2  once
  ├─ SolveUT()                           O(R · EOS)  up to 20 times
  │   └─ DeriveQuantities + ComputeCvTotal
  │       └─ per resource: Getv(T,P), GetCv(T,v)
  ├─ SolveVP()                           O(R · EOS)  up to 20 times
  │   └─ DeriveQuantities
  │       └─ per resource: Getv(T,P), GetU(T,v)
  ├─ RebuildIndexes()                    O(R)    once
  └─ SolvePhases()                       O(P · R)  once
      └─ per species-phase: Getv, GetLogphi, GetP
```

`R` = number of resources in this Volume, `P` = number of phases per species (usually 1-3), `EOS` = cost of a single EOS evaluation.

In the worst case (20 UT steps, 20 VP steps), each resource's EOS is evaluated ~40 times for `Getv` and ~40 times for `GetU`/`GetCv`, plus ~3 times from the three explicit `DeriveQuantities` calls.

### Where the time goes

Each EOS evaluation for a resource calls:

```
Getv(T, P)
  └─ GetZRoots(T, P)           — SolveCubicRealRoots (if cubic) or algebraic (if ideal/incompressible)
     ├─ GetA(T, P)              — Getalpha for SRK/PR
     │   └─ Getm, Getalpha      — polynomial + sqrt + pow
     └─ trigonometric branch    — Acos, Cos, Cbrt (3 root case)
       or algebraic branch      — Cbrt + Sqrt (1 root case)

GetU(T, v)
  └─ GetUIdeal(T, v)            — HeatCapacityFunction.GetH(T) + R*T
  └─ GetUDeparture(T, v)        — log, sqrt (per sub-class)

GetCv(T, v)
  └─ HeatCapacityFunction.Getc_p(T)
  └─ DeriveUDepartureByT(T, v)  — two GetUDeparture calls (finite difference)
```

**The dominant costs are:**

1. **Heat capacity polynomial evaluations** — NASA9 `GetH(T)` computes 9-term polynomial every call. Each `Getc_p`, `GetH`, `GetS` calls `Getvec_a` (array scan for temperature range) followed by the polynomial.
2. **Cubic root solving** — `SolveCubicRealRoots` with trigonometric functions when 3 real roots exist (below T_c for gases). This is called once per `Getv(T, P)`.
3. **EPM matrix assembly** — per species per Newton step: `Getmu` (which itself calls `GetH`, `GetS`, `Getv`, `GetLogphi`), then `view.TransposeThisAndMultiply(vec_lambda).PointwiseExp()`.

---

## 2. SolveReactions — Element Potential Method

```
for reactionStep in 0..MaxReactionSteps (20):
    1. Compute vec_mu[j] = Getmu(T, P, x_j=1) for all s species  O(s · EOS)
    2. vec_x = exp(-vec_mu/(RT) + view^T @ vec_lambda)            O(a·s + s)
    3. vec_n[j] = vec_N[phase] * vec_x[j]                          O(s)
    4. Build X_phase[j, m] = x_j if phase j = m                    O(s)
    5. Build vec_p[i] from freeElements                            O(a)
    6. vec_H = view @ vec_n - vec_p                                O(a·s)
    7. vec_Z = X_phase.ColumnSums()                                O(s·p)
    8. Build J (a+p × a+p):
       a. Q = view × diag(vec_n) × view^T                          O(a²·s)
       b. D = view @ X_phase                                        O(a·s·p)
    9. delta_x = J.Solve(-vec_F)                                    O((a+p)³)
   10. vec_lambda += delta_x[0:a]; vec_N += delta_x[a:a+p]         O(a+p)
   11. Clamp vec_N[m] >= N_mMin
   12. Early exit if ||vec_H||∞ < H_iTolerance ∧ ||vec_Z||∞ < Z_mTolerance

After loop:
    Recompute vec_mu, vec_x, vec_n
    Apply n_j to resources (create or +=)
    Bookkeep freeElements
```

Dimensions: `a` = #elements (max ~10 for typical combustion), `p` = 3 phases, `s` = #species (depends on full loading, potential thousands).

### What dominates

- **Step 1** (`Getmu` loop over all species): this is the single most expensive step because it runs `s` times. Each call involves `GetH`, `GetS`, `Getv`, `GetLogphi`. At 500 species and 10 Newton steps, that's 5000 Getmu calls per frame. With full thermo.inp (~2000 species), it's 20,000 calls.

- **Step 8a** (`Q = view × diag(vec_n) × view^T`): The current implementation does `Enumerable.Repeat(vec_n, a)` to broadcast the vector into a matrix, then `PointwiseMultiply`. This creates a temporary dense `(a × s)` matrix just for element-wise multiplication. Alternative: write `for (int j = 0; j < s; j++) { double w = vec_n[j]; for each i,k in nonzero n_ij, n_kj }` — exploits sparsity of the element-species matrix (most entries are zero).

- **Step 9** (`J.Solve`): `O((a+p)³)` with `a+p ≤ 13` is trivial (~2200 FLOPs). The linear solve is never the bottleneck.

### Irreducible costs

The `Getmu` loop over all species in each Newton iteration is irreducible — the chemical potential depends non-linearly on T and P through the EOS and heat capacity functions, so there's no closed-form shortcut.

---

## 3. EOS Call Costs — Scaling by Type

| EOS Type | `Getv` | `GetU`/`GetCv` | `GetLogphi` |
|----------|--------|----------------|-------------|
| IdealGas | Algebraic `RT/P` | 1x `GetH` | returns 0 |
| Incompressible | returns constant | 1x `GetH` | returns 0 |
| vdW/RK | `SolveCubicRealRoots` + `GetA` + `GetB` | `GetH` + log/div | log/div |
| SRK | `SolveCubicRealRoots` + `Getalpha`(sqrt, pow) | `GetH` + `dαT/dT` + log | log/div |
| PR | `SolveCubicRealRoots` + `Getalpha`(sqrt, pow) | `GetH` + `dαT/dT` + log + √2 | log/√2/div |

The cubic EOS adds ~10-100x cost over ideal gas, primarily from:
- `SolveCubicRealRoots`: 3 `Cbrt`, 1 `Sqrt`, potentially 3 `Cos`, 1 `Acos`
- `Getalpha`: 1 `Sqrt`, 1 `Pow`
- `GetUDeparture`: 1 `Log`

Currently all species use `IdealGas` or `Incompressible`, so these costs are not incurred.

---

## 4. Optimization Opportunities

### Heat capacity functions

**Cache `Getvec_a` result by T range.** Currently `Getvec_a` scans `TemperatureBoundaries` with a while loop on every call. For sequential calls at the same T (or within the same range), this is wasted work. A simple check `if (T >= lastLower && T <= lastUpper) return cachedCoeffs` would eliminate the scan.

**Precompute T², T³, T⁴.** The `Getc_p`, `GetH`, and `GetS` each compute powers of T independently. Computing `T2, T3, T4` once and passing them through saves up to 9 multiplications per call.

**Combine GetH and GetS.** They share the same polynomial structure. Computing both in one function that returns `(H, S)` would halve the polynomial evaluations.

### EOS evaluations

**Cache Z roots by species + (T, P) per frame.** In `DeriveQuantities`, the same species-phase at the same T and P is evaluated multiple times (once for GetU, once for Getv in the volume calculation). A per-frame cache `Dictionary<(SpeciesPhase, T_bin, P_bin), ZRoots>` would eliminate redundant cubic solves.

Example: if `DeriveQuantities` computes `GetU(T, v)` and `Getv(T, P)` for the same resource consecutively, `Getv` calls `GetZRoots`, then `GetU` calls `GetLogphi` which also needs a `v` but doesn't recompute `GetZRoots` directly (it takes `v` as parameter). However, `ComputeCvTotal` does call both `Getv` and `GetCv` for each resource. This is where a frame-level cache would help most.

### EPM matrix assembly

**Exploit sparsity in `view`.** Most `n_ij = 0` (hydrogen doesn't appear in carbon dioxide, etc.). The `Q = view · diag(vec_n) · view^T` product can be computed with a sparse loop:
```csharp
// Instead of PointwiseMultiply with full broadcast matrix:
for (int j = 0; j < s; j++) {
    double w = vec_n[j];
    if (w < n_jMin) continue;
    for (int i = 0; i < a; i++) {
        double n_ij = view[i, j];
        if (n_ij == 0) continue;
        for (int k = 0; k < a; k++) {
            double n_kj = view[k, j];
            if (n_kj == 0) continue;
            Q[i, k] += n_ij * n_kj * w;
        }
    }
}
```
This drops from `O(a²·s)` to `O(a² · nonzero_j)` where `nonzero_j` is the number of species with non-trivial stoichiometry. For typical organic chemistry (C, H, O, N elements), each species has ~3-5 non-zero entries, giving a 10-20x speedup over the dense approach.

**Avoid `Enumerable.Repeat` broadcast.** `Matrix<double>.Build.DenseOfRows(Enumerable.Repeat(vec_n, a))` allocates `a·s` doubles and copies the vector `a` times. The above sparse loop avoids this entirely.

### SolveUT / SolveVP

**Reduce DeriveQuantities calls.** Both Newton loops call `DeriveQuantities` at each step. Most of the time is spent in `Getv(T, P)` and `GetU(T, v)`. Since T and P change slowly, the EOS evaluations are the bottleneck. A per-frame Z-root cache (mentioned above) would reduce this to the polynomial evaluations.

**Early exit on small composition changes.** If `SolveReactions` converges in 1 iteration (no reaction), there's little to solve in `SolveUT` and `SolveVP`. Checking `|UError|` before entering the loop would skip unnecessary work.

### Parallel execution

With the `FormulaTable.viewCache` lock added (`Species.cs:247`), `Parallel.ForEach` on `Volume.Solve()` is safe:
- All static data is read-only after `Initialize()`
- Each `Volume` owns its own `Resources`, `freeElements`, `T`, `P`, `UTarget`
- `viewCache` inserts are under lock

The only caveat is `MathNet.Numerics` matrix object allocation in `SolveReactions` — check that `Matrix.Build.Dense` and `J.Solve()` don't use internal static mutable state. Current MathNet docs indicate `DenseMatrix` solves are reentrant.

---

## 5. What is Irreducible

1. **Getmu loop over all species** in `SolveReactions` — the non-linearity of `μ(T, P, x)` means there's no formula that computes all μ at once without per-species evaluation.

2. **Newton iterations** — the system is non-linear in T, P, and composition. Each iteration requires re-evaluating thermodynamic properties at the new state.

3. **Polynomial evaluations in heat capacity functions** — 5-9 terms evaluated at `T, T², T³, T⁴` for NASA7/NASA9. This is 5-9 multiplications and additions — already near minimum. Precomputing powers of T is the only optimization.

4. **Cubic root solving** — for cubic EOS below the critical temperature, the cubic `Z³ + c₂Z² + c₁Z + c₀ = 0` has 3 real roots. The trigonometric method with `Acos`/`Cos` is necessary. Above T_c, only 1 root exists and the algebraic method is fast.

5. **Matrix solve** `J.Solve(-vec_F)` — the `(a+p) × (a+p)` system is tiny (max ~13×13). The LU decomposition cost is negligible relative to the `O(s)` loop that builds the matrix.

---

## 6. Scaling Estimate

With `a = 8` elements, `p = 3` phases, `s = 500` species, a single `Volume.Solve()` call per frame:

| Operation | Calls per frame | Cost per call | Total |
|-----------|----------------|---------------|-------|
| Getmu (EPM loop) | 500 × 10 = 5,000 | ~2 µs | ~10 ms |
| Getv (DeriveQuantities × 43) | 500 × 43 = 21,500 | ~0.1 µs (ideal) | ~2 ms |
| GetU/GetCv | 500 × 43 = 21,500 | ~0.5 µs | ~10 ms |
| GetS | 500 × 3 = 1,500 | ~0.3 µs | negligible |
| Matrix ops (J, Q, D) | ~10 | ~10 µs | negligible |
| Cubic root solving | 0 (not wired) | ~0 | 0 |

**Estimated ~22 ms per Volume on one core at 500 species**, dominated by `Getmu`. This is well under 16.7 ms (60 FPS) for a single Volume.

At full thermo.inp loading (~1600 species for the subset that matters): ~70 ms per Volume, requiring either fewer species, fewer Newton steps, or parallelism.

Reducing `MaxReactionSteps` from 20 to 10 halves the dominant cost. The solver typically converges in 5-10 iterations for well-conditioned systems. `MaxUTSteps` and `MaxVPSteps` can similarly be reduced since T and P change slowly between frames.
