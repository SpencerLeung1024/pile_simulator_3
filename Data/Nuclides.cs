using System;
using System.Collections.Generic;

public class Nuclide
{
    public uint Z;
    public uint N;
    public double BindingEnergyPerNucleon; // eV
    public double HalfLife; // s
    public Dictionary<string, double> DecayProbabilities; // nubase code -> branching ratio (0-100)

    // Derived quantities
    public uint A;
    public double BindingEnergy; // eV
    public double NuclideMass; // kg, Z + N - binding energy
    public double AtomicMass; // kg, Z + N - binding energy + e
    public double MolarMass; // kg, atomic mass * N_A
    public double RelativeIsotopicMass; // technically dimensionless but can be multiplied by Da to get atomic mass, atomic mass / (1/12) of carbon-12

    private void DeriveQuantities()
    {
        A = Z + N;
        BindingEnergy = BindingEnergyPerNucleon * A;
        double bindingEnergyAsMass = Constants.EnergyToMass(BindingEnergy);
        NuclideMass = Z * Constants.ProtonMass + N * Constants.NeutronMass - bindingEnergyAsMass;
        AtomicMass = NuclideMass + Z * Constants.ElectronMass;
        MolarMass = AtomicMass * Constants.N_A;
        RelativeIsotopicMass = AtomicMass / Constants.Da;
    }

    // Private the default constructor to prohibit creating nuclides without calculating derived quantities
    private Nuclide() {}

    public Nuclide(uint Z, uint N, double BindingEnergyPerNucleon_eV, double HalfLife_s, Dictionary<string, double> DecayProbabilities)
    {
        this.Z = Z;
        this.N = N;
        this.BindingEnergyPerNucleon = BindingEnergyPerNucleon_eV;
        this.HalfLife = HalfLife_s;
        this.DecayProbabilities = DecayProbabilities ?? new Dictionary<string, double>();
        DeriveQuantities();
    }
}

public static class Nuclides
{
    public static Nuclide[,] table = new Nuclide[119, 178];

    public static Dictionary<Element, Dictionary<Nuclide, double>> abundances = new Dictionary<Element, Dictionary<Nuclide, double>>();

    public static double SemiEmpiricalMassFormula(uint Z, uint N)
    {
        double a_v = 15.5;
        double a_s = 15.5;
        double a_c = 0.691;
        double a_sym = 23.0;
        double a_p = 34.0;

        uint A = Z + N;
        int delta = 1 - ((int)Z % 2) - ((int)N % 2);

        double volumeTerm = a_v * A;
        double surfaceTerm = -a_s * Math.Pow(A, 2.0 / 3.0);
        double coulombTerm = -a_c * Z * (Z - 1) * Math.Pow(A, -1.0 / 3.0);
        double symmetryTerm = -a_sym * (A - 2 * Z) * (A - 2 * Z) / A;
        double pairingTerm = delta * a_p * Math.Pow(A, -3.0 / 4.0);

        return volumeTerm + surfaceTerm + coulombTerm + symmetryTerm + pairingTerm;
    }

    public static void MakeUpNuclide(uint Z, uint N)
    {
        double bindingEnergyMeV = SemiEmpiricalMassFormula(Z, N);
        double bindingEnergyPerNucleon = bindingEnergyMeV * 1e6 / (Z + N);
        table[Z, N] = new Nuclide(Z, N, bindingEnergyPerNucleon, double.PositiveInfinity, new Dictionary<string, double>());
    }

    public static Nuclide ByZN(uint Z, uint N)
    {
        if (table[Z, N] == null)
            MakeUpNuclide(Z, N);
        return table[Z, N];
    }

    public static Nuclide ByZA(uint Z, uint A)
    {
        return ByZN(Z, A - Z);
    }

    public static void Initialize()
    {
        NuclideDataLoader.Load("Data/mass_1.mas20.txt", "Data/nubase_4.mas20.txt");
    }
}

public class NuclideResource
{
    public Nuclide nuclide;
    public double n;
}
