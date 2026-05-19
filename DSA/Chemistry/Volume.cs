using Godot;
using System;
using System.Collections.Generic;

// A volume represents something that contains matter
// The matter may be made up of numerous species, existing as solid, liquid, gas, and supercritical fluid phases
public class Volume
{
    public float T; // K
    public float P; // Pa
    public float V; // m^3
    public List<SpeciesResource> SpeciesResources; // The species and phases present in this volume, and their amounts in mol
}