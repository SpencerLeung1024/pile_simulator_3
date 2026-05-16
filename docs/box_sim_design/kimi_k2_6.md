Your instinct to treat each phase as a separate “pseudo-species” and let the simulation pick the cheapest one is exactly right—that is the foundation of modern multi-phase chemical equilibrium.  
The trick is that the “cheapest” measure is not enthalpy, and certainly not the **standard** enthalpy at 298 K. At constant pressure the relevant currency is the **Gibbs free energy** $G = H - TS$ (or at fixed volume the **Helmholtz energy** $A = U - TS$). Sorting by Δ$H_{\mathrm f}^\circ$ will miss every entropy-driven effect you listed:

- **$2\,\mathrm{NO_2} \rightleftharpoons \mathrm{N_2O_4}$**: Low $T$ favors $\mathrm{N_2O_4}$ despite fewer molecules because the reaction is exothermic, but high $T$ kills it because $-T\Delta S$ dominates.
- **$\mathrm{N_2} + 3\,\mathrm{H_2} \rightleftharpoons 2\,\mathrm{NH_3}$**: Even though ammonia has a negative Δ$H_{\mathrm f}^\circ$, its formation destroys gas moles ($\Delta n_{\rm gas} = -2$). At high $T$ the entropy of the four reactant moles wins.
- **$\mathrm{H_2O} \rightleftharpoons \mathrm{H_2} + \tfrac12\mathrm{O_2}$** and further atomization: The enthalpies of formation of atomic H and O are large and *positive*, but at thousands of kelvin the $T\Delta S$ of liberating extra gas particles overwhelms the bond cost.

So you need enough data to compute **$G(T)$ for every phase of every species**, plus a way to turn that into **chemical potential** $\mu_i$ at the current box conditions. Below is the minimal, practical data checklist.

---

### What to store for each chemical species

Because you are assuming **immiscible condensed phases** and **ideal gases**, each species can be treated independently except for the element-conservation constraints and the shared gas volume.

```python
species = {
    "name": "water",
    "formula": {"H": 2, "O": 1},
    "molar_mass": 18.015,          # g/mol; optional if you compute from atoms

    # Critical point: above this there is no liquid/gas distinction
    "critical": {"Tc": 647.1,      # K
                 "Pc": 220.64e5}, # Pa

    "phases": {
        "solid": {
            "density": 917.0,      # kg/m³  (or a constant molar volume)
            "thermo": {
                "type": "nasa7",   # or "shomate", "constant_cp"
                "T_min": 50,
                "T_max": 273.15,
                # Coefficients that let you compute H°(T), S°(T), G°(T)
                "coeffs": [...]
            }
        },
        "liquid": {
            "density": 997.0,
            "thermo": {
                "type": "shomate",
                "T_min": 273.15,
                "T_max": 373.15,
                "coeffs": [...]
            }
        },
        "gas": {
            # gas has no fixed density; it uses the ideal-gas law
            "thermo": {
                "type": "nasa7",   # NASA polynomials are excellent for 200–6000 K
                "T_min": 200,
                "T_max": 6000,
                "coeffs": [...]
            }
        }
    }
}
```

#### The four essential data blocks

| # | Datum | Why you need it |
|---|-------|-----------------|
| **1** | **Elemental formula** | Enforces conservation of atoms. You cannot create NH₃ without bookkeeping N and H. |
| **2** | **$\Delta_{\mathrm f}H^\circ(298\ \mathrm K)$ and $S^\circ(298\ \mathrm K)$** (or absolute $H^\circ$ and $S^\circ$) | Anchor points to integrate to other temperatures. |
| **3** | **$C_p(T)$ model** (NASA 7-coefficient, Shomate, or even a constant $C_p$ for a crude toy) | Lets you compute $H(T)$ and $S(T)$ away from 298 K:<br>$$H(T) = H_{298} + \int_{298}^T C_p\,dT$$<br>$$S(T) = S_{298} + \int_{298}^T \frac{C_p}{T}\,dT$$ |
| **4** | **Molar volume $V_{\rm m}$ (or density) for every condensed phase** | (a) Displaces volume in your fixed box: $V_{\rm gas} = V_{\rm box} - \sum n_i V_{{\rm m},i}$.<br>(b) Provides the small Poynting pressure correction for condensed-phase chemical potentials. |

#### Where to get this data cheaply
- **Gases over wide $T$ ranges:** The NASA polynomial database (used by Cantera, e.g., the “Burcat” or “thermodynamic” datasets). Each species is ~14 numbers and covers 200–6000 K.
- **Solids and liquids:** NIST Chemistry WebBook provides Shomate equations for many species.
- **Atom/radical species (H, O, OH, etc.):** Also available in the NASA combustion database. You need these if you want thermal dissociation to appear organically.

---

### How the data is used in the loop

Because your box has a rigid wall, the natural conserved quantity is the **total internal energy** $U_{\rm tot}$. The simulation is a nested iteration:

#### Outer loop: temperature
Given a candidate composition, find the $T$ that satisfies
$$\boxed{U_{\rm tot} = \sum_{\text{all species,all phases}} n_i\,U_i(T)}$$
where $U_i \approx H_i(T) - P V_i$. For an ideal gas that $PV$ term is $RT$; for condensed phases it is negligible. You can pre-tabulate $U_i(T)$ from the same polynomials.

#### Inner loop: composition and phase split at fixed $T$ and $V_{\rm box}$

1. **Compute standard-state Gibbs energies** $G_i^\circ(T) = H_i^\circ(T) - T S_i^\circ(T)$ for every phase of every species.

2. **Compute chemical potentials $\mu$ under current conditions.**
   - For an **ideal gas** in the mixture:
     $$\mu_i^{\rm gas}(T,P_i) = G_{i,{\rm gas}}^\circ(T) + RT\ln\!\left(\frac{P_i}{P^\circ}\right)$$
     where $P_i = \dfrac{n_{i,{\rm gas}}RT}{V_{\rm gas}}$ and $V_{\rm gas} = V_{\rm box} - V_{\rm cond}$.
   - For a **pure condensed phase** (solid or liquid):
     $$\mu_i^{\rm cond}(T,P) \approx G_{i,{\rm cond}}^\circ(T) + V_{{\rm m},i}(P-P^\circ)$$
     The pressure correction is tiny unless you are at hundreds of atmospheres; you can neglect it for a first toy.

3. **Decide the stable phase for each species.**  
   The stable phase is simply the one with the *lowest* $\mu_i$. If $T > T_c$, delete the liquid branch so the species lives only as a “supercritical fluid” (just use the gas polynomial).

4. **Decide how much of each species exists (chemical equilibrium).**  
   Because the condensed phases are immiscible and the gas is ideal, you have two clean rules:
   - **Condensation:** If species $i$ has any condensed phase stable, it will remain in that phase until its gas partial pressure would try to rise above its saturation vapor pressure $P_{\rm sat}(T)$. For an immiscible species, $P_{\rm sat}(T)$ is obtained from the condition $\mu_i^{\rm gas}(T,P_{\rm sat}) = \mu_i^{\rm cond}(T,P_{\rm sat})$. In practice, if you already computed the $G^\circ$ curves, you can solve for $P_{\rm sat}$ on the fly or pre-fit an Antoine-style curve.
   - **Gas-phase reactions:** All gas species share the same $V_{\rm gas}$. The equilibrium is found by minimizing the total Gibbs/Helmholtz energy subject to element balance. You do **not** need to hardcode “CO burns to CO₂”. Instead, if you define the species set `{C(s), CO, CO₂, O₂}`, the minimizer will automatically choose the mix that gives the lowest total $\sum n_k \mu_k$.  
   
   A simple iterative way to do this *without* a heavy optimizer is:
   - Write independent reactions (e.g., formation reactions from the elements).
   - Compute $\Delta G_{\rm rxn} = \sum \nu_k \mu_k$ for each using the *current* partial pressures.
   - Step the most negative-$\Delta G$ reaction forward by a small amount, recalc all $\mu_k$ (because partial pressures changed), and repeat. This is a steepest-descent walk downhill on the free-energy surface. It will converge if your steps are small and you re-evaluate after every move.

5. **Energy bookkeeping.** When a small reaction step releases enthalpy, that energy stays in the box. Adjust the thermal budget, solve the outer $U$-balance again for a new $T$, and repeat until $T$ and composition stop changing.

---

### Why your original “positive ΔH means it won’t form” intuition breaks

Suppose you have atomic hydrogen in your database. Its **standard enthalpy of formation** is
$$\Delta_{\mathrm f}H^\circ(\text H) \approx +218\ \text{kJ mol}^{-1}$$
—very positive. If you sort by $\Delta_{\mathrm f}H^\circ$, hydrogen atoms will never be built from H₂. But their standard *entropy* of formation is also large and positive (more particles, more disorder). The Gibbs energy of formation becomes
$$\Delta_{\mathrm f}G^\circ(\text H,T) \approx 218\,000 - T \cdot \Delta S^\circ$$
Above roughly 4000 K it crosses zero, and at the enormous dilutions typical of high-$T$ dissociation, the $RT\ln(P_i)$ term pushes it even lower. The result: your equilibrium solver naturally populates H and O atoms at high temperature, giving you the dissociation behavior you want.

The same logic gives you:
- **CO vs CO₂:** If oxygen is scarce, the equilibrium minimizes $G$ by distributing O among C atoms as CO rather than converting a subset all the way to CO₂. Add more O₂ and CO is oxidized further. No special-case “pollutant” rule needed.
- **NH₃ vs N₂ + H₂:** Because $\mathrm{N_2} + 3\mathrm{H_2} \rightarrow 2\mathrm{NH_3}$ consumes four gas moles to make two, low pressure or high temperature swings the balance back to the elements even though ammonia is “more stable” enthalpically.

---

### Activation energy and metastability

You asked about modeling activation energy so that, for example, diamond does not instantly flip to graphite. The data above are purely **equilibrium** data. In an equilibrium world, the lowest-$\mu$ phase wins immediately, so diamond at 300 K is impossible.

If you want metastable phases (diamond, supercooled water, lingering CO instead of CO₂), you have two practical choices for a toy model:

| Approach | What you do | Cost |
|----------|-------------|------|
| **True equilibrium** | Include only the thermodynamically stable solid (graphite). Diamond simply is not in the database. | Zero extra code. |
| **Kinetic overlay** | Keep both diamond and graphite. Define a rate for interconversion (e.g., Arrhenius $k \propto e^{-E_a/RT}$). Only allow phase changes if the kinetic step fires. | You now need rate constants and a time-stepped kinetic layer on top of the thermo code. |

For a first pass, I strongly recommend option 1. Get the equilibrium solver working with the thermodynamic data list above. Once you can see CO/CO₂/ steam/ice/NH₃/NO₂/N₂O₄ emerge organically from the numbers, then you can bolt on kinetic gates if you really want long-lived diamonds.

### Bottom-line shopping list

For every species (including elements and radicals) that you want in your box, tabulate:

1. **Element formula** and molar mass.
2. **For each phase you model (solid / liquid / gas):**
   - A thermal model (`Cp(T)`, $\Delta_{\mathrm f}H^\circ_{298}$, $S^\circ_{298}$) that gives you $G^\circ(T)$.
   - Molar volume / density (for condensed phases).
3. **Critical constants** ($T_c$, $P_c$) to turn off the liquid branch above the critical point.
4. **An element-conservation ledger** so the solver knows that consuming 1 CO₂ removes 1 C and 2 O from the available pool.

That is all the physics you need. The “rules” you want—incomplete combustion, Haber-Bosch equilibrium, thermal dissociation, triple-point behavior—are emergent consequences of minimizing $\sum n_i \mu_i$ with that data.
