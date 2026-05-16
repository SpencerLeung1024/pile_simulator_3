using Godot;
using System;
using System.Collections.Generic;

public class Species
{
    public string Name; // "Water", "Carbon Dioxide", etc.
    public Dictionary<Element, uint> Formula; // {H: 2, O: 1} for water
    public float MolarMass; // kg / mol, calculated from the formula and Elements.cs
    public float omega; // acentric factor, used in Soave-Redlich-Kwong and Peng-Robinson equations of state
    public List<SpeciesPhase> Phases;
}

public enum Phase
{
    Gas,
    Liquid,
    Supercritical,
    Solid // Does not fully specify the phase since allotropes exist
}

public class SpeciesPhase
{
    public Species Species;
    public Phase Phase;
    public string Name; // "Graphite", "Diamond", etc. for solids, otherwise PhaseEnum

    public float StandardEnthalpyOfFormation; // J / mol, gas phase, at whatever the standard conditions are
    public float StandardEntropy; // J / (K * mol), gas phase, at whatever the standard conditions are
    public float StandardTemperature; // K, used to know where the standard enthalpy of formation and standard entropy are measured
    public float StandardPressure; // Pa, used similarly

    public HeatCapacityFunction HeatCapacityFunction;
    // Provides:
    /*
    - c_p(T), J / (K * mol), molar heat capacity at constant pressure
    - H(T), J / mol, molar enthalpy relative to standard enthalpy of formation
    - S(T), J / (K * mol), molar entropy relative to standard entropy
    */
    public EquationOfState EquationOfState;
    // Provides:
    /*
    - U(S, v), J / mol, molar internal energy
    - P(T, v), Pa, pressure
    - v(T, P), m^3 / mol, molar volume
    */
}

// An amount of a species in a phase
public class Resource
{
    //public Species Species;
    //public Phase Phase;
    // Normalize. SpeciesPhase contains both Species and Phase, so don't store them separately (avoid potential for inconsistency)
    public SpeciesPhase SpeciesPhase;
    public float Amount; // mol

    // TODO: Figure out where to store things like temperature, internal energy, etc.

    // TODO: Methods for applying heat
}

// TODO: Define species and species phases
