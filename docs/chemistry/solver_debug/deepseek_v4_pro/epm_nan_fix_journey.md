# EPM Solver NaN Fix — Journey and Remaining Issues

This doc explains the bugs found, fixes attempted, and what the next model should focus on.
It covers `Volume.cs` lines ~180–510, `BoxSim.cs`, and `Constants.cs`.

## Original State

`Volume.SolveReactions()` uses the Element Potential Method (EPM) with a dual-problem
Newton solver (`docs/chemistry/dual_problem/`, `docs/chemistry/solver/`).
The system is:

```
x_j         = exp(-μⱼ/RT + Σᵢ λᵢ · nᵢⱼ)      unnormalized mole fraction
n_j         = Nₘ · x_j                          moles of species j
Hᵢ(λ,N)     = Σⱼ nᵢⱼ · nⱼ - pᵢ                  element balance residual
Zₘ(λ,N)     = Σ_{j∈m} xⱼ                        phase normalization

Unknowns:  λ (a elements) + N (3 phases)
Jacobian:  [ Q   D  ]
           [ Dᵀ  0  ]    — saddle-point system
```

Species: C(gas), C(solid), CH₄(gas), CO₂(gas), H₂(gas), H₂O(gas),
H₂O(liquid), H₂O(solid), O₂(gas).  All gas uses IdealGasEquation,
all condensed use IncompressiblePhaseEquation (v=0).

Initial state: CH₄ 200 mol + O₂ 100 mol in 1 m³ box at T=293.15 K, P=1 bar.
Spark toggled on → species dissociate at 0.1%/frame.

## Bugs Found and Fixed

### 1. `C_v` accumulation (`Volume.cs` line ~70)
`DeriveQuantities` used `C_v += ...` without first setting `C_v = 0`.
Fixed by adding `C_v = 0.0` at the top.

### 2. Zero Jacobian → NaN (`Volume.cs` line ~200–224)
`vec_N` was initialized as `[0,0,0]` in the Volume constructor and never
updated before `SolveReactions`. With no free elements (spark off),
`vec_n[j] = 0 * x_j = 0`, making the Jacobian entirely zero → singular → NaN.

Fixed by:
- Initializing `vec_N[m]` from actual resource amounts
- Early-returning when `freeElements` has no significant entries

### 3. Hardcoded UTarget (`BoxSim.cs` line ~296)
`_volume.UTarget = 1e8` instead of `_volume.U`. Fixed.

### 4. `vec_lambda` dimension mismatch (`Volume.cs` line ~166)
When `Dissociate()` destroys all Resources, `RebuildIndexes()` finds 0
existing elements, sizes `vec_lambda` to 0. But the view includes all 118
elements (bitmask=0 fallback). `view.TransposeThisAndMultiply(vec_lambda)`
fails with 118×9 vs 0×1.

Fixed by: including `freeElements.Keys` in `existingElements`, and a
defensive `vec_lambda` resize in `SolveReactions`.

### 5. λ initialization (`Volume.cs` line ~247–303)

#### 5a. Zero initialization problem
`vec_lambda = [0,0,0]` gave `x_j = exp(-μⱼ/RT)` which spans ~190 orders
of magnitude (5e-121 to 2.6e80). This made the Jacobian impossibly
ill-conditioned.

#### 5b. Pseudo-inverse attempt (wrong)
Computing `λ = pinv(view^T) * (μ/RT)` in least-squares weighted all species
equally, including C(gas) with μ=+675 kJ/mol. Result: extreme λ values
still giving x_j spanning >100 orders of magnitude.

#### 5c. Linearly-independent basis attempt (matrix transposed wrong)
Select the `a` species with most negative μ whose element vectors are
linearly independent, solve the a×a system for x_j=1. The matrix was built
as `A[element, species]` but solved as `A * λ = b` (treating λ as
species-indexed). Fixed to `A[species, element]`.

#### 5d. Basis attempt (correct transpose)
λ = [-14.65, 7.58, -96.38] for basis {CO₂, H₂O(L), CH₄}.
- Basis species: x=1 ✓
- C(solid): x = exp(0.69 + 7.58) = 3905  ← **THE PROBLEM**
- H₂O(s): x = 0.83
- H₂O(g): x = 0.003
- H₂(g): x = 1.7e-7

The basis doesn't "span" C(solid) — its λ_C term comes from CO₂+CH₄,
and the same λ_C makes C(solid) explode. With only 3 λ values, we can't
independently control x_C(s) while keeping x_CO₂=1 and x_CH₄=1.

### 6. `vec_N` scale (`Volume.cs` line ~222–239)
When all resources are dissociated, `vec_N` was initialized from Resources
(empty) → clamped to `N_mMin = 1e-6`. But freeElements total = 1200 mol.
The solver can't pack 1200 mol of atoms into 3e-6 mol of phases.

Fixed by: `vec_N[m] = max(resourceAmount, freeElementTotal/3)`.

### 7. Newton step damping (`Volume.cs` line ~430–460)
The raw Newton step could change λ by 10⁵ or N by 10⁶ in one iteration.
Added damping:
- λ: max step 100 (absolute)
- N: max step max(N×2, freeElementTotal)
- N bound guard: prevents N → negative

### 8. LU → SVD solve (`Volume.cs` line ~404)
`J.Solve(-F)` uses LU decomposition, which amplifies numerical noise
in near-singular saddle-point systems. Changed to `J.Svd().Solve(-F)`
(truncates small singular values). This is what NASA CEA and Cantera use.

### 9. Phase suppression (`Volume.cs` line ~305–320)
After λ init, suppress N_m for any phase whose dominant species has
x_j > 100. Prevents the solid phase (x_C(s)=3905) from dominating the
Jacobian with a 1.56e6 mol phantom entry.

### 10. MaxReactionSteps → 50 (`Constants.cs` line ~155)
Increased from 20 to give the damped Newton solver breathing room.

## Remaining Problem: The Solid Phase Phantom

Despite all fixes, the result is consistently `C(solid): 100 mol,
H₂O(solid): 474 mol` — 474 mol H₂O on every run, regardless of
suppression or damping changes.

The iteration diagnostics reveal the root:
```
iter 0: damp=0.000, |H|=1.6e3,    |Z-1|=3.9e3, N=[1e-6, 1200,  0.2]
iter 1: damp=0.000, |H|=1.6e3,    |Z-1|=3.9e3, N=[400,   400,  1e-6]
iter 2: damp=1.000, |H|=1.5e5,    |Z-1|=3.9e3, N=[397,   199,  0.03]
```

**`|Z-1|` never changes from 3.9e3.** This is the solid-phase normalization
residual: x_C(s) + x_H₂O(s) - 1 ≈ 3905 + 0.83 - 1 = 3904. It's constant
because x_j = exp(-μ/RT + Σλ·n) depends on λ, not N, and λ barely moves
(λ_C stays at 7.58 through all iterations).

**Why λ doesn't move:** The SVD is giving δλ ≈ 0 and putting all the
correction into δN. The phase normalization residual (3.9e3) creates a
large Dᵀ·δλ term, but the Schur complement from the saddle-point
structure (`Dᵀ·Q⁻¹·D · δN = ...`) routes the correction through N
instead of λ. This is fundamental to saddle-point systems.

**Why damping = 0.000:** After N[gas] clamps to 1e-6 (iter 0), the
element balance needs 200+ mol C/H/O through a 1e-6-mol phase.
The raw Newton step δN[gas] is enormous (millions), dampFactor explodes
past 2000, damping → 0. The solver stalls.

**Why 474 mol H₂O:** This is the equilibrium of the gas-phase species
alone (ignoring solid normalization). With 200C, 800H, 200O and only
gas-phase mixing:
- CO₂: takes 100C + 200O → O exhausted
- CH₄: takes remaining 100C + 400H
- H₂O: takes remaining 400H → but O exhausted, so...
- Actually the numbers don't cleanly add up to 474, suggesting the
  solver is finding a different local minimum that violates element
  balance.

## What the Next Model Should Consider

### Option A: Use the STANJAN/CEA approach directly
The dual problem formulation is correct on paper but the saddle-point
Jacobian `[Q D; Dᵀ 0]` is notoriously difficult. STANJAN uses:
- **Sequential phase selection**: start with only the gas phase, then
  add condensed phases one at a time via stability analysis
- **Pure condensed phases**: each pure solid/liquid is its own phase
  with trivial normalization (one species → sum xⱼ = 1 always)
- This eliminates the Dᵀ block for pure phases, leaving only multi-
  species phases in the saddle-point

### Option B: Log-space variables
Instead of solving for N_m and x_j separately, work with ln(n_j) or
ln(N_m). This naturally bounds variables away from zero and prevents the
zero-Jacobian scenario. Cantera uses this approach.

### Option C: Weighted λ initialization
The current basis gives λ_C = 7.58 making C(s) = 3905. A better λ would
balance all species. Use weighted pseudo-inverse with weights ∝ 1/|μⱼ|
(penalize species far from equilibrium).

Or manually set λ_C ≈ 0 (C solid should have x ≈ 1, not 3905), then
solve the remaining 2 λ from other basis species.

### Option D: Trust-region / Levenberg-Marquardt
Replace damped Newton with a trust-region method that guarantees descent
of |F|². This handles singular/near-singular Jacobians naturally.

### Option E: Direct Gibbs minimization
Skip the EPM dual formulation entirely. Use a numerical optimizer
(e.g., MathNet's BFGS, or conjugate gradient) to directly minimize
G = Σ nⱼ·μⱼ subject to element balance constraints. Slower but simpler
and more robust.

### Option F: Fix the λ initialization properly
The most targeted fix: use a 4-species basis {CO₂, CH₄, H₂O(L), C(s)}
with pseudo-inverse. This gives a least-squares λ that doesn't favor
any single species. With all xⱼ ≈ O(1), the Jacobian stays well-
conditioned and the solver converges in a few iterations.

## Files Modified
- `DSA/Chemistry/Volume.cs`: Solve(), SolveReactions() — ~100 lines changed
- `Data/Constants.cs`: MaxReactionSteps 20→50
- `Scripts/BoxSim.cs`: UTarget fix

## Test Procedure
1. Fresh Volume (clear + re-add CH₄ 200 mol, O₂ 100 mol)
2. Turn spark on, click Step
3. Observe diagnostic print from SolveReactions
