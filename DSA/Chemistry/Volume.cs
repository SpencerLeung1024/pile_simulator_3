using System;
using System.Collections.Generic;

// A volume represents something that contains matter
// The matter may be made up of numerous species, existing as gas, liquid, and solid phases
public class Volume : Inventory<SpeciesPhaseResource>
{
    // Resources: the species in phases in this volume, and their amounts in mol
    // Mass: kg
    // Volume: m^3
    public double T; // K
    public double P; // Pa

    // TODO: MaybeAdd and MaybeMerge
    // Volume is a thermodynamic simulation, so putting things in the box requires work
    // A full theory of pumps is needed before those methods can be implemented
}
