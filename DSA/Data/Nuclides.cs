using System;
using System.Collections.Generic;

public enum DecayMode
{
    AlphaDecay, // 0, (Z, N) -> (Z-2, N-2) + (2, 2)
    BetaMinusDecay, // 1, the normal beta decay emitting an electron, (Z, N) -> (Z+1, N-1) + e- + !nu_e
    PositronEmission, // 2, the other beta decay emitting a positron, (Z, N) -> (Z-1, N+1) + e+ + nu_e
    ElectronCapture, // 3, (Z, N) + e- -> (Z-1, N+1) + nu_e
    // It can be difficult to distinguish between positron emission and electron capture
    // NuDat only gives an electron capture + positron emission combined probability
    ProtonEmission, // 4, (Z, N) -> (Z-1, N) + p
    NeutronEmission, // 5, (Z, N) -> (Z, N-1) + n
    SpontaneousFission // 6, (Z, N) -> ???
}

public class Nuclide
{
    public uint Z; // number of protons
    public uint N; // number of neutrons
    public double BindingEnergyPerNucleon; // eV
    // Note that "binding energy per nucleon" in tables includes the sum of ionization energies of all electrons of the neutral atom
    // It is extremely difficult to measure the mass of a mole of fully ionized nuclei
    // Instead, the mass of a mole of neutral atoms is measured
    // hydrogen-1 (Z: 1, N: 0) has a binding energy of 13.6 eV and a binding energy per nucleon of 13.6 eV
    public double HalfLife; // s, use the full constructor to input years
    public Dictionary<DecayMode, double> DecayProbabilities; // probability of decaying via that decay mode

    // Derived quantities
    /*
    public uint A { get { return Z + N; } } // mass number, number of nucleons
    public double BindingEnergy { get { return BindingEnergyPerNucleon * A; } } // eV
    public double Mass { get { return Z * Constants.ProtonMass + N * Constants.NeutronMass - Constants.EnergyToMass(BindingEnergy * Constants.eV); } } // kg
    public double RelativeIsotopicMass { get { return Mass / Constants.Da; } } // Da
    */
    // get functions are expensive so determine at construction
    public uint A; // mass number, number of nucleons
    public double BindingEnergy; // eV
    public double Mass; // kg, 1 nuclide, multiply by Constants.N_A for molar mass
    public double RelativeIsotopicMass; // Da

    private void DeriveQuantities()
    {
        A = Z + N;
        BindingEnergy = BindingEnergyPerNucleon * A;
        Mass = Z * Constants.ProtonMass + N * Constants.NeutronMass - Constants.EnergyToMass(BindingEnergy * Constants.eV);
        RelativeIsotopicMass = Mass / Constants.Da;
    }

    // Private the default constructor to prohibit creating nuclides without calculating derived quantities
    private Nuclide() {}

    // Shortened constructor for stable nuclides
    public Nuclide(uint Z, uint N, double BindingEnergyPerNucleon)
    {
        this.Z = Z;
        this.N = N;
        this.BindingEnergyPerNucleon = BindingEnergyPerNucleon;
        this.HalfLife = double.PositiveInfinity;
        this.DecayProbabilities = new Dictionary<DecayMode, double>();
        DeriveQuantities();
    }

    // Full constructor
    public Nuclide(uint Z, uint N, double BindingEnergyPerNucleon, double HalfLifeInYears, Dictionary<DecayMode, double> DecayProbabilities)
    {
        this.Z = Z;
        this.N = N;
        this.BindingEnergyPerNucleon = BindingEnergyPerNucleon;
        this.HalfLife = HalfLifeInYears * Constants.year;
        this.DecayProbabilities = DecayProbabilities;
        DeriveQuantities();
    }
}

public class Nuclides
{
    // We do not have data to fill the entire table, and most off-diagonal items are probably too unstable to be studied
    // Create the table first, then fill it in below
    public static Nuclide[,] table = new Nuclide[119, 178];
    // The largest Z is in oganesson-294 (Z: 118, N: 176)
    // 119 = 118 elements + 1 so that table[z=0,n=1] is a free neutron
    // But the largest N is in the previous element, tennesine-294 (Z: 117, N: 177)
    // 178 = 177 neutrons + 1 so that table[z=1,n=0] is hydrogen-1
    // Not a free proton. It has 13.6 eV of binding energy

    // Nevermind oganesson-295 exists now (Z: 118, N: 177)
    // line 5868 of `DSA/Data/nubase_4.mas20.txt`

    public static Dictionary< Element, Dictionary<Nuclide, double> > abundances = new Dictionary< Element, Dictionary<Nuclide, double> >();
    // abundances[element] = dictionary of nuclide to abundance
    // abundances[hydrogen] = { hydrogen-1: 0.999 844, hydrogen-2: 0.000 156 }
    // Separating 1 mole of hydrogen gives you 0.999 844 moles of hydrogen-1 (protium) and 0.000 156 moles of hydrogen-2 (deuterium)
    // Note that, like argon, the abundance of hydrogen's isotopes (and thus hydrogen's standard atomic weight) is based on what's on Earth, not the solar system
    // The primordial abundance of deuterium is about 0.000 026

    // You can get a bunch of data from https://www.nndc.bnl.gov/nudat3/
    // NuDat 3.0 by the National Nuclear Data Center at Brookhaven National Laboratory
    // Unfortunately, NuDat provides abundances as range. Pile Simulator 3 needs a specific value with zero uncertainty, so I have made some up

    // Used for binding energy per nucleon for nuclides not defined here
    // This is the total binding energy in MeV of the entire nucleus
    public static double SemiEmpiricalMassFormula(uint Z, uint N)
    {
        // There are a lot of coefficients floating around. See:
        // https://en.wikipedia.org/wiki/Semi-empirical_mass_formula#Calculating_coefficients
        // The coefficients used here are from https://phys.libretexts.org/Bookshelves/Nuclear_and_Particle_Physics/Introduction_to_Applied_Nuclear_Physics_(Cappellaro)/01%3A_Introduction_to_Nuclear_Physics/1.02%3A_Binding_energy_and_Semi-empirical_mass_formula
        double a_v = 15.5; // MeV, volume coefficient
        double a_s = 15.5; // MeV, surface coefficient
        // The book says 1) it should be close to a_v and 2) it is between 13 and 18 MeV. I have used the exact midpoint
        // Interestingly, Wikipedia's list says least squares regression supports a higher a_s, 17.8 - 18.3 MeV
        double a_c = 0.691; // MeV, Coulomb coefficient
        double a_sym = 23.0; // MeV, symmetry coefficient
        double a_p = 34.0; // MeV, pairing coefficient

        uint A = Z + N;
        int delta = 1 - ((int)Z % 2) - ((int)N % 2); // 1 for even-even, 0 for even-odd or odd-even, -1 for odd-odd

        double volumeTerm = a_v * A;
        double surfaceTerm = -a_s * Math.Pow(A, 2.0/3.0);
        double coulombTerm = -a_c * Z * (Z - 1) * Math.Pow(A, -1.0/3.0);
        double symmetryTerm = -a_sym * (A - 2 * Z) * (A - 2 * Z) / A;
        double pairingTerm = delta * a_p * Math.Pow(A, -3.0/4.0);

        return volumeTerm + surfaceTerm + coulombTerm + symmetryTerm + pairingTerm;
    }

    public static void MakeUpNuclide(uint Z, uint N)
    {
        double bindingEnergyMeV = SemiEmpiricalMassFormula(Z, N);
        double bindingEnergyPerNucleon = bindingEnergyMeV * 1e6 / (Z + N);
        table[Z, N] = new Nuclide(Z, N, bindingEnergyPerNucleon);
    }

    public static Nuclide ByZN(uint Z, uint N)
    {
        if (table[Z, N] == null)
        {
            MakeUpNuclide(Z, N);
        }
        return table[Z, N];
    }

    public static Nuclide ByZA(uint Z, uint A)
    {
        return ByZN(Z, A - Z);
    }

    // Helper function
    public static void AddIsotopes(Element element, Dictionary<Nuclide, double> abundance)
    {
        abundances[element] = abundance;
        foreach (Nuclide nuclide in abundance.Keys)
        {
            table[nuclide.Z, nuclide.N] = nuclide;
        }
    }

    // The big book of nuclides
    public static void Initialize()
    {
        AddIsotopes(Elements.BySymbol("H"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(1, 0, 13.6), 0.999844 }, // hydrogen-1 (protium)
            { new Nuclide(1, 1, 1.112283e6), 0.000156 }, // hydrogen-2 (deuterium)
            { new Nuclide(1, 2, 2.827265e6, 12.32, new Dictionary<DecayMode, double>
            {
                { DecayMode.BetaMinusDecay, 1.0 }
            }), 0.0 } // hydrogen-3 (tritium)
        });
        AddIsotopes(Elements.BySymbol("He"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(2, 1, 2.572680e6), 0.000002 }, // helium-3
            { new Nuclide(2, 2, 7.073916e6), 0.999998 }, // helium-4
            { new Nuclide(2, 3, 5.5124e6, 0.0, new Dictionary<DecayMode, double>
            {
                { DecayMode.NeutronEmission, 1.0 }
            }), 0.0 } // helium-5, an unstable intermediate nucleus during D-T fusion that immediately decays
        });

        AddIsotopes(Elements.BySymbol("Li"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(3, 3, 5.332331e6), 0.06 }, // lithium-6, this specific abundance is calculated as a linear combination producing the standard atomic weight of 6.94
            { new Nuclide(3, 4, 5.606440e6), 0.94 } // lithium-7
        });
        AddIsotopes(Elements.BySymbol("Be"), new Dictionary<Nuclide, double>
        {
           { new Nuclide(4, 4, 7.062436e6, 0.0, new Dictionary<DecayMode, double>
           {
               { DecayMode.AlphaDecay, 1.0 }
           }), 0.0 }, // beryllium-8, during He4-He4 fusion
           { new Nuclide(4, 5, 6.462670e6), 1.0 } // beryllium-9
        });
        AddIsotopes(Elements.BySymbol("B"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(5, 5, 6.475083e6), 0.19 }, // boron-10
            { new Nuclide(5, 6, 6.927732e6), 0.81 } // boron-11
        });
        AddIsotopes(Elements.BySymbol("C"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(6, 8, 7.680145e6), 0.9889 }, // carbon-12
            { new Nuclide(6, 8, 7.469850e6), 0.0111 }, // carbon-13
            { new Nuclide(6, 8, 7.520320e6, 5686.0, new Dictionary<DecayMode, double>
            {
                { DecayMode.BetaMinusDecay, 1.0 }
            }), 0.0 } // carbon-14, it exists in the parts per trillion but we don't have enough precision to work with that
        });
        AddIsotopes(Elements.BySymbol("N"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(7, 7, 7.475615e6), 0.9964 }, // nitrogen-14
            { new Nuclide(7, 8, 7.699460e6), 0.0036 } // nitrogen-15
        });
        AddIsotopes(Elements.BySymbol("O"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(8, 8, 7.976207e6), 0.9976 }, // oxygen-16
            { new Nuclide(8, 9, 7.750729e6), 0.0004 }, // oxygen-17
            { new Nuclide(8, 10, 7.767098e6), 0.0020 } // oxygen-18
        });
        AddIsotopes(Elements.BySymbol("F"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(9, 10, 7.779019e6), 1.0 }, // fluorine-19
        });
        AddIsotopes(Elements.BySymbol("Ne"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(10, 10, 8.032241e6), 0.9048 }, // neon-20
            { new Nuclide(10, 11, 7.971714e6), 0.0027 }, // neon-21
            { new Nuclide(10, 12, 8.080466e6), 0.0925 } // neon-22
        });

        // ... a bunch of elements whose isotopes are not particularly interesting for boiling water ...

        AddIsotopes(Elements.BySymbol("Th"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(90, 140, 7.630997e6, 7.54e4, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 } // It also has a cluster decay mode: 24Ne = 5.8e-11%
            }), 0.0002 }, // thorium-230, it decays too quickly for any primordial amount to survive but it is a daughter nuclide
            { new Nuclide(90, 142, 7.615034e6, 1.407e10, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 },
                { DecayMode.SpontaneousFission, 1.1e-11 }
            }), 0.9998 }, // thorium-232
            { new Nuclide(90, 143, 7.602894e6, 1309.8 / Constants.year, new Dictionary<DecayMode, double>
            {
                { DecayMode.BetaMinusDecay, 1.0 }
            }), 0.0 } // thorium-233
        });
        AddIsotopes(Elements.BySymbol("Pa"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(91, 140, 7.618427e6, 3.274e4, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 }
            }), 1.0 }, // protactinium-231, exists as a daughter nuclide
            { new Nuclide(91, 142, 7.604868e6, 2330640.0 / Constants.year, new Dictionary<DecayMode, double>
            {
                { DecayMode.BetaMinusDecay, 1.0 }
            }), 0.0 } // protactinium-233
        });
        AddIsotopes(Elements.BySymbol("U"), new Dictionary<Nuclide, double>
        {
            { new Nuclide(92, 141, 7.603957e6, 1.5919e5, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 }, // It also has a cluster decay mode: 24Ne = 7.2e-11%
            }), 0.0 }, // uranium-233
            { new Nuclide(92, 142, 7.600716e6, 2.455e5, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 },
                { DecayMode.SpontaneousFission, 1.64e-11 }
                // Has also been observed to cluster decay:
                // 28Mg = 1.4e-11%
                // (unspecified)Ne = 9e-12%
            }), 0.000054 }, // uranium-234, exists as a daughter nuclide
            { new Nuclide(92, 143, 7.590915e6, 7.040e8, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 },
                { DecayMode.SpontaneousFission, 7e-11 }
                // Has also been observed to cluster decay:
                // 20Ne = 8e-10%
                // 25Ne = 8e-10%
                // 28Mg = 8e-10%
            }), 0.007204 }, // uranium-235
            { new Nuclide(92, 146, 7.570126e6, 4.463e9, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 },
                { DecayMode.SpontaneousFission, 5.44e-7 } // Relatively high
            }), 0.992742 }, // uranium-238
            { new Nuclide(92, 147, 7.558562e6, 1407.0 / Constants.year, new Dictionary<DecayMode, double>
            {
                { DecayMode.BetaMinusDecay, 1.0 }
            }), 0.0 } // uranium-239
        });
        AddIsotopes(Elements.BySymbol("Np"), new Dictionary<Nuclide, double> // abundances are meaningless because amounts created from uranium in the natural environment are too low to measure
        {
            { new Nuclide(93, 144, 7.574990e6, 2.144e6, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 }
            }), 0.0 }, // neptunium-237
            { new Nuclide(93, 146, 7.560568e6, 203541.12 / Constants.year, new Dictionary<DecayMode, double>
            {
                { DecayMode.BetaMinusDecay, 1.0 }
            }), 0.0 } // neptunium-239
        });
        AddIsotopes(Elements.BySymbol("Pu"), new Dictionary<Nuclide, double> // abundances are meaningless
        {
            { new Nuclide(94, 144, 7.568361e6, 87.7, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 },
                { DecayMode.SpontaneousFission, 1.9e9 }
                // Has also been observed to cluster decay:
                // Si = 1.4e-14%
                // Mg = 6e-15%
            }), 0.0 }, // plutonium-238
            { new Nuclide(94, 145, 7.560319e6, 2.4109e4, new Dictionary<DecayMode, double>
            {
                { DecayMode.AlphaDecay, 1.0 },
                { DecayMode.SpontaneousFission, 3.01e-12 }
            }), 0.0 }, // plutonium-239
        });
    }
}

public class NuclideResource
{
    public Nuclide nuclide;
    public double n; // mol
}
