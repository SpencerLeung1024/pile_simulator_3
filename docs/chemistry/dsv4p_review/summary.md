# Summary of Answers

## Your questions → which file has the answer

| Question | File |
|----------|------|
| Why is U a function of S and v? How should GetU work? | `docs/answers/thermodynamic_fundamentals.md` |
| How to implement NASA7, NASA9, Shomate | `docs/answers/heat_capacity_functions.md` |
| How to implement all EOS (incl. incompressible, vdW, RK, SRK, PR) | `docs/answers/equations_of_state.md` |
| How to calculate saturation pressure (Maxwell construction) | `docs/answers/equations_of_state.md` |
| How to architect Resource and Volume. If I apply heat, how does control flow? | `docs/answers/architecture.md` |
| How to store nuclide mass/energy | `docs/answers/nuclides.md` |

## Key things to fix in your existing code

### 1. `EquationOfState.GetU(float S, float v)` → wrong signature

U depends on T and v, not S and v. Change to `GetU(float T, float v)`. For ideal gas: `U(T) = H(T) - RT`. For condensed phases: `U(T) ≈ H(T)`.

See: `docs/answers/thermodynamic_fundamentals.md`

### 2. `IdealGasEquation.GetU` references undeclared variables

Your current code references `c_p` and `T` but neither is stored in `IdealGasEquation`. The EOS shouldn't need to know $C_p$ — that comes from `HeatCapacityFunction` separately. The EOS only handles the P-T-v relationship.

### 3. `IncompressiblePhaseEquation.GetP` → should return 0 (sentinel)

An incompressible phase doesn't generate pressure. Pressure comes from the gas in the box. `GetP` returning 0 means "my pressure doesn't matter, use the box pressure."

### 4. You need a cubic solver

`VanDerWaalsEquation.GetvRoots` has a TODO. The cubic solver is in `docs/answers/equations_of_state.md`.

### 5. You need fugacity coefficient methods for saturation pressure

The Maxwell construction can be done via fugacity equality instead of area integration. Fugacity formulas for all four cubic EOS are in `docs/answers/equations_of_state.md`.

### 6. `Resource` should be a pure data+derived-properties class

Temperature belongs to Volume, not Resource. Resource just does `amount * molarProperty`.

### 7. `SpeciesPhase` should have the computation methods

`GetChemicalPotential(T, P, p_partial)` is the central function. It lives on `SpeciesPhase`. All other thermodynamics (GetH, GetS, GetMolarVolume, etc.) are also on `SpeciesPhase`.

## Recommended implementation order (what to do next)

1. **Fix the EOS signatures** — remove S parameter, add T parameter
2. **Implement the cubic solver** — `SolveCubicRealRoots` from `equations_of_state.md`
3. **Implement NASA7** — you'll need it for gas species from Burcat/Cantera data
4. **Finish all cubic EOS** — vdW, RK, SRK, PR using the cubic solver
5. **Add `GetChemicalPotential` to `SpeciesPhase`** — the function that makes everything else work
6. **Implement phase change in `Volume`** — saturation pressure, phase equilibration
7. **Implement the Heat→Temperature→Phase→Energy loop** — the full Simulate(dt) method

## The dissociation/recombination loop

You said this comes after phase change. The design docs (opus_4_7, gpt_5_5, etc.) strongly recommend Gibbs free energy minimization rather than the "liberate atoms, form most negative enthalpy first" approach. When you're ready for chemical reactions, those docs have the full answer. The short version: for each reaction $k$, compute $\Delta G = \sum \nu_i \mu_i$, and if $\Delta G < 0$, the reaction proceeds forward. Sort by most negative $\Delta G$.
