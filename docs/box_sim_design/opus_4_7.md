# You're rediscovering Gibbs free energy minimization

What you're describing is exactly the algorithm used by NASA's CEA code, Cantera, FactSage, etc. — pick the composition $\{n_i\}$ and phase assignments that minimize the total Gibbs energy

$$G_\text{total}(T,P) \;=\; \sum_i n_i\,\mu_i(T,P)$$

subject to atom conservation. The reason this works is that you don't have to know reactions or pathways at all — you just need $\mu_i(T,P)$ for every candidate species (including each polymorph as its own entry).

## The minimum data per species per phase

There's a de facto standard for this: the **NASA 7‑coefficient polynomial** (also Shomate, also CHEMKIN). Each species/phase entry needs:

| Field | Symbol | Why |
|---|---|---|
| Formula (atom counts) | e.g. `{C:1, O:2}` | Stoichiometry / atom balance |
| Phase tag | s / l / g | Which $G$ formula to use |
| Reference enthalpy of formation | $\Delta H_f^\circ(298.15\text{ K})$ | Sets the energy zero relative to elements |
| Reference entropy | $S^\circ(298.15\text{ K})$ | Drives temperature-dependent preferences |
| Heat capacity vs. $T$ | $C_p(T)$, usually 7 polynomial coeffs | Lets you integrate $H$ and $S$ to any $T$ |
| Molar volume (condensed only) | $v_m$ or $\rho(T)$ | Gives $pV$ work and $\partial G/\partial p$ |
| EOS params (gas, optional) | VdW $a,b$ or just "ideal" | Non-ideal gas behavior |
| Validity range | $T_\text{min},T_\text{max}$ | Refuse to extrapolate |

From these, for any $(T,P)$:

$$H_i(T) = \Delta H_f^\circ + \int_{298}^{T}\! C_p\,dT, \qquad S_i(T) = S^\circ + \int_{298}^{T}\! \frac{C_p}{T}\,dT$$

$$\mu_i(T,P) = H_i(T) - T\,S_i(T) + \underbrace{\int_{P^\circ}^{P}\! v_m\, dP}_{\text{condensed}} \;\;\text{or}\;\; \underbrace{RT\ln\!\frac{p_i}{P^\circ}}_{\text{ideal gas}}$$

**Polymorphs are just separate entries.** Diamond, graphite, and amorphous carbon each get their own row with their own $\Delta H_f^\circ$, $S^\circ$, $C_p$, $v_m$. The minimizer picks whichever has the lowest $\mu$ at the current $(T,P)$. Same for ice‑Ih vs. ice‑VI, or white/red/black phosphorus. **You never have to encode crystal structure** — it's implicit in the thermodynamic numbers.

## Why your "positive $\Delta H_f$ won't exist" worry is wrong

At $T=0$, yes — only lowest-enthalpy species survive. But at finite $T$, the relevant quantity is $\mu = H - TS$. Species like NO ($\Delta H_f^\circ = +90$ kJ/mol) exist at high $T$ because:

1. Forming them from $\tfrac12 N_2 + \tfrac12 O_2$ raises entropy of the *system* (more particle types, more configurations).
2. Equilibrium is always a *distribution* — at any finite $T$ every species exists in some concentration $\propto e^{-\mu_i/RT}$.

So your N₂O₄ ⇌ 2 NO₂ example will work correctly as long as both species are in the database with their real numbers. Same for NH₃ formation from N₂+H₂ — it's favored at low $T$ and high $P$ (lower entropy state) and disfavored at high $T$. This will fall out for free.

## Where pure equilibrium fails: kinetics

> *"All diamond will spontaneously turn into graphite, and solid C + H₂ form CH₄ at standard conditions."*

Right — these are the cases where pure $G$-minimization gives the wrong answer compared to reality, because the *path* is blocked. Two clean ways to handle this in a toy model:

<details>
<summary><b>Option A — frozen reactions (cheapest)</b></summary>

Mark certain conversions as "kinetically frozen below $T_\text{ignite}$." Concretely: each *reaction* in your reaction list gets an activation temperature. If $T < T_\text{act}$, that reaction is skipped this step. This is what Stationeers effectively does. Crude but tunable; lets diamond persist and prevents the $C + 2H_2 \to CH_4$ collapse at room temperature.
</details>

<details>
<summary><b>Option B — Arrhenius kinetics (more realistic)</b></summary>

Each reaction $r$ has a rate

$$k_r(T) = A_r\, e^{-E_{a,r}/RT}$$

and per timestep you advance the extent of reaction $\xi_r$ by $k_r\,\Delta t$ toward equilibrium (clamped to not overshoot). This naturally gives ignition curves, slow oxidation, etc. It costs you two extra numbers per reaction ($A$, $E_a$) but you don't need to enumerate equilibria yourself — equilibrium is the fixed point of the rate equations.

The price: you need a reaction list, not just species. If you don't want to maintain one, you can auto‑generate "elementary" reactions from species pairs (decomposition, recombination, atom swap) and use generic prefactors.
</details>

For a toy: use **A** for "metastability" cases (diamond, organic synthesis at room T) and pure equilibrium for the rest.

## On your iterative annealing scheme

Your idea — "liberate a fraction of atoms, reform compounds in order of decreasing stability, with shrinking step size" — is essentially **simulated annealing on the Gibbs surface**, which is reasonable for a toy. Two refinements that will help convergence:

1. **Liberate proportionally to $e^{-E_\text{bond}/RT}$** rather than a fixed fraction. That's just Arrhenius applied to dissociation, and it gives you "more freezing of state at low $T$" naturally — which is what you want for metastability.
2. **Don't loop "most negative $\Delta H_f$ first."** Sort by $\mu$ at the *current* $(T,P)$, not by reference enthalpy. Otherwise NH₃ wins over N₂+H₂ at 3000 K, which is wrong.

Better still: write it as gradient descent on $G_\text{total}$ in composition space, using your shrinking step as the learning rate. Identical in spirit, more robust.

## Your immiscibility assumption

For pure species clumps in condensed phases, $\mu_i = G_{m,i}(T,P)$ — no mixing term. For ideal gas mixtures,

$$\mu_i^{(g)} = G_{m,i}^\circ(T) + RT\ln\!\left(\frac{p_i}{P^\circ}\right), \qquad p_i = x_i P$$

That's all you need. You explicitly *do* need the partial-pressure log term for gases, even though they "don't interact" — it's the entropy of mixing, and it's what makes vapor pressure work (a liquid evaporates into a vacuum but stops when $p_i$ reaches saturation).

## Concrete data sources

- **NIST WebBook** — Shomate coefficients for hundreds of species, all phases, free.
- **NASA Glenn thermodynamic database** (Burcat's tables) — 7‑coefficient polynomials for ~3000 species including ions, radicals, polymorphs. Plain text. Free.
- **JANAF tables** — gold‑standard reference values.

Grab maybe 30 species (H, H₂, O, O₂, OH, H₂O(s,l,g), N, N₂, NO, NO₂, N₂O₄, NH₃, C(graphite), C(diamond), CO, CO₂, CH₄, …) and you'll already see all the behaviors on your wishlist.

## Minimum viable schema

```yaml
- name: water_liquid
  formula: {H: 2, O: 1}
  phase: l
  H_f_298: -285.83  # kJ/mol
  S_298:    69.95   # J/(mol K)
  Cp_coeffs: [...]  # 7 NASA coeffs or Shomate
  v_m: 1.807e-5     # m^3/mol
  T_range: [273, 647]

- name: water_gas
  formula: {H: 2, O: 1}
  phase: g
  H_f_298: -241.83
  S_298:   188.84
  Cp_coeffs: [...]
  T_range: [200, 6000]
  # ideal gas; no v_m needed

- name: water_ice_Ih
  formula: {H: 2, O: 1}
  phase: s
  H_f_298: -292.72   # extrapolated; only valid below 273
  S_298:    41.0
  Cp_coeffs: [...]
  v_m: 1.963e-5      # bigger than liquid → ice floats, falls out automatically
  T_range: [0, 273]
```

That's the whole skeleton. Add reactions only if you want kinetics; otherwise the minimizer figures out the chemistry from $\Delta H_f$, $S^\circ$, $C_p$, and atom balance.
