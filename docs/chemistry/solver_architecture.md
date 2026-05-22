# What Volume.Solve() does

After looking at and synthesizing `docs/chemistry/solver`:

```csharp
public class Constants
{
    public const double DissociationThreshold = 1e-3; // fraction per frame, 0.1%

    public const uint MaxReactionSteps = 20;
    public const double N_mMin = 1e-6; // mol, N_m for phase m is clamped to be > this after every Newton step
    public const double H_iTolerance = 1e-6; // mol element i unused, an early exit condition for SolveReactions
    public const double Z_mTolerance = 1e-6; // mole fraction of phase m away from 1, an early exit condition for SolveReactions
    public const double n_jMin = 1e-6; // mol, a species will not be realized if its calculated amount is < this
    // Instead, the elements it would have consumed are returned to freeElements
    // For comparison, Stationeers uses 0.001 mmol as the deletion threshold, below which a species (and any mass it embodies) vanishes

    public const uint MaxUTSteps = 20;
    public const double UTolerance = 1e-6; // (J actual - J target) / J target, an early exit condition for SolveUT

    public const uint MaxVPSteps = 20;
    public const double VTolerance = 1e-6; // (m^3 actual - m^3 target) / m^3 target, an early exit condition for SolveVP
}
```

```csharp
public class Volume : Inventory<SpeciesPhaseResource>
{
    // Resources: the species in phases in this volume, and their amounts in mol
    // Volume: m^3
    public double T; // K
    public double P; // Pa
    public double UTarget; // J, conserved when guessing T
    
    // Derived quantities:
    // Mass: kg
    // UsedVolume: m^3, slighly different meaning in Volume: the volume taken up by condensed phases only
    // FreeVolume: m^3, has a getter: Volume - UsedVolume
    public double U; // J, internal energy of the system
    public double S; // J / K, entropy of the system
    // H = U + PV, enthalpy of the system
    // G = H - TS, Gibbs free energy of the system
    // Publicing U and S allows the BoxSim UI to calculate and show thermodynamic variables

    // Internal solver variables:
    private Dictionary<SpeciesPhase, SpeciesPhaseResource> speciesPhaseToResource;
    private Dictionary<Species, List<SpeciesPhaseResource>> speciesToResources;
    private Dictionary<Element, double> freeElements; // Can be negative
    private ulong bitmask;
    private Vector<double> vec_lambda;
    private Vector<double> vec_N = Vector<double>.Build.Dense(3); // [N_gas, N_liquid, N_solid] (enum Phase order)
    private Vector<double> vec_V = Vector<double>.Build.Dense(3); // [V_gas, V_liquid, V_solid]

    private void DeriveQuantities()
    {
        // Mass, vec_V, UsedVolume, U, S
    }
    private void Dissociate()
    {
        // Check after dissociation Arrhenius equation if remaining amount < Constants.n_jMin
        // If true, wipe out that species phase
    }
    private void RebuildIndexes() // Not BuildIndexes like the static classes because Volume.Resources changes every frame
    {
        // speciesToResources, speciesPhaseToResource, bitmask
        // Invalidates vec_lambda if bitmask changed
    }
    private void SolveReactions(); // Using SpeciesPhase.Getmu(T, P, x_j = 1.0) (no mixing term code path)
    private void SolveUT(); // At constant U, guess T that conserves U, calls DeriveQuantities at each step
    private void SolveVP(); // At constant V, guess P that conserves V, calls DeriveQuantities at each step
    private void SolvePhases();

    private void Solve():
    {
        Dissociate();
        RebuildIndexes();
        SolveReactions();
        SolveUT();
        SolveVP();
        RebuildIndexes();
        SolvePhases();
    }
}
```

## Key Features
- MathNet.Numerics
- Stability over correctness. Each frame's correctness can be sacrificed (N_mMin floor, negative freeElements, etc.) to stabilize the overall system over time
- I really want 3 phases. You get fewer instabilities if you only work with gas but there are many interesting chemical reactions that involve condensed phases above the (in-game) DissociationTemperature (and able to react), but still have the condensed phase as most stable
- - 2 C(s) + 2 O2(g) -> 2 CO(g)
- - Fe2O3(s) + 3 CO(g) -> Fe(l) + 3 CO2(g)
- - The player should have the choice between building a BlastFurnace : Device, or do reduction by CO in a Volume. The latter may be faster or use less overall power
- - I don't simulate aqueous phases so you can't do electro-deposition of Cu2+. Oh well
- freeElements is preserved between frames. It is defined as element amounts that have entered the Volume that are not bound in its Resources. This should conserve mass, with only floating point error accumulating
- Instead of doing 20 x (1 reactionStep, 1 phaseStep, 1 applyHeatStep, 1 solvePStep), I now do 20 reactionstep, 20 UTStep, 20 VPStep, 1 phaseStep (I guess I could loop this if I really wanted fast phase equalization)
- - The old interleaved method would cause bits and pieces of resources (mass) to be gained and lost haphazardly
- - Volume.Solve doesn't do an outer loop anymore so all four solve steps are finding solutions to different states, but hopefully it stabilizes over many frames
