using System;
using System.Collections.Generic;

public class Constants
{
    // SI prefixes
    public static readonly Dictionary<string, double> prefixToValue = new Dictionary<string, double>
    {
        {"Y", 1e24},
        {"Z", 1e21},
        {"E", 1e18},
        {"P", 1e15},
        {"T", 1e12},
        {"G", 1e9},
        {"M", 1e6},
        {"k", 1e3},
        {"h", 1e2},
        {"da", 1e1},
        {"d", 1e-1},
        {"c", 1e-2},
        {"m", 1e-3},
        {"u", 1e-6},
        {"n", 1e-9},
        {"p", 1e-12},
        {"f", 1e-15},
        {"a", 1e-18},
        {"z", 1e-21},
        {"y", 1e-24}
    };

    public static readonly Dictionary<int, string> exponentToPrefix = new Dictionary<int, string>
    {
        {24, "Y"},
        {21, "Z"},
        {18, "E"},
        {15, "P"},
        {12, "T"},
        {9, "G"},
        {6, "M"},
        {3, "k"},
        {2, "h"},
        {1, "da"},
        {-1, "d"},
        {-2, "c"},
        {-3, "m"},
        {-6, "u"},
        {-9, "n"},
        {-12, "p"},
        {-15, "f"},
        {-18, "a"},
        {-21, "z"},
        {-24, "y"}
    };

    public static readonly Dictionary<string, string> fixKgDict = new Dictionary<string, string>
    {
        //{"Ykg", null}, // 1e27 g can't be represented
        {"Zkg", "Yg"},
        {"Ekg", "Zg"},
        {"Pkg", "Eg"},
        {"Tkg", "Pg"},
        {"Gkg", "Tg"},
        {"Mkg", "Gg"},
        {"kkg", "Mg"},
        //{"hkg", null}, // 1e5 g
        //{"dakg", null} // 1e4 g
        {"dkg", "hg"},
        {"ckg", "dag"},
        {"mkg", "g"},
        {"ukg", "mg"},
        {"nkg", "ug"},
        {"pkg", "ng"},
        {"fkg", "pg"},
        {"akg", "fg"},
        {"zkg", "ag"},
        {"ykg", "zg"}
    };

    // Physics

    // speed of light
    public const double c = 2.99792458e8; // m / s
    // gravitational constant
    public const double G = 6.67430e-11; // m^3 / (kg * s^2)
    // Stefan-Boltzmann constant
    public const double sigma = 5.670374419e-8; // W / (m^2 K^4)
    // cosmic microwave background temperature
    public const double CMBTemperature = 2.72548; // K

    // Chemistry

    // The pursuit of a standard temperature and pressure is as old as the pursuit of turning lead into gold
    // See https://en.wikipedia.org/wiki/Standard_temperature_and_pressure for more details

    // IUPAC STP = (0 C, 1 bar)
    // IUPAC SATP = (25 C, 1 atm)
    // NIST NTP = (20 C, 1 atm). Note that NIST also uses (25 C, 1 bar) for thermodynamic data, and 15 C as a temperature correction for refined petroleum products

    public const double IUPACStandardTemperature = 273.15; // K, 0 C
    public const double NISTNormalTemperature = 293.15; // K, 20 C
    public const double IUPACStandardAmbientTemperature = 298.15; // K, 25 C
    
    public const double bar = 1e5; // Pa, 100 kPa
    public const double atm = 1.01325e5; // Pa, 101.325 kPa

    // Avogadro constant
    public const double N_A = 6.02214076e23; // 1 / mol
    // Boltzmann constant
    public const double k_B = 1.380649e-23; // J / K
    // Ideal gas constant = N_A * k_B
    public const double R = 8.31446261815324; // J / (K * mol)
    // Dalton (Da), unified atomic mass unit (u)
    // u is preferred but "u" is a nice variable to use in code so I use the less ambiguous "Da"
    public const double Da = 1.66053906892e-27; // kg
    // electronvolt
    public const double eV = 1.602176634e-19; // J

    public const double ProtonMass = 1.0072764665789 * Da; // kg
    public const double NeutronMass = 1.00866491606 * Da; // kg
    public const double ElectronMass = 5.485799090441e-4 * Da; // kg  

    // Gameplay

    //public const float ConservationOfMassTolerance = 1e-7f; // (mol element in product - mol element in reactant) / mol element in reactant
    public const double ConservationOfMassTolerance = 1e-15;

    // floats have 23 bits in the mantissa and 24 bits of precision, which is about 7.225 decimal digits
    // doubles have 52 bits in the mantissa and 53 bits of precision, which is about 15.955 decimal digits
    // It is not possible for floats to represent precisions below 1e-7

    // How long will it take for a mol to become 0.5 mol at an error of 1e-7 per frame?

    // present = initial * (1 - error) ^ frames
    // 0.5 = 1 * (1 - 1e-7) ^ frames
    // Solve for frames
    // log10(0.5) = frames * log10(1 - 1e-7)
    // frames = log10(0.5) / log10(1 - 1e-7)
    // frames = -3.010 299 956 639 81e-1 / -4.342 945 036 179 77e-8
    // frames = 6.931 471 459 025 85e6
    // That's about 1 day 8 hours at 60 FPS
    // It's actually not very long. I've put 120 hours into my moon base in Stationeers
    // The reaction and phase solvers run constantly, so if I tried to make Pile Simulator 3 out of floats my resources would literally decay faster than radon

    // The same calculation, with doubles:

    // frames = log10(0.5) / log10(1 - 1e-15)
    // frames = -3.010 299 956 639 81e-1 / -4.342 944 819 032 52e-16
    // frames = 6.931 471 805 599 44e14
    // That's 366 082 years at 60 FPS
    // A reasonable amount of time?

    // How many bits in the mantissa do we need to keep 0.5 mols out of 1 mol from the beginning of the universe?

    // The best estimate of the age of the universe is 13.787 billion years
    // Apparently IUPAC has defined a year. See https://en.wikipedia.org/wiki/Year#IUPAC%E2%80%93IUGS_proposal
    // 1 a = 31 556 925.974 7 s = 365.242 198 781 25 d, where d is defined to be exactly 86 400 s
    // So the universe is 4.350 753 384 131 88e17 s old
    // At 60 FPS, 2.610 452 030 479 13e19 frames have elapsed since the beginning of the universe
    // God uses a 640x480 monitor (Terry A. Davis, n.d.), but the refresh rate is not specified
    // 2.610 452 030 479 13e19 = log10(0.5) / log10(1 - 1e-m)
    // Solve for m
    // log10(1 - 1e-m) = log10(0.5) / 2.610 452 030 479 13e19
    // 1 - 1e-m = 10^(log10(0.5) / 2.610 452 030 479 13e19)
    // 1e-m = 1 - 10^(log10(0.5) / 2.610 452 030 479 13e19)
    // m = -log10(1 - 10^(log10(0.5) / 2.610 452 030 479 13e19))
    // m = 19.575 890 256 003 8 decimal digits
    // mantissa = ceil(m * log2(10)) - 1
    // mantissa = ceil(19.575 890 256 003 8 * 3.321 928 094 887 36) - 1
    // mantissa = ceil(65.029 699 823 850 7) - 1
    // So 65 bits are needed

    // Note that we now keep track of (possibly negative) freeElements, so conservation of mass is no longer a quantity we track
    // Positive and negative amount errors are self-limiting, so in the end only floating point error accumulates

    // Dissociate
    public const double DissociationThreshold = 1e-3; // fraction per frame, 0.1%

    // SolveReactions
    public const uint MaxReactionSteps = 20;
    public const double log_xMin = -100;
    public const double log_xMax = 100;
    public const double N_mMin = 1e-6; // mol, N_m for phase m is clamped to be > this after every Newton step
    public const double V_mMin = 1e-6; // m^3, V_m is not used in the solver but is used for display
    public const double LambdaMaxJump = 1e1; // Maximum change in an element's element potential per step
    public const double H_iTolerance = 1e-6; // mol element i unused, an early exit condition for SolveReactions
    public const double Z_mTolerance = 1e-6; // mole fraction of phase m away from 1, an early exit condition for SolveReactions
    public const double n_jMin = 1e-6; // mol, a species will not be realized if its calculated amount is < this
    // Instead, the elements it would have consumed are returned to freeElements
    // For comparison, Stationeers uses 0.001 mmol as the deletion threshold, below which a species (and any mass it embodies) vanishes

    // SolveUT
    public const uint MaxUTSteps = 20;
    public const double UTolerance = 1e-6; // (J actual - J target) / J target, an early exit condition for SolveUT
    public const double Tmin = 1e-6; // K, avoid going below absolute zero
    public const double MaxDeltaT = 50.0; // K, maximum Newton step in SolveUT (damping)

    // SolveVP
    public const uint MaxVPSteps = 20;
    public const double VTolerance = 1e-6; // (m^3 actual - m^3 target) / m^3 target, an early exit condition for SolveVP
    public const double Pmin = 1e-6; // Pa, avoid going below a vacuum    
    public const double MaxDeltaP = 1e5; // Pa, maximum Newton step in SolveVP (damping)
    
    // SolvePhases
    public const double PhaseDamping = 0.1; // fraction, how aggressively to move toward equilibrium in SolvePhases
    // Originally 0.5 but it caused weird oscillations
    // The cause was that as liquid water evaporates, it cools down the system
    // This dramatically reduces T and P_sat, so all the gaseous water condenses on the next frame
    // Stationeers uses 0.1 in its code apparently. Sadly no improvement here

    // Unscientific
    
    public static double year = 31556925.9747; // s, nevermind we actually do need a year for nuclide half life

    // Helper functions

    public static string FormatUnit(double value, uint figures, string unit)
    {
        string valueStr;
        string prefix;
        string prefixAndUnit;
        if (value == 0.0) // Can't log 0
        {
            valueStr = "0";
            prefixAndUnit = unit;
        }
        else
        {
            int exponent = (int)(Math.Floor(Math.Log10(Math.Abs(value)) / 3) * 3); // Only use the kilo prefixes
            prefix = exponentToPrefix.GetValueOrDefault(exponent, "");
            if (prefix != "")
            {
                double rescaledValue = value / Math.Pow(10, exponent);
                valueStr = rescaledValue.ToString($"G{figures}");
                if (unit == "kg")
                {
                    string fixedUnit = fixKgDict.GetValueOrDefault(prefix + unit, "");
                    if (fixedUnit != "")
                    {
                        prefixAndUnit = fixedUnit;
                    }
                    else
                    {
                        // Just use scientific notation and no prefix
                        valueStr = value.ToString($"G{figures}");
                        prefixAndUnit = unit;
                    }
                }
                else
                {
                    prefixAndUnit = prefix + unit;
                }
            }
            else
            {
                valueStr = value.ToString($"G{figures}");
                prefixAndUnit = unit;
            }
        }
        return $"{valueStr} {prefixAndUnit}";
    }

    public static double MassToEnergy(double mass)
    {
        return mass * c * c;
    }

    public static double EnergyToMass(double energy)
    {
        return energy / (c * c);
    }
}
