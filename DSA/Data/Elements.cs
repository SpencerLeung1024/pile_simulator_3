using System;
using System.Collections.Generic;

public class Element
{
    public string Name; // Element name, as defined by IUPAC. The first letter is lowercase in this string. e.g. "hydrogen", "helium", "lithium"
    public string Symbol; // Element symbol, as defined by IUPAC. e.g. "H", "He", "Li"
    public uint Z; // Atomic number, number of protons
    public double A_r; // Standard atomic weight
    // Technically a dimensionless number but can be converted to the actual mass of the average atom by multiplying by one dalton
    // IUPAC provides a range for A_r, but Pile Simulator 3 uses the conventional atomic weight (up to 5 significant figures) with no uncertainty
    // There is also the problem that standard atomic weights are defined based on isotopes on Earth, which causes problems in Pile Simulator 3 since you're mining asteroids
    // Most argon in space is argon-36, but terrestrial argon is dominated by argon-40 from radioactive decay of potassium-40, so A_r is 39.95
    // Stored as a double even though floats have enough precision for 5 decimal digits, because we need to work with doubles in moles and conservation of mass
}

public class Elements
{
    public static Element[] list = new Element[]
    {
        // https://en.wikipedia.org/wiki/Standard_atomic_weight#List_of_atomic_weights
        new Element() { Name = "hydrogen",      Symbol = "H",  Z = 1,   A_r = 1.008  },
        new Element() { Name = "helium",        Symbol = "He", Z = 2,   A_r = 4.0026 },
        
        new Element() { Name = "lithium",       Symbol = "Li", Z = 3,   A_r = 6.94   },
        new Element() { Name = "beryllium",     Symbol = "Be", Z = 4,   A_r = 9.0122 },
        new Element() { Name = "boron",         Symbol = "B",  Z = 5,   A_r = 10.81  },
        new Element() { Name = "carbon",        Symbol = "C",  Z = 6,   A_r = 12.011 },
        new Element() { Name = "nitrogen",      Symbol = "N",  Z = 7,   A_r = 14.007 },
        new Element() { Name = "oxygen",        Symbol = "O",  Z = 8,   A_r = 15.999 },
        new Element() { Name = "fluorine",      Symbol = "F",  Z = 9,   A_r = 18.998 },
        new Element() { Name = "neon",          Symbol = "Ne", Z = 10,  A_r = 20.180 },
        
        new Element() { Name = "sodium",        Symbol = "Na", Z = 11,  A_r = 22.990 },
        new Element() { Name = "magnesium",     Symbol = "Mg", Z = 12,  A_r = 24.305 },
        new Element() { Name = "aluminium",     Symbol = "Al", Z = 13,  A_r = 26.982 }, // Preferred over aluminum
        new Element() { Name = "silicon",       Symbol = "Si", Z = 14,  A_r = 28.085 },
        new Element() { Name = "phosphorus",    Symbol = "P",  Z = 15,  A_r = 30.974 },
        new Element() { Name = "sulfur",        Symbol = "S",  Z = 16,  A_r = 32.06  }, // Preferred over sulphur
        new Element() { Name = "chlorine",      Symbol = "Cl", Z = 17,  A_r = 35.45  },
        new Element() { Name = "argon",         Symbol = "Ar", Z = 18,  A_r = 39.95  },
        
        new Element() { Name = "potassium",     Symbol = "K",  Z = 19,  A_r = 39.098 },
        new Element() { Name = "calcium",       Symbol = "Ca", Z = 20,  A_r = 40.078 },
        new Element() { Name = "scandium",      Symbol = "Sc", Z = 21,  A_r = 44.956 },
        new Element() { Name = "titanium",      Symbol = "Ti", Z = 22,  A_r = 47.867 },
        new Element() { Name = "vanadium",      Symbol = "V",  Z = 23,  A_r = 50.942 },
        new Element() { Name = "chromium",      Symbol = "Cr", Z = 24,  A_r = 51.996 },
        new Element() { Name = "manganese",     Symbol = "Mn", Z = 25,  A_r = 54.938 },
        new Element() { Name = "iron",          Symbol = "Fe", Z = 26,  A_r = 55.845 },
        new Element() { Name = "cobalt",        Symbol = "Co", Z = 27,  A_r = 58.933 },
        new Element() { Name = "nickel",        Symbol = "Ni", Z = 28,  A_r = 58.693 },
        new Element() { Name = "copper",        Symbol = "Cu", Z = 29,  A_r = 63.546 },
        new Element() { Name = "zinc",          Symbol = "Zn", Z = 30,  A_r = 65.38  },
        new Element() { Name = "gallium",       Symbol = "Ga", Z = 31,  A_r = 69.723 },
        new Element() { Name = "germanium",     Symbol = "Ge", Z = 32,  A_r = 72.630 },
        new Element() { Name = "arsenic",       Symbol = "As", Z = 33,  A_r = 74.922 },
        new Element() { Name = "selenium",      Symbol = "Se", Z = 34,  A_r = 78.971 },
        new Element() { Name = "bromine",       Symbol = "Br", Z = 35,  A_r = 79.904 },
        new Element() { Name = "krypton",       Symbol = "Kr", Z = 36,  A_r = 83.798 },

        new Element() { Name = "rubidium",      Symbol = "Rb", Z = 37,  A_r = 85.468 },
        new Element() { Name = "strontium",     Symbol = "Sr", Z = 38,  A_r = 87.62  },
        new Element() { Name = "yttrium",       Symbol = "Y",  Z = 39,  A_r = 88.906 },
        new Element() { Name = "zirconium",     Symbol = "Zr", Z = 40,  A_r = 91.222 },
        new Element() { Name = "niobium",       Symbol = "Nb", Z = 41,  A_r = 92.906 },
        new Element() { Name = "molybdenum",    Symbol = "Mo", Z = 42,  A_r = 95.95  },
        new Element() { Name = "technetium",    Symbol = "Tc", Z = 43,  A_r = 97.0   }, // No stable isotopes and no natural occurrence, so A_r of a synthetic element is not defined by IUPAC. The actual atomic weight of your sample depends on how you produced it. For the purpose of this code we will use the most stable isotope
        new Element() { Name = "ruthenium",     Symbol = "Ru", Z = 44,  A_r = 101.07 },
        new Element() { Name = "rhodium",       Symbol = "Rh", Z = 45,  A_r = 102.91 },
        new Element() { Name = "palladium",     Symbol = "Pd", Z = 46,  A_r = 106.42 },
        new Element() { Name = "silver",        Symbol = "Ag", Z = 47,  A_r = 107.87 },
        new Element() { Name = "cadmium",       Symbol = "Cd", Z = 48,  A_r = 112.41 },
        new Element() { Name = "indium",        Symbol = "In", Z = 49,  A_r = 114.82 },
        new Element() { Name = "tin",           Symbol = "Sn", Z = 50,  A_r = 118.71 },
        new Element() { Name = "antimony",      Symbol = "Sb", Z = 51,  A_r = 121.76 },
        new Element() { Name = "tellurium",     Symbol = "Te", Z = 52,  A_r = 127.60 },
        new Element() { Name = "iodine",        Symbol = "I",  Z = 53,  A_r = 126.90 },
        new Element() { Name = "xenon",         Symbol = "Xe", Z = 54,  A_r = 131.29 },

        new Element() { Name = "caesium",       Symbol = "Cs", Z = 55,  A_r = 132.91 }, // Preferred over cesium
        new Element() { Name = "barium",        Symbol = "Ba", Z = 56,  A_r = 137.33 },
        new Element() { Name = "lanthanum",     Symbol = "La", Z = 57,  A_r = 138.91 },
        new Element() { Name = "cerium",        Symbol = "Ce", Z = 58,  A_r = 140.12 },
        new Element() { Name = "praseodymium",  Symbol = "Pr", Z = 59,  A_r = 140.91 },
        new Element() { Name = "neodymium",     Symbol = "Nd", Z = 60,  A_r = 144.24 },
        new Element() { Name = "promethium",    Symbol = "Pm", Z = 61,  A_r = 145.0  }, // Synthetic
        new Element() { Name = "samarium",      Symbol = "Sm", Z = 62,  A_r = 150.36 },
        new Element() { Name = "europium",      Symbol = "Eu", Z = 63,  A_r = 151.96 },
        new Element() { Name = "gadolinium",    Symbol = "Gd", Z = 64,  A_r = 157.25 },
        new Element() { Name = "terbium",       Symbol = "Tb", Z = 65,  A_r = 158.93 },
        new Element() { Name = "dysprosium",    Symbol = "Dy", Z = 66,  A_r = 162.50 },
        new Element() { Name = "holmium",       Symbol = "Ho", Z = 67,  A_r = 164.93 },
        new Element() { Name = "erbium",        Symbol = "Er", Z = 68,  A_r = 167.26 },
        new Element() { Name = "thulium",       Symbol = "Tm", Z = 69,  A_r = 168.93 },
        new Element() { Name = "ytterbium",     Symbol = "Yb", Z = 70,  A_r = 173.05 },
        new Element() { Name = "lutetium",      Symbol = "Lu", Z = 71,  A_r = 174.97 },
        new Element() { Name = "hafnium",       Symbol = "Hf", Z = 72,  A_r = 178.49 },
        new Element() { Name = "tantalum",      Symbol = "Ta", Z = 73,  A_r = 180.95 }, // Note -um
        new Element() { Name = "tungsten",      Symbol = "W",  Z = 74,  A_r = 183.84 },
        new Element() { Name = "rhenium",       Symbol = "Re", Z = 75,  A_r = 186.21 },
        new Element() { Name = "osmium",        Symbol = "Os", Z = 76,  A_r = 190.23 },
        new Element() { Name = "iridium",       Symbol = "Ir", Z = 77,  A_r = 192.22 },
        new Element() { Name = "platinum",      Symbol = "Pt", Z = 78,  A_r = 195.08 }, // Note -um
        new Element() { Name = "gold",          Symbol = "Au", Z = 79,  A_r = 196.97 },
        new Element() { Name = "mercury",       Symbol = "Hg", Z = 80,  A_r = 200.59 },
        new Element() { Name = "thallium",      Symbol = "Tl", Z = 81,  A_r = 204.38 },
        new Element() { Name = "lead",          Symbol = "Pb", Z = 82,  A_r = 207.2  },
        new Element() { Name = "bismuth",       Symbol = "Bi", Z = 83,  A_r = 208.98 },
        new Element() { Name = "polonium",      Symbol = "Po", Z = 84,  A_r = 209.0  }, // Subsequent elements exist in radioactive decay chains as a daughter product but at levels too low to measure, so use the most stable isotope
        new Element() { Name = "astatine",      Symbol = "At", Z = 85,  A_r = 210.0  },
        new Element() { Name = "radon",         Symbol = "Rn", Z = 86,  A_r = 222.0  },
        
        new Element() { Name = "francium",      Symbol = "Fr", Z = 87,  A_r = 223.0  },
        new Element() { Name = "radium",        Symbol = "Ra", Z = 88,  A_r = 226.0  },
        new Element() { Name = "actinium",      Symbol = "Ac", Z = 89,  A_r = 227.0  },
        new Element() { Name = "thorium",       Symbol = "Th", Z = 90,  A_r = 232.04 }, // Primordial thorium has survived from the formation of the Earth
        new Element() { Name = "protactinium",  Symbol = "Pa", Z = 91,  A_r = 231.04 }, // Even though no protactinium has survived from the formation of the Earth, it exists in sufficient quantities as a daughter product to measure its A_r
        new Element() { Name = "uranium",       Symbol = "U",  Z = 92,  A_r = 238.03 }, // Primordial
        new Element() { Name = "neptunium",     Symbol = "Np", Z = 93,  A_r = 237.0  }, // All subsequent elements are synthetic
        new Element() { Name = "plutonium",     Symbol = "Pu", Z = 94,  A_r = 244.0  },
        new Element() { Name = "americium",     Symbol = "Am", Z = 95,  A_r = 243.0  },
        new Element() { Name = "curium",        Symbol = "Cm", Z = 96,  A_r = 247.0  },
        new Element() { Name = "berkelium",     Symbol = "Bk", Z = 97,  A_r = 247.0  },
        new Element() { Name = "californium",   Symbol = "Cf", Z = 98,  A_r = 251.0  },
        new Element() { Name = "einsteinium",   Symbol = "Es", Z = 99,  A_r = 252.0  },
        new Element() { Name = "fermium",       Symbol = "Fm", Z = 100, A_r = 257.0  },
        new Element() { Name = "mendelevium",   Symbol = "Md", Z = 101, A_r = 258.0  },
        new Element() { Name = "nobelium",      Symbol = "No", Z = 102, A_r = 259.0  },
        new Element() { Name = "lawrencium",    Symbol = "Lr", Z = 103, A_r = 266.0  },
        new Element() { Name = "rutherfordium", Symbol = "Rf", Z = 104, A_r = 267.0  },
        new Element() { Name = "dubnium",       Symbol = "Db", Z = 105, A_r = 268.0  },
        new Element() { Name = "seaborgium",    Symbol = "Sg", Z = 106, A_r = 269.0  },
        new Element() { Name = "bohrium",       Symbol = "Bh", Z = 107, A_r = 270.0  },
        new Element() { Name = "hassium",       Symbol = "Hs", Z = 108, A_r = 271.0  },
        new Element() { Name = "meitnerium",    Symbol = "Mt", Z = 109, A_r = 278.0  },
        new Element() { Name = "darmstadtium",  Symbol = "Ds", Z = 110, A_r = 281.0  },
        new Element() { Name = "roentgenium",   Symbol = "Rg", Z = 111, A_r = 282.0  },
        new Element() { Name = "copernicium",   Symbol = "Cn", Z = 112, A_r = 285.0  },
        new Element() { Name = "nihonium",      Symbol = "Nh", Z = 113, A_r = 286.0  },
        new Element() { Name = "flerovium",     Symbol = "Fl", Z = 114, A_r = 289.0  },
        new Element() { Name = "moscovium",     Symbol = "Mc", Z = 115, A_r = 290.0  },
        new Element() { Name = "livermorium",   Symbol = "Lv", Z = 116, A_r = 293.0  },
        new Element() { Name = "tennessine",    Symbol = "Ts", Z = 117, A_r = 294.0  },
        new Element() { Name = "oganesson",     Symbol = "Og", Z = 118, A_r = 294.0  }

        // TODO: Ask for more funding for particle colliders so we can make more elements
    };

    public static Dictionary<string, Element> nameToElement = new Dictionary<string, Element>();
    public static Dictionary<string, Element> symbolToElement = new Dictionary<string, Element>();

    private static void BuildIndexes()
    {
        foreach (Element element in list)
        {
            nameToElement[element.Name] = element;
            symbolToElement[element.Symbol] = element;
        }
    }

    // Remember to call this somewhere when the game starts
    public static void Initialize()
    {
        // No external file needs to be loaded
        BuildIndexes();
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
        return list[z-1];
    }
}
