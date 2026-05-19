using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public class Species
{
    public string Name; // "Water", "Carbon Dioxide", etc.
    public Dictionary<Element, uint> Formula; // {H: 2, O: 1} for water
    public double omega; // acentric factor, used in Soave-Redlich-Kwong and Peng-Robinson equations of state
    public List<SpeciesPhase> Phases;

    // Derived quantities
    //public double MolarMass { get { return Formula.Sum(kv => kv.Key.A_r * kv.Value); } } // kg / mol
    public double MolarMass; // Doing a sum every time is expensive so determine this value at construction

    private void DeriveQuantities()
    {
        MolarMass = Formula.Sum(kv => kv.Key.A_r * kv.Value);
    }

    // Private the default constructor to prohibit creating species without calculating derived quantities
    private Species() {}

    // Constructor
    public Species(string Name, Dictionary<Element, uint> Formula, double omega, List<SpeciesPhase> Phases)
    {
        this.Name = Name;
        this.Formula = Formula;
        this.omega = omega;
        this.Phases = Phases;
        DeriveQuantities();
    }
}

public enum Phase
{
    Gas, // 0, supercritical fluid also uses this
    Liquid, // 1
    Solid // 2, does not fully specify the phase since allotropes exist
}

public class SpeciesPhase
{
    public Species Species;
    public Phase Phase;
    public string Name; // "Graphite", "Diamond", etc. for solids, otherwise PhaseEnum

    /*
    public float StandardEnthalpyOfFormation; // J / mol, gas phase, at whatever the standard conditions are
    public float StandardEntropy; // J / (K * mol), gas phase, at whatever the standard conditions are
    public float StandardTemperature; // K, used to know where the standard enthalpy of formation and standard entropy are measured
    public float StandardPressure; // Pa, used similarly
    */
    // All three heat capacity functions: NASA7, NASA9, and Shomate, are forms in which standard enthalpy of formation and standard entropy are already included
    // NASA9.GetH(T) *is* molar enthalpy at temperature T (well after I multiply by RT but my code does that)
    // etc.
    // ConstantHeatCapacityFunction still needs StandardEnthalpyOfFormation, StandardEntropy, and StandardTemperature though

    public HeatCapacityFunction HeatCapacityFunction;
    // Provides:
    /*
    - c_p(T), J / (K * mol), molar heat capacity at constant pressure
    - H(T), J / mol, molar enthalpy
    - S(T), J / (K * mol), molar entropy
    */
    public EquationOfState EquationOfState;
    // Provides:
    /*
    - U(T, v), J / mol, molar internal energy
    - P(T, v), Pa, pressure
    - v(T, P), m^3 / mol, molar volume
    */
}

// An amount of a species in a phase
// Originally just called "Resource". It turns out that Godot already has a "Resource". Also I need to disambiguate between species phase resources and nuclide resources
public class SpeciesPhaseResource
{
    //public Species Species;
    //public Phase Phase;
    // Normalize. SpeciesPhase contains both Species and Phase, so don't store them separately (avoid potential for inconsistency)
    public SpeciesPhase SpeciesPhase;
    public float n; // mol
    // These are all the fields of Resource. It has no methods. All chemistry logic is handled by Volume
}

// TODO: Define species and species phases
