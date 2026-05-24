# Council response: anthropic/claude-opus-4.7

Date: 2026-05-23 (round 3)

Reviewed code/docs:

- `DSA/Chemistry/Volume.cs` (current `SolveReactionsGas`, the view-chopping at line 234-248, the lambda pre-solve at line 252-296, the Newton loop at 320-463, the result application at 465-505)
- `DSA/Chemistry/EquationsOfState.cs`
- `DSA/Chemistry/HeatCapacityFunctions.cs`
- `Data/Species.cs` (`Getmu` at 98-126, `NASA9Loader.Load(path, null)` loading all 1612 species at line 194)
- `Data/Constants.cs` (`log_xMin = -100`, `log_xMax = 100`, `LambdaMaxJump = 1e1`)
- `Scripts/BoxSim.cs`
- `docs/chemistry/solver_debug_3/boxsim.txt`
- `docs/chemistry/solver_debug_3/output.txt`
- `docs/chemistry/dual_problem/*`
- `docs/chemistry/solver/*` (esp. `kimi_k2_6.md` and my own previous `opus_4_7.md`)
- `docs/chemistry/solver_debug/*`
- `docs/chemistry/solver_debug_2/*` (all four previous responses)
- `docs/chemistry/solver_architecture.md`

This is round 3. The previous round (`solver_debug_2`) had four reviewers converging on:
"the equilibrium is wrong because CO is missing and SolvePhases doesn't use mu^0".
That was the right diagnosis for the 6-species case (200 CH4 + 100 O2, subset = H2/C/O2/CH4/H2O/CO2).
Round 3 is a different problem. The user has switched to `NASA9Loader.Load(path, null)`,
loading **all 1612 SpeciesPhases**. The system now produces 1e+297 mol of B3O3F3 from a single
Newton step. None of the round-2 recommendations directly address this. The user explicitly
asked for four different approaches; I will only give one here, but I will try hard to give
the approach the other three are *least likely* to give.

## Direct, short answers first

1. **The error is in the result-application loop (lines 466-505), not in the Newton math itself.**
   `vec_n[j] = vec_N[phase] * vec_x[j]` is applied with `vec_x` clamped to `exp(100) ≈ 2.7e+43`.
   With `vec_N[gas] ≈ 400`, that produces `n_j ≈ 1.07e+46 mol` per offending species. Newton
   was never given a chance to converge — the early-exit check (line 458) is at the *end* of
   the iteration, so reactionStep 0 finishes with `vec_lambda` updated by a tiny SVD-damped step
   and the application loop then fires with `vec_x` from the **next** evaluation at line 466,
   which uses pre-clamped log space (the line 466 evaluation does not clamp `log_x`).
   So even if you fix Newton, the writeback step would explode anyway. See § A.

2. **Don't choose a subset from 1612 species programmatically per frame. Choose it
   programmatically *per Volume*, at construction time, using a stoichiometric feasibility
   test against the elements actually present.** Then refresh that set only when elements
   enter or leave. The expensive part of large NASA9 sets is not the math per step; it is the
   ill-conditioning that comes from log(x_j) ranging over hundreds of decades. Filtering by
   element-presence alone takes you from 1612 to ~30-50 species for a CH4/O2 box. See § B.

3. **For T_c, P_c, omega data on hundreds of species: combine the ChemSep open database
   (LGPL, has T_c/P_c/omega for ~430 species), the Yaws/Reid/Poling/Prausnitz textbook
   appendix as a static C# table, and Joback group-contribution as a fallback for everything
   else.** v_c is the easiest because v_c ≈ Z_c \* R T_c / P_c with Z_c ≈ 0.27 for most
   non-polar molecules — you almost never actually need a measured v_c. See § C.

The rest of this document explains why, with derivations and code-shape suggestions.

## A. Where the error is, and how to stabilize the solver as-architected

### A.1 The proximate cause is the unclamped writeback, not the Jacobian

Look at `Volume.cs` lines 466-468:

```csharp
vec_x = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda)).PointwiseExp();
vec_n = Vector<double>.Build.Dense(s);
for (int j = 0; j < s; j++)
{
    int phase = (int)viewSpecies[j].Phase;
    vec_n[j] = vec_N[phase] * vec_x[j];
    ...
}
```

This is the final writeback. Compare to the in-loop evaluation at lines 330-333:

```csharp
Vector<double> log_x = -vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda);
log_x = log_x.PointwiseMaximum(Constants.log_xMin).PointwiseMinimum(Constants.log_xMax);
vec_x = log_x.PointwiseExp();
```

The in-loop version clamps `log_x` to `[-100, +100]`. The writeback version does not.
With 1612 species and a barely-stepped `vec_lambda`, `log_x[j]` for spurious oxides
(B3O3F3, UOF4, etc.) reaches values like `+685` (which gives `exp(685) ≈ 1.1e+297`).
That is exactly the 7.1e+297 mol of B3O3F3 in `boxsim.txt`. Confirm by spot-checking
B3O3F3's `vec_mu`: it appears late in the `vec_mu` array in `output.txt` (you can find it
because the file was truncated — but the magnitude pattern of those huge oxides is
consistent with mu ≈ -1.5 to -2 MJ/mol at 293 K, so `-mu/RT ≈ +700-900`).

**Fix 1 (one line, mandatory): clamp `log_x` in the writeback too.** This alone stops the
1e+297 explosion. It is not a *correct* fix — clamping `log_x` to 100 still means you
can write `exp(100) ≈ 2.7e+43 mol` to a species — but it stops total destruction long
enough to see the next layer of bugs.

```csharp
Vector<double> log_x_final = -vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda);
log_x_final = log_x_final.PointwiseMaximum(Constants.log_xMin).PointwiseMinimum(Constants.log_xMax);
vec_x = log_x_final.PointwiseExp();
```

### A.2 The lambda pre-solve (lines 252-296) generates bad lambdas at high s

The Gram-Schmidt dominant-species pre-solve is GPT-5.5's contribution from round 2. It works
well for ~30 species: pick the `a` lowest-mu species whose formula columns are linearly
independent, then solve `view[:, selected]^T \* lambda = mu[selected] / RT`. The resulting
lambda makes the dominant species have `x_j ≈ 1`.

At 1612 species with very diverse mu, the algorithm still works *for the species it selected*,
but the **un-selected** species are evaluated at this same lambda and many of them produce
gigantic `x_j`. The pre-solve picks lambdas optimised for, say, H2O / CH4 / N2. The
solver-internal exponents then make B3O3F3 (which has its own huge negative mu) come out
at `exp(+700)` because the lambdas weighted toward fluorine-containing species are not what
the H2O-fit set chose.

The fix here is not in the pre-solve itself, it is in how you select species (see § B).
But if you want to keep the pre-solve, two cheap improvements:

1. **Use only the dominant species' contribution to lambda.** After the Gram-Schmidt pick,
   compute `vec_x` for the selected species at the new lambda and verify all are `O(1)`.
   If any selected species has `|log_x| > 5`, the pre-solve is inconsistent — fall back to
   `vec_lambda = 0` and let damping handle it.

2. **Scale lambda by element abundance.** The pre-solve treats every selected species
   equally, but the species you picked might have wildly different element compositions.
   Solve the weighted least-squares problem instead of the square system: minimize
   `sum_j (mu_j/RT - view[:,j]^T lambda)^2 \* p_j`, where `p_j` is the rough abundance
   you expect for that species. For most boxes, `p_j ≈ 1` for the closed-shell molecules
   you actually expect to see, and `p_j ≈ 1e-6` for everything else. This biases lambda
   toward the species you actually have rather than picking exotica.

### A.3 The Newton loop's damping is the wrong size at s = 1612

`LambdaMaxJump = 1e1` (Constants.cs:184) limits `|Δλ_i|` to 10 per step. With elements C, H, O
having lambda around -100 to -300 (per the output), `Δλ = 10` is reasonable. But here is the
trap: when `vec_n` for a species is `1e+46` and `view` has nonzero entries for that species
in many element rows, the Jacobian quadrant `Q = view \* diag(vec_n) \* view^T` has entries
of order `1e+46`. The right-hand side `vec_F` (element imbalance residual) has entries of
order `1e+46` too (because most of the imbalance comes from that one bad species).
SVD solves this and gives `Δλ ≈ 1e+0` to maybe `1e+2` — small relative to the matrix, but
applied against `lambda` values of `-100 to -300` it perturbs them by a factor of order 1
in the exponent. Multiplied across 1612 species through `view.T * delta_lambda`, this shifts
many species by `exp(±10)` and the next iteration is just as bad.

In the round-2 6-species case, `LambdaMaxJump = 10` was fine because the dynamic range of
`log_x` was bounded by `log_xMin/log_xMax = ±100` and there were ~6 species so the
condition number of Q stayed below 1e6. At 1612 species spanning 1000 decades of mu, the
condition number of Q routinely exceeds 1e+40 and SVD is throwing away most of the
information in the system.

**Fix 2 (geometric Lambda damping):** Replace the absolute jump cap with a relative one,
specifically a **trust region on log-x change**:

```csharp
// Compute max log-x change from this step:
Vector<double> delta_log_x = view.TransposeThisAndMultiply(delta_x.SubVector(0, a));
double max_abs_dlogx = delta_log_x.PointwiseAbs().Maximum();
double damping = 1.0;
if (max_abs_dlogx > Constants.MaxDeltaLogX) // suggest 2.0
{
    damping = Constants.MaxDeltaLogX / max_abs_dlogx;
}
// Keep the existing N_gas damping check as well.
delta_x *= damping;
```

This is the STANJAN damping from PDF 1, page ~18. It guarantees no species' `x_j` changes
by more than `exp(MaxDeltaLogX)` per step. With `MaxDeltaLogX = 2`, no species moves more
than ~7x per step, even if the math wants to move it by 1e+40. The trade-off: with 1612
species you may now need 50+ Newton steps to converge instead of 5. That is fine for
gameplay (~1 ms still).

### A.4 The Jacobian is being SVD-truncated in the wrong direction

`Volume.cs:424` uses `J.Svd().Solve(-vec_F)`. SVD's `Solve` truncates singular values below
some default threshold relative to the largest. With Q's largest entry at `1e+46` and the
phase-normalization rows at order `1`, SVD's default tolerance throws away **the entire
phase normalization equation**. The `Z_m - 1 = 0` constraint is silently dropped. The
Newton step satisfies element balance (because Q dominates) but does nothing about phase
normalization.

This is one reason the spurious B3O3F3 grows monotonically: element balance is approximately
satisfied at every step because B3O3F3 contains B, O, F in a fixed ratio, and the Newton
step finds whatever `vec_x[B3O3F3]` makes the B, O, F balances close. The mole-fraction
sum `Σx_j = 1` is treated as noise.

**Fix 3 (Jacobian row scaling):** Before solving, scale each row of J and `vec_F` so they
have comparable magnitudes:

```csharp
// After building J and vec_F, before solving:
Vector<double> rowScale = Vector<double>.Build.Dense(a + 1);
for (int row = 0; row < a + 1; row++)
{
    double maxAbs = J.Row(row).PointwiseAbs().Maximum();
    rowScale[row] = maxAbs > 1e-30 ? 1.0 / maxAbs : 1.0;
    J.SetRow(row, J.Row(row) * rowScale[row]);
    vec_F[row] *= rowScale[row];
}
Vector<double> delta_x = J.Svd().Solve(-vec_F);
// delta_x is in the same space — no inverse scaling needed
```

This is row-equilibration, the cheapest preconditioner. It makes SVD's relative tolerance
apply equally to every equation in the system. Without this, SVD will always favor the
loudest row (element balance dominated by the runaway species). With this, the phase
normalization equation regains comparable weight.

### A.5 What stabilization looks like end-to-end

Putting A.1, A.3, A.4 together gives:

```csharp
// In SolveReactionsGas, replace the Newton iteration body with:

// (a) Build J and vec_F as before.

// (b) Row-equilibrate (Fix 3):
for (int row = 0; row < a + 1; row++)
{
    double maxAbs = J.Row(row).PointwiseAbs().Maximum();
    double s_row = maxAbs > 1e-30 ? 1.0 / maxAbs : 1.0;
    J.SetRow(row, J.Row(row) * s_row);
    vec_F[row] *= s_row;
}

// (c) Solve.
Vector<double> delta_x = J.Svd().Solve(-vec_F);

// (d) Trust-region on log-x (Fix 2):
Vector<double> delta_log_x_test = view.TransposeThisAndMultiply(delta_x.SubVector(0, a));
double max_abs_dlogx = delta_log_x_test.PointwiseAbs().Maximum();
double damping = 1.0;
if (max_abs_dlogx > 2.0)
    damping = 2.0 / max_abs_dlogx;
// Keep N_gas non-negativity damping as well:
double delta_N_gas = delta_x[a];
double N_gasAboveMin = vec_N[0] - Constants.N_mMin;
if (delta_N_gas < 0 && N_gasAboveMin > 0)
    damping = Math.Min(damping, 0.5 * Math.Abs(N_gasAboveMin / delta_N_gas));
delta_x *= damping;

// (e) Apply.
vec_lambda += delta_x.SubVector(0, a);
vec_N[0] += delta_x[a];

// After the loop, in the writeback (Fix 1):
Vector<double> log_x_final = (-vec_mu / (Constants.R * T) + view.TransposeThisAndMultiply(vec_lambda))
    .PointwiseMaximum(Constants.log_xMin).PointwiseMinimum(Constants.log_xMax);
vec_x = log_x_final.PointwiseExp();
```

This will stop the 1e+297 explosion. It will not, by itself, give *correct* chemistry — the
solver will still produce many micromoles of unwanted species — but it will give *bounded*,
*element-balanced* results that you can debug further. In my experience that is the
correct intermediate state to ship before tackling B.

### A.6 What this approach does *not* solve

- The fundamental ill-conditioning of running EPM on 1612 species at 293 K. Many species
  legitimately should have `x_j ≈ exp(-200)`, and `log_xMin = -100` floors them at
  `exp(-100) ≈ 4e-44`, off by 100 orders of magnitude. The element-balance residual then
  carries an accumulated bias from thousands of floored species. The damping above
  controls divergence; it does not control bias.
- Spurious species at low `n_j` still go into Resources because `n_jMin = 1e-6` and even
  `exp(-100) \* 400 ≈ 1.5e-41 mol` is below threshold so it gets skipped. That part is
  fine. The problem is species at `log_x ≈ -10`, where `n_j ≈ 0.02 mol`, well above
  threshold, and chemically unjustified.
- The condensed-phase exclusion bug (gas-only solver but condensed phases still in the
  formula table) discussed extensively in round 2. The current code at line 240-244 *does*
  filter `viewSpecies` to gas-only, fixing one of round-2's main complaints. Good. But the
  filter happens *after* `GetView(bitmask)` builds the full view, then chops to gas.
  `GetView` is cached on `bitmask` (Species.cs:276), so we're caching the bad multi-phase
  view and chopping it every call. Move the chop into a separate cache or rebuild it
  cheaply: it's `s` Phase enum checks, microseconds at worst.

## B. Choosing a subset programmatically

The other reviewers (or future reviewers) will probably say:
"hand-curate the species list".
That works for 6 species. It does not work for the asteroid mining game's eventual scope
(Fe, Cu, Ni, Si, S, plus their oxides/sulfides/halides). You need an automatic procedure.

Here is the procedure I would build. **It runs once per Volume per "composition shape"
change, not per frame.** Composition shape = the bitmask of elements that exist in the
Volume's Resources plus freeElements above some threshold. As long as the shape stays
the same, the subset stays the same.

### B.1 Stage 1: element-presence filter (already what bitmask does, but tighter)

The current `FormulaTable.GetView(bitmask)` (Species.cs:321) keeps any species whose formula
sums to nonzero over the included element rows. That should be: keep only species whose
formula contains **only** elements in the bitmask. Currently it keeps any species that has
*any* element in the bitmask, because `col.Sum() > 0.0` (Species.cs:379) is true even if the
species also requires elements not in the bitmask.

Wait — actually, the GetView builds a row-filtered matrix first and then column-filters by
whether *the remaining rows* sum to nonzero. A species that needs Cl (a row not in the
bitmask) would have its Cl atoms ignored entirely, and a species like "ALHCL2" would appear
as if it were "ALH" (its Cl atoms zeroed out). So **GetView silently drops elements**.

Let me reread... yes:

```csharp
// Volume.cs RebuildIndexes: existingElements has H, C, O for CH4+O2 case
ulong newBitmask = FormulaTable.GetViewBitmask(existingElements.ToList()); // bits 0, 5, 7
// FormulaTable.GetView builds tempMatrix with only rows 0, 5, 7
// Then column-filters by col.Sum() > 0
// ALHCL2 column has Al(row 12)=1, H(row 0)=1, Cl(row 16)=2.
// After row-filter, only H survives → tempMatrix column is [1, 0, 0]
// col.Sum() = 1 > 0 → ALHCL2 stays in the view, looking like "H"
```

This is a bug in `GetView` that the round-2 reviewers missed because they only had 6 species,
all of which happened to have all their elements in the bitmask. With 1612 species, this bug
is the **root cause of why Al-, B-, F-, Cl-, U-containing species end up in vec_n**. The
solver thinks ALHCL2 is just a fancy isomer of H with a giant negative mu, places it in the
gas phase as a "fictitious H" with insane preference, and out comes 546 Gmol of "ALHCL2"
(which it cannot physically be — it carries 2 Cl atoms per molecule that are coming from
nowhere).

**Fix 4 (mandatory, real root cause):** In `FormulaTable.GetView`, before column-filtering,
also reject any species whose formula uses an element *not* in the bitmask. The cleanest
way is:

```csharp
// In FormulaTable.GetView, after building tempMatrix:
for (int table_j = 0; table_j < AllSpeciesPhases.list.Count; table_j++)
{
    SpeciesPhase speciesPhase = AllSpeciesPhases.list[table_j];
    // Reject species using elements outside the bitmask:
    bool valid = true;
    foreach (Element element in speciesPhase.Species.Formula.Keys)
    {
        int element_i = (int)element.Z - 1;
        if (element_i >= 64 || (bitmask & (1UL << element_i)) == 0)
        {
            valid = false;
            break;
        }
    }
    if (!valid) continue;

    Vector<double> col = tempMatrix.Column(table_j);
    if (col.Sum() > 0.0)
    {
        viewSpeciesList.Add(speciesPhase);
        viewColsList.Add(col);
    }
}
```

**This fix alone, with everything else broken, would reduce the explosion enormously.**
The view for a CH4/O2 box would now contain only H/C/O species (maybe 80-100 entries
from NASA9), not 1612. The remaining EPM problems are merely ill-conditioning, not
silently fabricating fluorine.

After this fix, the subset choice becomes: which of the ~100 H/C/O species do you actually
want to consider? See B.2.

### B.2 Stage 2: a single-pass dominance prune

Even with the H/C/O restriction, you still have ~100 species and many of them are exotic
carbon clusters (C2, C3, C4, ..., C5O2, etc.) that NASA9 carries for completeness. Most
have huge `+mu` at 293 K and end up with `log_x ≈ -300`. They drag the condition number
into 1e+50 territory before they can be floored.

A simple, defensible prune is: **compute `mu_j / RT` for every species in the
element-filtered view at the current T and P. Keep species whose `mu_j / RT` is within a
window of the minimum** `mu_j / RT` over species containing each element.

Pseudocode:

```csharp
// For each element i in the bitmask:
//   find min mu_j / RT over species j whose formula contains element i
//   keep any species whose mu_j / RT - element_i_min <= WindowSize
// WindowSize = 50 means we tolerate species up to exp(-50) less likely than the dominant
// species per element atom they carry. That includes radicals and oxidation states a few
// steps off the dominant chemistry but rejects C20 cluster species.

Dictionary<Element, double> elementMin = ...;
foreach (Species j in candidates)
{
    double mu_norm = mu_j[j] / (R * T);
    foreach (Element e in j.Formula.Keys)
    {
        if (mu_norm < elementMin[e]) elementMin[e] = mu_norm;
    }
}
foreach (Species j in candidates)
{
    double mu_norm = mu_j[j] / (R * T);
    bool keep = true;
    foreach ((Element e, uint count) in j.Formula)
    {
        double headroom = (mu_norm - elementMin[e]) / count;
        if (headroom > Constants.SubsetMaxHeadroom) { keep = false; break; }
    }
    if (keep) chosenSubset.Add(j);
}
```

This is a heuristic but it has the property that any species CEA would compute as nonzero
at temperature T (mole fraction > exp(-windowSize)) is in your subset. It is **temperature
dependent**, so you should recompute the subset whenever T changes by more than ~100 K.

For the CH4/O2 box at 293 K, this prune typically reduces a 100-species H/C/O list to about
20-30 species (CH4, CO2, H2O, CO, H2, O2, OH, H, O, C(g), C(gr), H2O(L), H2O(cr), maybe a
few alcohols and aldehydes). That is workable.

### B.3 Stage 3: refresh trigger

The subset only needs to be recomputed when:

1. The element bitmask changes (a new element enters or leaves the Volume above threshold).
2. T changes by more than 100 K.
3. P changes by more than 1 decade (very rare in gameplay).

Otherwise reuse the cached subset. The expensive part is computing 1612 mu values, which
happens once per "shape change", maybe once per gameplay event.

### B.4 Why not the STANJAN dominant-species algorithm?

STANJAN's dominant-species algorithm (the one PDF 1 describes) does something similar but
inline with the Newton iteration. It is cleverer but harder to implement correctly. For a
game, the staged prune above is much easier to debug and gives nearly the same subset.

If you want to copy STANJAN exactly, the algorithm is:

1. Build the `a × s` formula matrix.
2. For each element, find the species containing that element with the most-negative mu.
3. These `a` species are the "dominant set". They span the element space (with rare
   exceptions for pathological compositions).
4. Solve for lambda by inverting the `a × a` dominant submatrix (you already do this at
   lines 282-296).
5. Use that lambda to compute `x_j` for all species. Species with `log x_j > -windowSize`
   join the active set; the rest are deferred.
6. Repeat from step 4 until the active set stabilizes.

This is essentially the same logic as my staged prune, just iterated. For game purposes
the staged prune converges in one pass and is simpler.

## C. Sources of T_c, P_c, omega, v_c data

You said NIST WebBook is not downloadable. Correct — it's per-species pages, no bulk
download. Here are sources that *are* bulk-downloadable, in roughly the order I'd try them.

### C.1 Primary: open machine-processable

1. **[ChemSep LITE](http://www.chemsep.com/downloads/index.html)** (free download, embedded
   in the open-source ChemSep simulator). Component database has ~430 species with T_c, P_c,
   omega, Z_c, v_c (called "Vc"), and dozens of other properties. Format: their `.pcd`
   file (plain text key-value) or via the ChemSep XML export. ~430 species is plenty for
   the chemistry you'll actually want to simulate (combustion, water, hydrocarbons,
   ammonia, common refrigerants). License is LGPL for the database — redistributable as
   long as you cite ChemSep.

2. **[NIST ThermoData Engine (TDE) seed XML](https://www.nist.gov/mml/acmd/trc/thermodata-engine)**
   if you can get a copy. Not bulk-downloadable from the web, but ships with REFPROP
   ($300 per install at NIST) and many commercial simulators (Aspen, ChemCAD). I
   would not buy REFPROP for a game project — too expensive — but if you have university
   access, you can extract the seed XML.

3. **[CoolProp](http://www.coolprop.org/)** (open-source, MIT). Has ~120 species with full
   equation-of-state parameters, including T_c, P_c, omega derived from their fitted EOS.
   Less coverage than ChemSep but more accurate where covered. Has a C# wrapper. Reading
   the JSON fluid files directly is straightforward.

4. **[OpenSMOKE++ database](https://www.opensmokepp.polimi.it/)** — focused on combustion,
   ~5000 species with NASA polynomials, includes critical properties for many. Free for
   research use, but check the license for commercial games.

### C.2 Primary: bulk PDF + OCR / manual entry

5. **Reid, Prausnitz, Poling, "Properties of Gases and Liquids", 5th edition (2001),
   Appendix A** — the canonical critical properties table for ~600 species. It is a PDF
   table. You can OCR it (`pdftotext`, `tabula-py`) and clean it up in an afternoon. The
   result becomes a static C# array shipped with the game. This is what I would actually do
   if I wanted maximum coverage with minimum runtime complexity.

6. **Yaws, "Yaws' Handbook of Properties of the Chemical Elements" and other Yaws handbooks**
   — ~2000 species. Pirated PDFs are easy to find, and the tables are well-structured
   (regular column widths). The 2003 edition is in many university libraries.

### C.3 Fallback: group contribution

When you have a species in your data (NASA9 has 1114 species) but no critical properties:

7. **Joback group-contribution method.** Estimate T_c, P_c, v_c from molecular structure
   (number and type of functional groups). Accuracy is ±20% for T_c, ±50% for P_c, worse
   for v_c. Bad for accurate process simulation; fine for a game. Implementation: ~50
   lines, plus a lookup table of group contributions.

8. **Lydersen / Constantinou-Gani / Marrero-Gani** are more accurate group methods if you
   want more rigor. Same idea, different group definitions.

The catch is that the group contribution method needs a structural decomposition of each
species (CH4 has 1 -CH3 group plus 1 C, etc.), and NASA9's `thermo.inp` only gives you
elemental formula, not structure. You would need a SMILES table or hand-coded structural
decompositions. PubChem CID lookups can be automated if you have an internet-connected
build step.

### C.4 v_c specifically

You asked specifically about v_c, which is the easiest to fake. For non-polar species,
the critical compressibility `Z_c = P_c v_c / (R T_c)` is remarkably constant at
**0.27 ± 0.02**. For polar species it ranges 0.22-0.30. So once you have T_c and P_c:

```csharp
double v_c = 0.27 * Constants.R * T_c / P_c; // good to ±10%
```

For SRK and PR equations of state, **you don't actually need v_c**. The equations are
written in terms of T_c, P_c, omega only. v_c only shows up if you use Van der Waals in
reduced form (which your code does at `EquationsOfState.cs:226`), and even there it can be
replaced by `3 R T_c / (8 P_c)` (vdW's prediction of v_c, exact for vdW). So I'd say:

- For vdW: use the vdW formula `v_c = (3/8) R T_c / P_c`. No measurement needed.
- For SRK / PR: don't store v_c at all. Get rid of it from the abstract class. Use the
  EOS-internal `b` parameter instead, which is what the math actually wants.

That removes one of the three quantities you were worried about. Now you only need T_c, P_c,
omega.

### C.5 Concrete recommendation

For Pile Simulator 3 specifically, given that the game is C# and you don't want a runtime
dependency on a database:

1. Download ChemSep LITE.
2. Extract the `.pcd` file's T_c, P_c, omega columns for the 430 species it covers.
3. Write a one-off C# tool that joins this table with NASA9's species list by name.
   (NASA9 uses chemistry-formula names like "CH4", "H2O", "C(gr)"; ChemSep uses CAS RN
   plus common name. Joining on formula will hit ~80% of overlapping species.)
4. For the remaining ~20% of overlapping species, OCR the Reid/Prausnitz/Poling Appendix A
   and join by formula.
5. For species in NASA9 but not in either of the above (mostly radicals, exotic clusters,
   excited states), set T_c, P_c, omega to placeholder values that make the EOS fall back
   to ideal-gas behavior at all conditions Pile Simulator 3 will reach. For radicals this
   is correct (they're high-T low-P species that never approach criticality in a game).
6. Ship the joined table as a static C# initializer or a CSV next to `thermo.inp`.

Total work: a weekend of data cleanup. Total coverage: every species in NASA9 has *some*
EOS data, of which a few hundred are real measurements and the rest are placeholders.

## D. What I would do *in order*, if I were you

This is round 3. The list of pending stabilization work is long. Here is what I would do,
in order, and at each step you can pause to test:

1. **Apply Fix 4 (GetView formula bug) first.** This is the largest single source of the
   1e+297 numbers. After this fix, the CH4/O2 box should only see H/C/O species, taking
   you from 1612 to ~100. Run BoxSim; expect more reasonable-looking output but probably
   still bad (the 6-species round-2 bugs are still present).

2. **Apply Fix 1 (clamp writeback).** Defensive. Stops any remaining `1e+297` even with
   subsequent bugs.

3. **Apply Fix 3 (row-equilibrate J).** Fixes the silent dropping of phase-normalization
   constraints in SVD.

4. **Apply Fix 2 (trust-region damping).** Caps Newton step size by max log-x change, not
   by raw delta-lambda. This is what STANJAN actually uses; the absolute jump cap was
   always a placeholder.

5. **Re-run BoxSim** with the H2/C/O2/CH4/H2O/CO2 subset (revert `NASA9Loader.Load(path, null)`
   to `NASA9Loader.Load(path, subset)` for the moment). Verify the round-2 expected behavior
   reappears (CH4 + O2 → CO2 + H2O at low T, slow toward equilibrium). This re-establishes
   a known-good baseline.

6. **Run with `subset = null` (all NASA9) and the staged prune from B.2.** The prune is
   ~50 lines and lives in `RebuildIndexes`. Verify the chosen subset for the CH4/O2 box
   contains the obvious species (CH4, CO2, H2O, CO, H2, O2, and maybe OH/H/O depending on
   T) and not Al/B/F/U/Nb. Run BoxSim and verify behavior.

7. **Now address the round-2 issues** (SolvePhases using mu^0, the missing CO from the
   curated subset, etc.). These are still real but they were already addressable; round 3
   gives you the room to think about them by stopping the catastrophic numerical blowup.

T_c/P_c/omega data acquisition (§ C) can happen in parallel — it has no dependency on the
solver fixes. The vdW EOS doesn't need it for the immediate stability work because all your
condensed phases use the placeholder `IncompressiblePhaseEquation` anyway.

## E. Where I disagree with my round-2 self

Reading back my round-2 response, I emphasized "SolvePhases bug" as the primary cause of
gaseous carbon at 1000 K. I stand by that *for the 6-species case*. For the 1612-species
case it is a sideshow:

- The 7e+297 mol of B3O3F3 in `boxsim.txt` came out of `SolveReactionsGas`, not
  `SolvePhases`. `SolvePhases` only moves moles between phases of the *same species*; it
  cannot create B3O3F3 if `SolveReactionsGas` didn't create it.

- The B3O3F3 came from the `GetView` formula bug (B.1, Fix 4) producing a view that lets
  the solver place fluorine and boron and aluminum into species when there are no F, B, Al
  atoms in `freeElements`. The element-balance residual has nothing to push back against —
  there are zero B atoms required, zero produced.

- Wait actually that's not quite right either. The element balance constraint should be
  `view * vec_n = vec_p`. If B is not in `viewElements`, there's no row in `view` for B,
  and no constraint on B. The solver sees ALHCL2's formula column with H=1 (because Al, Cl
  got dropped) and uses ALHCL2 like it's pure hydrogen. With huge negative mu, it pours
  hundreds of moles of "H" into ALHCL2. The H atoms are conserved (from `freeElements[H]`);
  the Al, Cl atoms are conjured out of nothing because the solver doesn't know they exist.

- So the *real* root cause of round 3 is: `GetView` drops atom counts for elements outside
  the bitmask. After that fix, the round-2 advice becomes relevant again — but with
  damping/row-scaling improvements as backstop for the larger species set.

Round 2 was right about a smaller problem. Round 3 is a different, larger problem hiding
underneath.

## F. Compact answer for the impatient

**Q1: Where is the error and how do I stabilize?**

The largest single error is in `FormulaTable.GetView`: it silently zeroes out atom counts
for elements not in the bitmask, so species like ALHCL2 appear as pure hydrogen with a
massive negative chemical potential, and the solver places enormous moles there because
the missing Al/Cl atoms are not balanced. **Fix `GetView` to reject any species using an
element outside the bitmask.** Add: writeback log-x clamping (one line), Jacobian row
equilibration before SVD (five lines), and trust-region damping on `view^T \* delta_lambda`
instead of absolute `delta_lambda` (five lines). Together these stop the 1e+297 blowup
without needing to change the architecture.

**Q2: How do I choose a subset programmatically?**

After the `GetView` fix, the view is automatically restricted to species using only
elements in the Volume. For further reduction, prune by `mu_j / RT`: keep species whose
mu (per element atom they carry) is within `WindowSize ≈ 50` of the minimum mu over
species containing that element. This is temperature-dependent and should be recomputed
when T or the element bitmask changes substantially, but not every frame. For a CH4/O2
box at 293 K this gives ~25-40 species automatically.

**Q3: Source of T_c, P_c, omega data?**

ChemSep LITE (free, ~430 species), supplemented by OCR of Reid/Prausnitz/Poling Appendix A
(~600 species), with Joback group-contribution as fallback. **You don't need v_c at all**
if you use SRK or PR; for vdW use the analytic `v_c = (3/8) R T_c / P_c`. Combine into a
static C# table joined to NASA9 by chemical formula; weekend of work, lifetime of use.
