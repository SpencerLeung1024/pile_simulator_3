using Godot;
using System;
using System.Collections.Generic;

public abstract class HeatCapacityFunction
{
    public abstract double Getc_p(double T); // J / (K * mol), molar heat capacity at constant pressure
    public abstract double GetH(double T); // J / mol, molar enthalpy relative to standard enthalpy of formation
    public abstract double GetS(double T); // J / (K * mol), molar entropy relative to standard entropy
}

public class ConstantHeatCapacityFunction : HeatCapacityFunction
{
    public double c_p;
    public double StandardEnthalpyOfFormation;
    public double StandardEntropy;
    public double StandardTemperature; // K, used to know where the standard enthalpy of formation and standard entropy are measured
    public override double Getc_p(double T)
    {
        return c_p;
    }
    public override double GetH(double T)
    {
        // H(T) = H(T_ref) + integral from T_ref to T of c_p(T) dT
        // c_p is constant, so c_p * integral from T_ref to T of 1 dT
        // H(T) = H(T_ref) + c_p * (T - T_ref)
        return StandardEnthalpyOfFormation + c_p * (T - StandardTemperature);
    }
    public override double GetS(double T)
    {
        // S(T) = S(T_ref) + integral from T_ref to T of c_p(T) / T dT
        // c_p is constant, so c_p * integral from T_ref to T of 1 / T dT
        // Antiderivative of 1 / T is ln(T)
        // S(T) = S(T_ref) + c_p * (ln(T) - ln(T_ref))
        // ln(a) - ln(b) = ln(a / b)
        // This lets us do one expensive transcendental call instead of two
        return StandardEntropy + c_p * Math.Log(T / StandardTemperature);
    }
}

// Reuse the same coefficient selection logic
public abstract class MultiTemperatureFunction: HeatCapacityFunction
{
    public double[] TemperatureBoundaries; // K, e.g. [200.0, 1000.0, 6000.0]
    public List<double[]> all_vec_a; // num zones x num coefficients, e.g. 2 x 7 for NASA7

    protected double[] Getvec_a(double T, bool AllowOutOfRange = true)
    {
        if (T < TemperatureBoundaries[0] && !AllowOutOfRange)
        {
            throw new ArgumentOutOfRangeException($"Temperature {T} is below minimum temperature {TemperatureBoundaries[0]}");
        }
        int a_index = 0;
        while (a_index < TemperatureBoundaries.Length - 1 && T > TemperatureBoundaries[a_index+1])
        {
            a_index++;
        }
        if (T > TemperatureBoundaries[TemperatureBoundaries.Length - 1] && !AllowOutOfRange)
        {
            throw new ArgumentOutOfRangeException($"Temperature {T} is above maximum temperature {TemperatureBoundaries[TemperatureBoundaries.Length - 1]}");
        }
        return all_vec_a[a_index];
    }
}

public class NASA7Function : MultiTemperatureFunction
{
    public override double Getc_p(double T)
    {
        // NASA7 as written in manuals gives c_p / R
        double[] a = Getvec_a(T);
        double T2 = T * T;
        double T3 = T2 * T;
        double T4 = T3 * T;
        return Constants.R * (a[0] + a[1] * T + a[2] * T2 + a[3] * T3 + a[4] * T4);
    }

    public override double GetH(double T)
    {
        // As written gives H / RT
        // a[5] = a_6 is the constant of integration and shifts H so the standard enthalpy of formation is included
        double[] a = Getvec_a(T);
        double T2 = T * T;
        double T3 = T2 * T;
        double T4 = T3 * T;
        return Constants.R * T * (a[0] + a[1] * T / 2 + a[2] * T2 / 3 + a[3] * T3 / 4 + a[4] * T4 / 5 + a[5] / T);
    }

    public override double GetS(double T)
    {
        // As written gives S / R
        // a[6] = a_7 does similarly for standard entropy
        double[] a = Getvec_a(T);
        double T2 = T * T;
        double T3 = T2 * T;
        double T4 = T3 * T;
        return Constants.R * (a[0] * Math.Log(T) + a[1] * T + a[2] * T2 / 2 + a[3] * T3 / 3 + a[4] * T4 / 4 + a[6]);
    }
}

public class NASA9Function : MultiTemperatureFunction
{
    // Now with 28% more coefficients!

    // Note: NASA9's a[0] and a[1] (a_1 and a_2) are *low temperature* coefficients
    // All the NASA7 coefficients have been shifted up by two
    public override double Getc_p(double T)
    {
        double[] a = Getvec_a(T);
        double T2 = T * T;
        double T3 = T2 * T;
        double T4 = T3 * T;
        return Constants.R * (a[0] / T2 + a[1] / T + a[2] + a[3] * T + a[4] * T2 + a[5] * T3 + a[6] * T4);
    }

    public override double GetH(double T)
    {
        // Note: a[0] = a_1 is used as a negative
        double[] a = Getvec_a(T);
        double T2 = T * T;
        double T3 = T2 * T;
        double T4 = T3 * T;
        return Constants.R * T * (-a[0] / T2 + a[1] * Math.Log(T) / T + a[2] + a[3] * T / 2 + a[4] * T2 / 3 + a[5] * T3 / 4 + a[6] * T4 / 5 + a[7] / T);
    }

    public override double GetS(double T)
    {
        // Note: a[0] and a[1] = a_1 and a_2 are used as negatives
        double[] a = Getvec_a(T);
        double T2 = T * T;
        double T3 = T2 * T;
        double T4 = T3 * T;
        return Constants.R * (-a[0] / (2 * T2) - a[1] / T + a[2] * Math.Log(T) + a[3] * T + a[4] * T2 / 2 + a[5] * T3 / 3 + a[6] * T4 / 4 + a[8]);
    }
}

public class ShomateFunction : MultiTemperatureFunction
{
    // NIST chemistry webbook has 2 sets of coefficients for species that have a Shomate section
    // But the temperature boundary is all over the place
    // H2O: [500, 1700, 6000]
    // CO2: [298, 1200, 6000]
    // N2O: [298, 1400, 6000]
    // N2H4: [800, 2000, 6000]

    // Note that Shomate manuals call the coefficients A, B, C, D, E, F, G, H
    // I've already called them a_i in the abstract class so I'll have to keep that
    // Also Shomate uses t = T / 1000 and returns enthalpy in kJ / mol

    public override double Getc_p(double T)
    {
        double[] a = Getvec_a(T);
        double t = T / 1000.0;
        double t2 = t * t;
        double t3 = t2 * t;
        return a[0] + a[1] * t + a[2] * t2 + a[3] * t3 + a[4] / t2;
    }

    public override double GetH(double T)
    {
        // As written gives H - H_ref
        // Coefficient a[5] = F is -H_ref
        // Just ignore a[5]
        // Note: a[4] = E is used as a negative
        double[] a = Getvec_a(T);
        double t = T / 1000.0;
        double t2 = t * t;
        double t3 = t2 * t;
        double t4 = t3 * t;
        return 1000 * (a[0] * t + a[1] * t2 / 2 + a[2] * t3 / 3 - a[3] * t4 / 4 - a[4] / t + a[7]);
    }

    public override double GetS(double T)
    {
        // Note: a[4] = E is used as a negative
        double[] a = Getvec_a(T);
        double t = T / 1000.0;
        double t2 = t * t;
        double t3 = t2 * t;
        return a[0] * Math.Log(t) + a[1] * t + a[2] * t2 / 2 + a[3] * t3 / 3 - a[4] / (2 * t2) + a[6];
    }
}