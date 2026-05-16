using Godot;
using System;

public class Constants
{
    // Gravity
    public const float G = 6.67430e-11f; // m^3 / (kg * s^2)

    // Chemistry

    // The pursuit of a standard temperature and pressure is as old as the pursuit of turning lead into gold
    // See https://en.wikipedia.org/wiki/Standard_temperature_and_pressure for more details

    // IUPAC STP = (0 C, 1 bar)
    // IUPAC SATP = (25 C, 1 atm)
    // NIST NTP = (20 C, 1 atm). Note that NIST also uses (25 C, 1 bar) for thermodynamic data, and 15 C as a temperature correction for refined petroleum products

    public const float IUPACStandardTemperature = 273.15f; // K, 0 C
    public const float NISTNormalTemperature = 293.15f; // K, 20 C
    public const float IUPACStandardAmbientTemperature = 298.15f; // K, 25 C
    
    public const float bar = 1e5f; // Pa, 100 kPa
    public const float atm = 1.01325e5f; // Pa, 101.325 kPa

    // Avogadro constant
    public const float N_A = 6.02214076e23f; // 1 / mol
    // Boltzmann constant
    public const float k_B = 1.380649e-23f; // J / K
    // Ideal gas constant = N_A * k_B
    public const float R = 8.31446261815324f; // J / (K * mol)
    // Dalton (Da), unified atomic mass unit (u)
    // u is preferred but "u" is a nice variable to use in code so I use the less ambiguous "Da"
    public const float Da = 1.66053906892e-27f; // kg
    // electronvolt
    public const float eV = 1.602176634e-19f; // J
}
