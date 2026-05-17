# Nuclide Mass & Energy Storage

## How to store the mass of a nuclide

Store mass in **daltons (Da)**, which you already have defined as `Constants.Da = 1.66053906892e-27f kg`. This is the natural unit for atomic-scale masses.

Your `Nuclide` class currently has `Z` and `N`. Add:

```csharp
public class Nuclide
{
    public uint Z;       // Number of protons
    public uint N;       // Number of neutrons
    public float mass;   // Atomic mass in Da (unified atomic mass units)
    // A = Z + N is the mass number (integer)
}
```

Example: Carbon-12 has Z = 6, N = 6, mass = 12.000 Da. A free proton has mass ≈ 1.007276 Da.

To convert to kilograms: `mass_kg = mass * Constants.Da`. To convert to energy: `E = mass_kg * c^2` (but use binding energy instead — see below).

## How to store binding energy

**Store binding energy in eV** (electronvolts). You already have `Constants.eV = 1.602176634e-19f J`.

Nuclides should store **binding energy per nucleon**, which is the conventional way:

```csharp
public class Nuclide
{
    public uint Z;
    public uint N;
    public float mass_Da;                 // Atomic mass in Da
    public float bindingEnergyPerNucleon_eV; // Binding energy per nucleon in eV
    // For carbon-12: ~7.68 MeV per nucleon
}
```

### Why binding energy per nucleon?

1. It's the standard format in nuclear data tables.
2. It makes it easy to compare nuclear stability: iron-56 has the highest at ~8.79 MeV/nucleon.
3. Total binding energy = `bindingEnergyPerNucleon_eV * (Z + N)`.
4. The sign convention: binding energy is **positive** — it's the energy released when the nucleus formed from free nucleons, or equivalently, the energy you need to add to break the nucleus apart.

## Are nuclear reactions exactly equal to changes in mass?

**Yes.** By $E = mc^2$, the mass defect is real:

$$m_\text{nucleus} = Z \cdot m_p + N \cdot m_n - \frac{E_\text{binding}}{c^2}$$

The mass of a nucleus is less than the sum of its free nucleons by exactly $\Delta m = E_\text{binding} / c^2$.

For a nuclear reaction, the Q-value (energy released) is:

$$Q = \left(\sum m_\text{reactants} - \sum m_\text{products}\right) \cdot c^2$$
$$Q = \sum E_\text{binding, products} - \sum E_\text{binding, reactants}$$

So you can work either way: store precise masses and compute binding energies from them, or store binding energies and compute masses. **For game purposes, store binding energy per nucleon** — it's more directly useful for computing energy released in nuclear reactions.

## Recommended Nuclide fields

```csharp
public class Nuclide
{
    public uint Z;                        // Number of protons
    public uint N;                        // Number of neutrons
    public float mass_Da;                 // Atomic mass including electrons, in Da
    public float bindingPerNucleon_eV;    // Binding energy per nucleon, eV
    public float halfLife_s;              // Half-life in seconds (float.MaxValue for stable)
    public string decayChain;             // How it decays (for later)

    // Derived properties
    public uint A => Z + N;               // Mass number
    public float totalBinding_eV => bindingPerNucleon_eV * A;  // Total binding energy
    public float mass_kg => mass_Da * Constants.Da;            // Mass in kg

    // Rest mass energy (for completeness)
    public float restEnergy_J => mass_kg * 8.987551787e16f; // c^2 in m^2/s^2
}
```

This is not urgent — your AGENTS.md correctly notes nuclides are for the far future. The chemistry system works entirely at the element/species level, not the nuclide level.
