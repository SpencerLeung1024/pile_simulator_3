# First Solver Questions

These notes answer the remaining implementation questions for the first chemistry solver.

Read in this order:

1. `linear_algebra.md`: which .NET package to use, and where matrix objects help.
2. `dissociation_pool.md`: how to adapt the Element Potential Method to Pile Simulator 3's per-frame dissociation rule.
3. `phase_moles.md`: how gas, liquid, and solid phase mole totals should appear, disappear, and avoid oscillation.
4. `energy_conservation.md`: how `ApplyHeat` should work in a rigid volume.

The short version:

1. Import `MathNet.Numerics` and use `MathNet.Numerics.LinearAlgebra` for the Newton linear solve.
2. Use matrix/vector objects for `J`, `F`, and `delta`; do not expect them to remove every loop in `SolveReactions`.
3. Treat dissociated moles as a reaction pool. Locked, non-dissociated moles are not part of the reaction element inventory.
4. Only active phases have meaningful `N_m` and mole fractions. Absent phases are handled by an outer active-set loop, not by setting `N_m = 0` inside Newton.
5. `ApplyHeat` should solve for a new `T` whose recomputed total internal energy equals the target energy. In a rigid closed volume, heat changes `U`; reactions and phase changes redistribute `U` and therefore change `T`.
