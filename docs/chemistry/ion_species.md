# Ion Species

## What are they?

An ion is a molecule or atom that has gained or lost electrons:

- **Cation** (positive ion): lost one or more electrons. e.g. H2O+ has 1 fewer electron than H2O. C+ has lost an electron.
- **Anion** (negative ion): gained one or more electrons. e.g. C- has an extra electron. OH- has an extra electron.

In chemical formulas, an ion has the same atomic nuclei as its neutral parent. The element formula is identical (H2O+ still has 2 H and 1 O), but the electron count differs.

Ions carry net charge. There is no stable macroscopic collection of pure H2O+ the way you can have a jar of H2O. Individual H2O+ ions repel each other strongly. They only exist as a small minority mixed among neutral species, or in a plasma where free electrons balance the charge.

In any real system, total charge is conserved. If H2O+ appears, an electron (e-) or an anion must appear elsewhere.

## How does thermo.inp represent them?

Neutral H2O (line 5755):
```
H   2.00O   1.00  ... Hf =  -241,826 J/mol
```

H2O+ cation (line 5763):
```
H   2.00O   1.00E  -1.00  ... Hf =  +981,602 J/mol
```

CO2+ cation (line 2712):
```
C   1.00O   2.00E  -1.00  ... Hf =  +944,688 J/mol
```

The differences from the neutral parent:

| Species | Hf (kJ/mol) | Electron count |
|---------|-------------|----------------|
| H2O     | -241.8     | neutral |
| H2O+    | +981.6     | -1e (cation) |
| CO2     | -393.5     | neutral |
| CO2+    | +944.7     | -1e (cation) |
| C (g)   | 0*         | neutral |
| C+      | +1809.4    | -1e (cation) |
| C-      | +588.3     | +1e (anion) |
| O2      | 0†         | neutral |
| O+      | +1561.8    | -1e (cation) |
| O-      | +102.5     | +1e (anion) |

*Reference element, Hf defined as 0 by convention.
†Reference element.

**Key pattern**: Ion Hf values are massively endothermic compared to their neutral parents. The extra ~1000-1800 kJ/mol is the ionization energy the molecule had to absorb to lose (or, in the case of anions, gain) an electron.

## Why do they exist if Hf is so high?

A high Hf means the species is thermodynamically disfavored at low temperature. At 300 K, the equilibrium concentration of H2O+ in steam is effectively zero.

But Hf is only part of the story. The Gibbs free energy G = H - TS determines equilibrium. At high temperature:

- The T*S term grows. Entropy favors dissociation/ionization because one molecule splitting into two particles increases the number of possible arrangements.
- At combustion temperatures (2000-4000 K), ions appear in non-negligible concentrations. Rocket exhaust in vacuum nozzles hits these conditions.
- At arc/plasma temperatures (10,000+ K), ionization is the dominant species. Air turns into N+, O+, and free electrons.

NASA CEA includes ion species specifically because real rocket and jet engine exhaust contains them. They affect heat capacity, sonic velocity, and nozzle performance at high temperature. They also matter for electrical conductivity in re-entry plasma sheaths (radio blackout).

In contrast, NIST Chemistry WebBook focuses on 298 K standard-state data and does not tabulate ions. The ions in thermo.inp are there for the aerospace combustion community.

## Are they 'unstable'?

In the thermodynamic sense: yes, at room temperature. The equilibrium lies overwhelmingly toward neutral species. An isolated H2O+ will eventually capture a free electron (recombination) and become neutral H2O, releasing ~1223 kJ/mol as heat or radiation.

In the kinetic sense: it depends. An ion drifting through cold neutral gas will recombine quickly. In a hot plasma or flame where free electrons are abundant and everything is colliding at high energy, ions are continually created and destroyed in a steady state.

So: an individual H2O+ molecule is inherently metastable and will recombine when it finds an electron. But a system can sustain a small ion population as long as energy is being pumped in (heat, electric fields, UV radiation).

## Relevance to Pile Simulator 3

Your initial H2/H2O/CH4/CO2/O2/C subset contains no ions, and that is likely correct for most gameplay purposes. Ions become relevant at:

- **Engine exhaust**: If you model the combustion chamber and nozzle expansion, temperatures reach 3000-4000 K and ions like H2O+, CO2+, OH-, O+, H+ start appearing.
- **Plasma processing/electric arc furnace**: If you add an arc furnace device, the plasma column is >10,000 K and most species are ionized.
- **Atmospheric entry**: Friction heating produces an ionized sheath.

For room-temperature chemistry in a box (mixing tanks, electrolysis, Fischer-Tropsch, etc.), you can ignore ions entirely. The equilibrium solver will automatically predict near-zero concentrations of them at low T anyway.

If you ever do want them, adding `"H2O+"` and `"CO2+"` to the subset in `AllSpecies.Initialize()` (and `"e-"` back into the loader) will pull them from thermo.inp.
