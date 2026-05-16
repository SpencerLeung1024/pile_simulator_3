using Godot;
using System;
using System.Collections.Generic;

public class Element
{
    public string Name; // Element name, as defined by IUPAC. The first letter is uppercase in this string. e.g. "Hydrogen", "Helium", "Lithium"
    public string Symbol; // Element symbol, as defined by IUPAC. e.g. "H", "He", "Li"
    public uint Z; // Atomic number, number of protons
    public float A_r; // Standard atomic weight
    // Technically a dimensionless number but can be converted to the actual mass of the average atom by multiplying by one dalton
    // IUPAC provides a range for A_r, but Pile Simulator 3 uses the conventional atomic weight (up to 5 significant figures) with no uncertainty
    // There is also the problem that standard atomic weights are defined based on isotopes on Earth, which causes problems in Pile Simulator 3 since you're mining asteroids
    // Most argon in space is argon-36, but terrestrial argon is dominated by argon-40 from radioactive decay of potassium-40, so A_r is 39.95
}

public class Elements
{
    public static Element[] list = new Element[]
    {
        // https://en.wikipedia.org/wiki/Standard_atomic_weight#List_of_atomic_weights
        new Element() { Name = "Hydrogen",      Symbol = "H",  Z = 1,   A_r = 1.008f  },
        new Element() { Name = "Helium",        Symbol = "He", Z = 2,   A_r = 4.0026f },
        
        new Element() { Name = "Lithium",       Symbol = "Li", Z = 3,   A_r = 6.94f   },
        new Element() { Name = "Beryllium",     Symbol = "Be", Z = 4,   A_r = 9.0122f },
        new Element() { Name = "Boron",         Symbol = "B",  Z = 5,   A_r = 10.81f  },
        new Element() { Name = "Carbon",        Symbol = "C",  Z = 6,   A_r = 12.011f },
        new Element() { Name = "Nitrogen",      Symbol = "N",  Z = 7,   A_r = 14.007f },
        new Element() { Name = "Oxygen",        Symbol = "O",  Z = 8,   A_r = 15.999f },
        new Element() { Name = "Fluorine",      Symbol = "F",  Z = 9,   A_r = 18.998f },
        new Element() { Name = "Neon",          Symbol = "Ne", Z = 10,  A_r = 20.180f },
        
        new Element() { Name = "Sodium",        Symbol = "Na", Z = 11,  A_r = 22.990f },
        new Element() { Name = "Magnesium",     Symbol = "Mg", Z = 12,  A_r = 24.305f },
        new Element() { Name = "Aluminium",     Symbol = "Al", Z = 13,  A_r = 26.982f }, // Preferred over aluminum
        new Element() { Name = "Silicon",       Symbol = "Si", Z = 14,  A_r = 28.085f },
        new Element() { Name = "Phosphorus",    Symbol = "P",  Z = 15,  A_r = 30.974f },
        new Element() { Name = "Sulfur",        Symbol = "S",  Z = 16,  A_r = 32.06f  }, // Preferred over sulphur
        new Element() { Name = "Chlorine",      Symbol = "Cl", Z = 17,  A_r = 35.45f  },
        new Element() { Name = "Argon",         Symbol = "Ar", Z = 18,  A_r = 39.95f  },
        
        new Element() { Name = "Potassium",     Symbol = "K",  Z = 19,  A_r = 39.098f },
        new Element() { Name = "Calcium",       Symbol = "Ca", Z = 20,  A_r = 40.078f },
        new Element() { Name = "Scandium",      Symbol = "Sc", Z = 21,  A_r = 44.956f },
        new Element() { Name = "Titanium",      Symbol = "Ti", Z = 22,  A_r = 47.867f },
        new Element() { Name = "Vanadium",      Symbol = "V",  Z = 23,  A_r = 50.942f },
        new Element() { Name = "Chromium",      Symbol = "Cr", Z = 24,  A_r = 51.996f },
        new Element() { Name = "Manganese",     Symbol = "Mn", Z = 25,  A_r = 54.938f },
        new Element() { Name = "Iron",          Symbol = "Fe", Z = 26,  A_r = 55.845f },
        new Element() { Name = "Cobalt",        Symbol = "Co", Z = 27,  A_r = 58.933f },
        new Element() { Name = "Nickel",        Symbol = "Ni", Z = 28,  A_r = 58.693f },
        new Element() { Name = "Copper",        Symbol = "Cu", Z = 29,  A_r = 63.546f },
        new Element() { Name = "Zinc",          Symbol = "Zn", Z = 30,  A_r = 65.38f  },
        new Element() { Name = "Gallium",       Symbol = "Ga", Z = 31,  A_r = 69.723f },
        new Element() { Name = "Germanium",     Symbol = "Ge", Z = 32,  A_r = 72.630f },
        new Element() { Name = "Arsenic",       Symbol = "As", Z = 33,  A_r = 74.922f },
        new Element() { Name = "Selenium",      Symbol = "Se", Z = 34,  A_r = 78.971f },
        new Element() { Name = "Bromine",       Symbol = "Br", Z = 35,  A_r = 79.904f },
        new Element() { Name = "Krypton",       Symbol = "Kr", Z = 36,  A_r = 83.798f },

        new Element() { Name = "Rubidium",      Symbol = "Rb", Z = 37,  A_r = 85.468f },
        new Element() { Name = "Strontium",     Symbol = "Sr", Z = 38,  A_r = 87.62f  },
        new Element() { Name = "Yttrium",       Symbol = "Y",  Z = 39,  A_r = 88.906f },
        new Element() { Name = "Zirconium",     Symbol = "Zr", Z = 40,  A_r = 91.222f },
        new Element() { Name = "Niobium",       Symbol = "Nb", Z = 41,  A_r = 92.906f },
        new Element() { Name = "Molybdenum",    Symbol = "Mo", Z = 42,  A_r = 95.95f  },
        new Element() { Name = "Technetium",    Symbol = "Tc", Z = 43,  A_r = 97.0f   }, // No stable isotopes and no natural occurrence, so A_r of a synthetic element is not defined by IUPAC. The actual atomic weight of your sample depends on how you produced it. For the purpose of this code we will use the most stable isotope
        new Element() { Name = "Ruthenium",     Symbol = "Ru", Z = 44,  A_r = 101.07f },
        new Element() { Name = "Rhodium",       Symbol = "Rh", Z = 45,  A_r = 102.91f },
        new Element() { Name = "Palladium",     Symbol = "Pd", Z = 46,  A_r = 106.42f },
        new Element() { Name = "Silver",        Symbol = "Ag", Z = 47,  A_r = 107.87f },
        new Element() { Name = "Cadmium",       Symbol = "Cd", Z = 48,  A_r = 112.41f },
        new Element() { Name = "Indium",        Symbol = "In", Z = 49,  A_r = 114.82f },
        new Element() { Name = "Tin",           Symbol = "Sn", Z = 50,  A_r = 118.71f },
        new Element() { Name = "Antimony",      Symbol = "Sb", Z = 51,  A_r = 121.76f },
        new Element() { Name = "Tellurium",     Symbol = "Te", Z = 52,  A_r = 127.60f },
        new Element() { Name = "Iodine",        Symbol = "I",  Z = 53,  A_r = 126.90f },
        new Element() { Name = "Xenon",         Symbol = "Xe", Z = 54,  A_r = 131.29f },

        new Element() { Name = "Caesium",       Symbol = "Cs", Z = 55,  A_r = 132.91f }, // Preferred over cesium
        new Element() { Name = "Barium",        Symbol = "Ba", Z = 56,  A_r = 137.33f },
        new Element() { Name = "Lanthanum",     Symbol = "La", Z = 57,  A_r = 138.91f },
        new Element() { Name = "Cerium",        Symbol = "Ce", Z = 58,  A_r = 140.12f },
        new Element() { Name = "Praseodymium",  Symbol = "Pr", Z = 59,  A_r = 140.91f },
        new Element() { Name = "Neodymium",     Symbol = "Nd", Z = 60,  A_r = 144.24f },
        new Element() { Name = "Promethium",    Symbol = "Pm", Z = 61,  A_r = 145.0f  }, // Synthetic
        new Element() { Name = "Samarium",      Symbol = "Sm", Z = 62,  A_r = 150.36f },
        new Element() { Name = "Europium",      Symbol = "Eu", Z = 63,  A_r = 151.96f },
        new Element() { Name = "Gadolinium",    Symbol = "Gd", Z = 64,  A_r = 157.25f },
        new Element() { Name = "Terbium",       Symbol = "Tb", Z = 65,  A_r = 158.93f },
        new Element() { Name = "Dysprosium",    Symbol = "Dy", Z = 66,  A_r = 162.50f },
        new Element() { Name = "Holmium",       Symbol = "Ho", Z = 67,  A_r = 164.93f },
        new Element() { Name = "Erbium",        Symbol = "Er", Z = 68,  A_r = 167.26f },
        new Element() { Name = "Thulium",       Symbol = "Tm", Z = 69,  A_r = 168.93f },
        new Element() { Name = "Ytterbium",     Symbol = "Yb", Z = 70,  A_r = 173.05f },
        new Element() { Name = "Lutetium",      Symbol = "Lu", Z = 71,  A_r = 174.97f },
        new Element() { Name = "Hafnium",       Symbol = "Hf", Z = 72,  A_r = 178.49f },
        new Element() { Name = "Tantalum",      Symbol = "Ta", Z = 73,  A_r = 180.95f }, // Note -um
        new Element() { Name = "Tungsten",      Symbol = "W",  Z = 74,  A_r = 183.84f },
        new Element() { Name = "Rhenium",       Symbol = "Re", Z = 75,  A_r = 186.21f },
        new Element() { Name = "Osmium",        Symbol = "Os", Z = 76,  A_r = 190.23f },
        new Element() { Name = "Iridium",       Symbol = "Ir", Z = 77,  A_r = 192.22f },
        new Element() { Name = "Platinum",      Symbol = "Pt", Z = 78,  A_r = 195.08f }, // Note -um
        new Element() { Name = "Gold",          Symbol = "Au", Z = 79,  A_r = 196.97f },
        new Element() { Name = "Mercury",       Symbol = "Hg", Z = 80,  A_r = 200.59f },
        new Element() { Name = "Thallium",      Symbol = "Tl", Z = 81,  A_r = 204.38f },
        new Element() { Name = "Lead",          Symbol = "Pb", Z = 82,  A_r = 207.2f  },
        new Element() { Name = "Bismuth",       Symbol = "Bi", Z = 83,  A_r = 208.98f },
        new Element() { Name = "Polonium",      Symbol = "Po", Z = 84,  A_r = 209.0f   }, // Subsequent elements exist in radioactive decay chains as a daughter product but at levels too low to measure, so use the most stable isotope
        new Element() { Name = "Astatine",      Symbol = "At", Z = 85,  A_r = 210.0f   },
        new Element() { Name = "Radon",         Symbol = "Rn", Z = 86,  A_r = 222.0f   },
        
        new Element() { Name = "Francium",      Symbol = "Fr", Z = 87,  A_r = 223.0f   },
        new Element() { Name = "Radium",        Symbol = "Ra", Z = 88,  A_r = 226.0f   },
        new Element() { Name = "Actinium",      Symbol = "Ac", Z = 89,  A_r = 227.0f   },
        new Element() { Name = "Thorium",       Symbol = "Th", Z = 90,  A_r = 232.04f  }, // Primordial thorium has survived from the formation of the Earth
        new Element() { Name = "Protactinium",  Symbol = "Pa", Z = 91,  A_r = 231.04f  }, // Even though no protactinium has survived from the formation of the Earth, it exists in sufficient quantities as a daughter product to measure its A_r
        new Element() { Name = "Uranium",       Symbol = "U",  Z = 92,  A_r = 238.03f  }, // Primordial
        new Element() { Name = "Neptunium",     Symbol = "Np", Z = 93,  A_r = 237.0f   }, // All subsequent elements are synthetic
        new Element() { Name = "Plutonium",     Symbol = "Pu", Z = 94,  A_r = 244.0f   },
        new Element() { Name = "Americium",     Symbol = "Am", Z = 95,  A_r = 243.0f   },
        new Element() { Name = "Curium",        Symbol = "Cm", Z = 96,  A_r = 247.0f   },
        new Element() { Name = "Berkelium",     Symbol = "Bk", Z = 97,  A_r = 247.0f   },
        new Element() { Name = "Californium",   Symbol = "Cf", Z = 98,  A_r = 251.0f   },
        new Element() { Name = "Einsteinium",   Symbol = "Es", Z = 99,  A_r = 252.0f   },
        new Element() { Name = "Fermium",       Symbol = "Fm", Z = 100, A_r = 257.0f   },
        new Element() { Name = "Mendelevium",   Symbol = "Md", Z = 101, A_r = 258.0f   },
        new Element() { Name = "Nobelium",      Symbol = "No", Z = 102, A_r = 259.0f   },
        new Element() { Name = "Lawrencium",    Symbol = "Lr", Z = 103, A_r = 266.0f   },
        new Element() { Name = "Rutherfordium", Symbol = "Rf", Z = 104, A_r = 267.0f   },
        new Element() { Name = "Dubnium",       Symbol = "Db", Z = 105, A_r = 268.0f   },
        new Element() { Name = "Seaborgium",    Symbol = "Sg", Z = 106, A_r = 269.0f   },
        new Element() { Name = "Bohrium",       Symbol = "Bh", Z = 107, A_r = 270.0f   },
        new Element() { Name = "Hassium",       Symbol = "Hs", Z = 108, A_r = 271.0f   },
        new Element() { Name = "Meitnerium",    Symbol = "Mt", Z = 109, A_r = 278.0f   },
        new Element() { Name = "Darmstadtium",  Symbol = "Ds", Z = 110, A_r = 281.0f   },
        new Element() { Name = "Roentgenium",   Symbol = "Rg", Z = 111, A_r = 282.0f   },
        new Element() { Name = "Copernicium",   Symbol = "Cn", Z = 112, A_r = 285.0f   },
        new Element() { Name = "Nihonium",      Symbol = "Nh", Z = 113, A_r = 286.0f   },
        new Element() { Name = "Flerovium",     Symbol = "Fl", Z = 114, A_r = 289.0f   },
        new Element() { Name = "Moscovium",     Symbol = "Mc", Z = 115, A_r = 290.0f   },
        new Element() { Name = "Livermorium",   Symbol = "Lv", Z = 116, A_r = 293.0f   },
        new Element() { Name = "Tennessine",    Symbol = "Ts", Z = 117, A_r = 294.0f   },
        new Element() { Name = "Oganesson",     Symbol = "Og", Z = 118, A_r = 294.0f   }

        // TODO: Ask for more funding for particle colliders so we can make more elements
    };

    public static Dictionary<string, Element> nameToElement = new Dictionary<string, Element>();
    public static Dictionary<string, Element> symbolToElement = new Dictionary<string, Element>();
    public static Dictionary<uint, Element> zToElement = new Dictionary<uint, Element>();

    // Remember to call this somewhere when the game starts
    public static void BuildIndexes()
    {
        foreach (Element element in list)
        {
            nameToElement[element.Name] = element;
            symbolToElement[element.Symbol] = element;
            zToElement[element.Z] = element;
        }
    }

    public static Element ByName(string name)
    {
        return nameToElement[name];
    }

    public static Element BySymbol(string symbol)
    {
        return symbolToElement[symbol];
    }

    public static Element ByZ(uint z)
    {
        return zToElement[z];
    }
}
