# What Volume.Solve() does

After looking at and synthesizing `docs/chemistry/solver`:

```csharp
public class Constants
{
    public const double DissociationThreshold = 1e-3; // fraction per frame, 0.1%
    public const uint MaxReactionSteps = 20;
    public const uint MaxUTSteps = 20;
    public const uint MaxVPSteps = 20;
    
    public const double N_mMin = 1e-6; // mol, N_m for phase m is clamped to be > this after every Newton step
    public const double n_jMin = 1e-6; // mol, a species will not be realized if its calculated amount is < this
    // Instead, the elements it would have consumed are returned to freeElements
    // For comparison, Stationeers uses 0.001 mmol as the deletion threshold, below which a species (and any mass it embodies) vanishes
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
    private double[] vec_lambda;
    private double[] vec_N = new double[3]; // [N_gas, N_liquid, N_solid] (enum Phase order)
    private double[] vec_V = new double[3]; // [V_gas, V_liquid, V_solid]

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

## Key Changes
- MathNet.Numerics
- Stability over correctness. Each frame's correctness can be sacrificed (N_mMin floor, negative freeElements, etc.) to stabilize the overall system over time
- freeElements is preserved between frames. It is defined as element amounts that have entered the Volume that are not bound in its Resources. This should conserve mass, with only floating point error accumulating

TODO...
