# Solver Debug 4: CH4, H2O(cr), and the Pre-Solver

## 1. What the Pre-Solver Does

The pre-solver lives at `Volume.SolveReactionsGas()` lines 277—324. It was written by GPT-5.5 ("I have no idea how this works").

The Newton iteration for the dual problem needs a starting guess for the element potentials `vec_lambda`. A bad guess (e.g., all zeros) causes certain species to have `x_j = exp(-mu/RT)` spanning 190 orders of magnitude, which blows up the Jacobian. The pre-solver tries to find a starting `vec_lambda` where a chosen set of "dominant" species each have `x_j ≈ 1`.

### How it works

1. **Three species are hardcoded** (lines 282—285): CO2, H2O, CH4. Always.
2. For each, Gram-Schmidt orthogonalizes its stoichiometry column against already-selected columns.
3. If the residual norm > 1e-9, the species is accepted. Stops when `selectedSpecies.Count == a` (number of elements).
4. Builds an `a × a` matrix `A_init[i][k] = n_{i, selectedSpecies[k]}` and RHS `b_init[k] = mu_k / RT`.
5. Solves `A_init * lambda = b_init`. This `lambda` makes `-mu/RT + A^T * lambda = 0` for each selected species — meaning `x_j = 1` for them.

### Why it works for {CO2, H2O, CH4}

The stoichiometry columns for {CO2: [C:1,O:2], H2O: [H:2,O:1], CH4: [C:1,H:4]} are linearly independent (det ≈ 6), so the Gram-Schmidt accept all three. The solution gives `lambda = [-13.7, 1.8, -94.5]` at 293 K, which yields `x_CH4 = 1, x_CO2 = 1, x_H2O = 1` — perfect.

### Why it fails if {CO2, H2O, CO} is the basis instead

CO2: [C:1,O:2], H2O: [H:2,O:1], CO: [C:1,O:1]. These are also linearly independent (det ≠ 0). The solver finds `lambda = [-1.9, 48.9, -118.0]` at 293 K. But with these λ values:

```
log_x_CH4 = -(-129235)/(RT) + λ_C*1 + λ_H*4 + λ_O*0 ≈ 53 + (-1.9) + 4*48.9 ≈ 247
x_CH4 = e^247 ≈ 7.8e40  (7.8E+40 mole fraction!)
```

This makes `vec_n_CH4 = N_gas * x_CH4 ≈ 7.8e34 mol` (for N_gas = 1e-6 mol min floor). The element mass residual `vec_H_C = 3.1e35` explodes the Jacobian. Newton produces `delta_x = [-∞, ∞, ∞, NaN]`. SVD can't recover. Solid crash.

The root cause: CH4 and CO have very different `mu` values at 293 K (CH4 ≈ -129 kJ/mol, CO ≈ -169 kJ/mol). The pre-solver forces x_CO = 1 and x_CO2 = 1 and x_H2O = 1, but CH4's `mu` is 40 kJ/mol higher than CO's. The resulting `lambda_C` and `lambda_H` don't match CH4's stoichiometry, so CH4's implied `log_x` is enormous. When CO is in the dominant set instead of CH4, the Newton solver can't damp it away — the first step is already NaN.

**Key insight:** The pre-solver is not a generic algorithm. It works only because of the specific hardcoded species — and only because those three happen to be a good basis for the initial high-CH4 low-T combustion problem. This is fragile: adding CO to the species list without changing the hardcoding causes immediate divergence. Adding other species (like large carbon chain molecules) would similarly break the basis selection.

## 2. Why `vec_specificMu` Didn't Stabilize Things

The comment at lines 264—267 explains the intent:

> Use specific (per mass) mu so large molecules don't automatically win.

When loading all 682 species, the Gram-Schmidt basis selection sorts by ascending `mu` (`Volume.cs` line 281, commented out but it's how the algorithm was originally written). Large polyatomic molecules have very negative formation enthalpies — (CH3COOH)2 at -1,046 kJ/mol, (HCOOH)2 at -914 kJ/mol. They get picked as "dominant" species even though they shouldn't be. The vector `vec_specificMu` was supposed to normalize `mu` by molar mass or total atom count so that small molecules are on equal footing.

### What went wrong

1. **It was never wired in.** At line 281, the sort-by-`vec_specificMu` is commented out. Instead, the three hardcoded species are used. The `vec_specificMu` values only appear in a debug print at line 301, which is also inside the hardcoded loop.

2. **The three normalization schemes have intrinsic problems:**
   - `vec_mu[j]` (standard, unnormalized): large molecules win because they have big negative `H_f°`. Works for the 6-species subset but not for 682 species.
   - `vec_mu[j] / MolarMass` (per kg): penalizes heavy molecules. But formation energy doesn't scale with mass — H2O (18 g/mol, H_f° = -242 kJ/mol) and (CH3COOH)2 (120 g/mol, H_f° = -1046 kJ/mol) have per-mass energies of -13.4 and -8.7 kJ/g. Water would always outcompete acetic acid. The authors noted "does not converge."
   - `vec_mu[j] / Formula.Sum(kv => kv.Value)` (per atom): penalizes large formulas. But CH4 (5 atoms) vs C2H6 (8 atoms) have different chemistry that isn't just an atom-count scaling. Also "does not converge."

3. **The fundamental issue:** The Gram-Schmidt basis selection conflates two concerns: (a) picking a linearly independent set of stoichiometry vectors, and (b) picking species that should actually be abundant. These are not the same. You can pick a basis that's great for linear algebra but gives terrible `lambda` for the remaining species (as with CH4 vs CO above). The real solution is an active-set method that finds which species are present at equilibrium, not a one-shot Gram-Schmidt.

## 3. Why CH4 Specifically?

Methane is the most problematic species in the 7-species H/C/O system for three reasons:

### 3.1 Its `mu` is in an awkward middle position

At 293 K:
| Species | mu (kJ/mol) |
|---------|-------------|
| CO2     | -456        |
| H2O(g)  | -297        |
| CO      | -168        |
| CH4     | -129        |
| O2      | -60         |
| H2      | -38         |
| C(g)    | +677        |

CO2, H2O, and CH4 span the range reasonably and produce a usable λ. But CO2, H2O, and CO span a wider range and produce λ that makes CH4's x explode. If you picked CO2, H2O, and H2 as basis instead, CH4 would similarly explode. CH4's mu of -129 is far from "one end" but close enough to CO (-168) that the two-lambda systems differ significantly.

### 3.2 CH4 vs CO is the actual equilibrium fight

In the actual chemical equilibrium (NASA CEA), the main contest is between CH4 and CO for carbon. Below ~1200 K, CO2 + H2O dominates. Above ~1200 K, CO + H2 dominates. CH4 is an intermediate species whose abundance peaks around 850 K and declines at higher T. The solver oscillates because the equilibrium shifts from CH4-rich (cold) to CO-rich (hot) as the system heats up through SolveUT.

In the `co_pinned_ch4_no_phases.txt` run, the solver goes through 20 Newton steps oscillating around the answer. Step 0: CH4 dominates (x=1), steps 1-5: CH4 x oscillates wildly (484 million at step 1, then settles). By the time SolveUT raises T to 1406 K, CO becomes the dominant carbon carrier (157 mol CO vs 35.8 mol CH4) — exactly the CEA result. But getting there required 19 Newton steps (all 20 allowed), and the trajectory was far from monotonic.

### 3.3 CH4 is the most stable hydrocarbon at low T

With only H, C, and O elements available, CH4 is the only stable hydrocarbon. Any H and C that combine at low T form CH4. At high T, CH4 cracks into CO + 2H2 (steam reforming: CH4 + H2O → CO + 3H2). This means CH4 has two distinct thermodynamic regimes with very different `x_j` values. The dual variable `lambda` that works at 300 K (CH4 ≈ 100%) is very different from the `lambda` that works at 1400 K (CH4 ≈ 6%). The Newton solver struggles across this transition.

In the `co.txt` crash, all this is irrelevant because the initial λ blows up CH4's x before any Newton steps can damp it.

## 4. What's Going On with H2O(cr)?

### 4.1 NASA9 extrapolation at 750 K

The ice phase `H2O(cr)` from thermo.inp has temperature ranges [200, 273.15] K. At 750 K, the `MultiTemperatureFunction.Getvec_a()` at `HeatCapacityFunctions.cs:47` defaults to `AllowOutOfRange = true`, meaning it uses the 200-273 K coefficient set without error.

At 750 K, the NASA9 formula `c_p/R = a0/T² + a1/T + a2 + a3*T + a4*T² + a5*T³ + a6*T⁴` with the ice coefficients produces **nonsense values**. The `a3`, `a4`, `a5`, `a6` terms dominate (T, T², T³, T⁴) but were fitted to the 200-273 K range. Extrapolated 2.75× beyond the upper bound, they produce:
- A `c_p` that is completely wrong (possibly negative or absurdly large)
- An H(T) value that makes ice look thermodynamically favorable compared to gas or liquid water
- An S(T) value that makes ice look like it has high entropy (the opposite of true)

### 4.2 How ice appears in the combustion problem

In the `no_co.txt` output (which enables SolvePhases), at 753 K:
```
H2O(s): 81.9 mol  (solid ice!)
H2O(g): 6.45 mol
```

At 753 K, the equilibrium should be: 100% gas, 0% liquid, 0% solid. Instead, the system has 81.9 mol of ice. This happens because:

1. **SolvePhases sorts phases by chemical potential.** The ice phase, with horribly extrapolated NASA9 coefficients, has a lower `mu` than gas or liquid water. It appears "most stable."
2. **Phase transfer moves moles toward lower mu.** Gas and liquid water both transfer moles *into* ice — the solver is making water freeze at 750 K because ice's extrapolated `H(T)` and `S(T)` look favorable.

### 4.3 Why not liquid water?

Liquid water `H2O(L)` has temperature ranges [273.15, 373.15, 600.0] K. At 750 K, it's 1.25× beyond its upper bound. The extrapolation produces bad but different numbers than ice. In practice, ice wins the mu comparison at 750 K because its polynomials are designed for a lower-T region with lower entropy values, making the `-T*S` term in `G = H - TS` less negative — which perversely makes `mu` more negative. The extrapolated vapor pressure calculation then sees ice as ultra-stable and drives all water into the solid phase.

### 4.4 The water.txt test works fine

In the three-phase water test at 293 K, ice starts at 100 mol and correctly melts into liquid. The solver correctly computes `P_sat ≈ 2.3 kPa` and moves gas → liquid and solid → liquid. By frame ~300, the system is 40.8 mol gas and 259 mol liquid water at 381 K. Ice is gone. This works because 293 K is only 20 K above ice's maximum temperature, so the polynomial extrapolation is tolerable. The mu ordering is correct: liquid < gas < solid at 300 K.

### 4.5 Root cause summary

| Phase     | Valid T range (K) | T in combustion run (K) | Extrapolation factor | Behavior |
|-----------|-------------------|------------------------|----------------------|----------|
| H2O(cr)   | 200—273           | 750—1400              | 2.7—5.1×             | Wins mu comparison, freezes water at high T |
| H2O(L)    | 273—600           | 750—1400              | 1.25—2.3×            | Also wrong but loses to ice |
| H2O(g)    | 200—6000          | 750—1400              | Fully in range       | Correct |

**The fix is NOT to ban ice.** The fix is to enforce temperature ranges from the NASA9 polynomial data. If T > species_max_T, that phase should be skipped in `SolvePhases` or its mu set to +∞ so it can never be the destination. This is a one-line guard: check `T` against `TemperatureBoundaries.Last()` for each phase.

## 5. What's Needed for Exotic High-Pressure Phases

Your current model can correctly handle the three standard phases of water (gas, liquid, ice Ih) as long as temperatures stay within polynomial bounds. But exotic high-pressure phases require fundamentally different physics:

### 5.1 What the current model can't do

| Phenomenon | What's needed | What you have |
|---|---|---|
| Ice polymorphs (II—XVIII) | Phase-specific EOS with density jumps at phase boundaries | One "ice" phase with 0 molar volume |
| Solid CO2 (dry ice) at high P | CO2(s) NASA9 + PR EOS for vapor pressure | CO2 has gas, liquid, solid phases (4-phase actually) |
| Metallic hydrogen | Pressure-driven insulator-metal transition, degenerate electron EOS | N/A — not in thermo.inp at all |
| Supercritical water | Continuous gas-liquid transition above 647 K/22 MPa | Treated as separate gas/liquid phases |
| Silica-like CO2 (CO2-V) | High-P quartz analogue phase, unknown H_f° | N/A |
| Solid-solid phase transitions | Phase diagram data, Clausius-Clapeyron slope | Only mu-based phase sorting |
| High-pressure ice melting | Pressure depresses melting point (ice Ih melts at -20°C at 200 MPa) | ice has v=0, no PV work |

### 5.2 What you'd need to add

**1. Phase diagram data for each species.** You need critical points, triple points, and phase boundary slopes (dP/dT). Without this, you can't determine if T=300 K, P=500 MPa should produce ice VI or liquid water. Sources:
- Pure water: IAPWS-95 (full EOS for all phases, including ice Ih, III, V, VI, VII)
- CO2: Span-Wagner EOS + phase diagram data for CO2-I (dry ice), CO2-II, CO2-III, CO2-V
- H2: Sesame EOS tables for metallic hydrogen transition (~400 GPa)

**2. Real molar volumes for condensed phases.** `IncompressiblePhaseEquation` uses `v = 0.0` for all solids/liquids (Species.cs:253). This means:
- Poynting correction is always `v*(P-P°) ≈ 0` — condensed phase mu is independent of pressure
- Volume accounting is wrong; a liter of ice should occupy ~1 L (18 mL/mol * 55.5 mol/L)
- Density differences between phases (e.g., ice is less dense than liquid water, which is why it floats and why its melting point decreases with pressure) are invisible

Database options: NIST REFPROP/CoolProp, CRC Handbook, or mineral density tables.

**3. Pressure-dependent EOS for condensed phases.** `IncompressiblePhaseEquation` is fine for moderate pressures but breaks when `v*(P-P°)` becomes significant (>1-10 kJ/mol). Ice VI at 1 GPa has ~25% higher density than liquid water — the Poynting term matters. Options:
- Murnaghan isothermal EOS: `P(v) = (K0/K0') * ((v0/v)^K0' - 1)` where K0 is bulk modulus
- Birch-Murnaghan for higher pressures
- Tabulated EOS data (Sesame tables, ANEOS)

**4. Phase-aware fugacity.** Your current `SolvePhases` computes:
```
P_sat = P° * exp((mu_cond - mu_gas) / (RT))
```
This assumes: (a) gas is ideal (`phi=1`), (b) condensed phase mu is independent of pressure (`v=0`). Neither holds at high pressure. The correct formula:
```
P_sat = P° * exp((mu_cond(T,P_sat) - mu_gas(T,P_sat)) / (RT))
```
This is implicit in `P_sat` because mu depends on pressure. It requires an iterative solve (or use fugacity equality directly: `f_gas(T,P) = f_cond(T,P)`).

**5. A phase search algorithm.** Currently, you enumerate all phases of a species and compare mu values. At high pressure, the set of possible phases changes:
- Some phases only exist above threshold pressures (ice VI requires >0.6 GPa)
- Some phases are metastable (ice IV in water's phase diagram)
- The equilibrium phase is the one with lowest Gibbs free energy *per mole*, but some phases may be inaccessible due to nucleation barriers

You'd need something like a "phase selector" that takes (T, P) and returns the single equilibrium phase, rather than trying to co-exist all phases simultaneously. Co-existence should only happen at phase boundaries (Gibbs phase rule: F = C - P + 2).

### 5.3 Realistic roadmap

For a game, you don't need IAPWS-95 fidelity. A reasonable approach:

1. **Fix NASA9 extrapolation first** (guard T bounds). This prevents ice at 750 K.
2. **Add molar volumes** for condensed phases from a simple database.
3. **Add critical constants** (T_c, P_c, omega) for all species. CoolProp is a good source. This enables cubic EOS fugacity for all gases (not just ideal gas).
4. **For special species** (H2O, CO2, H2) that have exotic high-P phases, add phase diagram lookups:
   ```
   (T, P) → {phase_name, density, valid_range}
   ```
   This is a small table for each special species, not a universal solver.
5. **Only then** add pressure-dependent EOS for exotic phases.

Items 1-3 are needed anyway to make the current solver stable. Items 4-5 are gameplay content (ice moons, gas giant interiors, etc.) that can be added incrementally per-species.
