using System;
using System.Collections.Generic;

public abstract class Inventory<T>
{
    protected List<T> Resources; // Protect so you can't add a resource without going through Add, which recalculates derived quantities
    public double Volume; // m^3

    // Derived quantities:
    public double Mass; // kg
    public double UsedVolume; // m^3
    public double FreeVolume { get { return Volume - UsedVolume; } } // m^3

    protected abstract void DeriveQuantities();

    public abstract bool CanAdd(T resource);
    public abstract bool MaybeAdd(T resource); // true if added, false if not
    //public abstract bool MaybeMerge(Inventory<T> other); // true if merged, false if not
    // Nevermind NuclideInventory is not Inventory<NuclideResource>
}

// A specific inventory for nuclides
// In Pile Simulator 3, nuclides do not have physical or thermodynamic properties
// TODO: Figure out how much volume nuclides take up
public class NuclideInventory : Inventory<NuclideResource>
{
    // Resources: the nuclides in this inventory, and their amounts in mol
    // Mass: kg
    // Volume: m^3

    private double GetVolumeOfResource(NuclideResource resource)
    {
        return resource.n * 1e-3; // Placeholder. Assume every nuclide takes up 1 L per mol
    }

    protected override void DeriveQuantities()
    {
        Mass = 0;
        UsedVolume = 0;
        foreach (NuclideResource resource in Resources)
        {
            Mass += resource.nuclide.MolarMass * resource.n;
            UsedVolume += GetVolumeOfResource(resource);
        }
    }

    // Not volume safe
    private void Merge(NuclideResource resource)
    {
        foreach (NuclideResource r in Resources)
        {
            if (r.nuclide == resource.nuclide)
            {
                r.n += resource.n;
                return;
            }
        }
        Resources.Add(resource);
    }

    public override bool CanAdd(NuclideResource resource)
    {
        return FreeVolume >= GetVolumeOfResource(resource);
    }

    public override bool MaybeAdd(NuclideResource resource)
    {
        if (!CanAdd(resource))
        {
            return false;
        }
        else
        {
            Merge(resource);
            DeriveQuantities();
            return true;
        }
    }

    public bool MaybeMerge(NuclideInventory other) // true if merged, false if not
    {
        if (FreeVolume < other.UsedVolume)
        {
            return false;
        }
        else
        {
            foreach (NuclideResource resource in other.Resources)
            {
                Merge(resource);
            }
            DeriveQuantities();
            return true;
        }
    }
}

// A specific inventory for chemical species in specific phases
// See Volume.cs

// A specific inventory for countable items
public class ItemInventory : Inventory<ItemResource>
{
    // Resources: the items in this inventory, and their amounts in count
    // Mass: kg
    // Volume: m^3
}
