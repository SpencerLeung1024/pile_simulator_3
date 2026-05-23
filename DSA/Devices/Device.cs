using System;
using System.Collections.Generic;

public abstract class Device
{
    public string Name;
    public double Scale; // Multiplier for power and throughput. Different devices have different rules for what scales are allowed
    public Dictionary<ItemResource, double> UnitBuildCost; // Cost to build one device at scale 1

    // Derived quantities:
    // Depends on subclass
    protected abstract void DeriveQuantities();
}
