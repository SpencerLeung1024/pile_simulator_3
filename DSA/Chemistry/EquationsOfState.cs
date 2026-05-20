using System;

public abstract class EquationOfState
{
    public SpeciesPhase SpeciesPhase; // Parent reference for heat capacity function and phase enum

    public abstract double GetU(double T, double v); // J / mol, molar internal energy
    public abstract double GetP(double T, double v); // Pa, pressure
    public abstract double Getv(double T, double P); // m^3 / mol, molar volume
    public abstract double GetLogphi(double T, double P, double v); // dimensionless, ln(ϕ) where ϕ is the fugacity coefficient
}

public class IdealGasEquation : EquationOfState
{
    public override double GetU(double T, double v)
    {
        // https://en.wikipedia.org/wiki/Mayer%27s_relation
        // c_p - c_v = R so c_v = c_p - R
        double c_p = SpeciesPhase.HeatCapacityFunction.Getc_p(T);
        return (c_p - Constants.R) * T;
    }
    public override double GetP(double T, double v)
    {
        return (Constants.R * T) / v;
    }

    public override double Getv(double T, double P)
    {
        return (Constants.R * T) / P;
    }

    public override double GetLogphi(double T, double P, double v)
    {
        return 0.0; // By definition
    }
}

// Used for solids, and for liquids when gases use the ideal gas law
public class IncompressiblePhaseEquation : EquationOfState
{
    public double v; // m^3 / mol, constant molar volume

    public override double GetU(double T, double v)
    {
        // For an incompressible phase:
        // U = H - PV
        // But P comes from the environment and we don't have it
        double H = SpeciesPhase.HeatCapacityFunction.GetH(T);
        return H; // Just assume the pressure-volume term is zero
    }
    public override double GetP(double T, double v)
    {
        return 0.0;
    }

    public override double Getv(double T, double P)
    {
        return v;
    }

    public override double GetLogphi(double T, double P, double v)
    {
        return 0.0; // By definition
    }
}

public abstract class CubicEquationOfState : EquationOfState
{
    public double T_c; // K, critical temperature
    public double P_c; // Pa, critical pressure
    public double v_c; // m^3 / mol, critical molar volume

    // Each subclass overrides these
    public abstract double GetA(double T, double P);
    public abstract double GetB(double T, double P);
    public abstract double[] GetZRoots(double T, double P);
    
    // Returns real roots in ascending order (1 or 3 roots)
    public static double[] SolveCubicRealRoots(double a, double b, double c, double d)
    {
        // Normalize: x^3 + A*x^2 + B*x + C = 0
        double A = b / a;
        double B = c / a;
        double C = d / a;

        // Depress: substitute x = t - A/3 → t^3 + p*t + q = 0
        double p = B - A*A / 3.0;
        double q = (2.0*A*A*A / 27.0) - (A*B / 3.0) + C;

        // Discriminant: Δ = (q/2)^2 + (p/3)^3
        double disc = (q*q / 4.0) + (p*p*p / 27.0);

        if (disc > 0)
        {
            // One real root
            double sqrtDisc = Math.Sqrt(disc);
            double u = Math.Cbrt(-q/2.0 + sqrtDisc);
            double v = Math.Cbrt(-q/2.0 - sqrtDisc);
            double t1 = u + v;
            return new double[] { t1 - A/3.0 };
        }
        else if (disc < 0)
        {
            // Three real roots (trigonometric solution)
            double r = Math.Sqrt(-p*p*p / 27.0);
            double phi = Math.Acos(-q / (2.0 * Math.Sqrt(-p*p*p / 27.0)));
            double t0 = 2.0 * Math.Cbrt(-p/3.0) * Math.Cos(phi/3.0);
            double t1 = 2.0 * Math.Cbrt(-p/3.0) * Math.Cos((phi + 2.0*Math.PI)/3.0);
            double t2 = 2.0 * Math.Cbrt(-p/3.0) * Math.Cos((phi + 4.0*Math.PI)/3.0);
            double[] ts = new double[] { t0, t1, t2 };
            System.Array.Sort(ts);
            double shift = A/3.0;
            return new double[] { ts[0] - shift, ts[1] - shift, ts[2] - shift };
        }
        else
        {
            // disc == 0: multiple root at the critical point
            double t1 = 2.0 * Math.Cbrt(-q/2.0);
            double t2 = -Math.Cbrt(-q/2.0);
            return new double[] { Math.Min(t1, t2) - A/3.0, Math.Max(t1, t2) - A/3.0 };
        }
    }

    public double GetUIdeal(double T, double v)
    {
        // Just the ideal gas equation
        double c_p = SpeciesPhase.HeatCapacityFunction.Getc_p(T);
        return (c_p - Constants.R) * T;
    }

    public abstract double GetUDeparture(double T, double v);

    // The user-facing methods
    public override double GetU(double T, double v)
    {
        return GetUIdeal(T, v) + GetUDeparture(T, v);
    }

    public override double Getv(double T, double P)
    {
        double[] Zroots = GetZRoots(T, P);
        double selectedZ = Zroots[0];
        if (SpeciesPhase.Phase == Phase.Gas)
        {
            selectedZ = Zroots[Zroots.Length - 1]; // Largest root is gas phase
        }
        // Otherwise the smallest root is the condensed phase

        // PV = ZnRT
        // Pv = ZRT
        // v = ZRT / P
        return (selectedZ * Constants.R * T) / P;
    }
}

public class VanDerWaalsEquation : CubicEquationOfState
{
    public override double GetA(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        // I might write down the steps in a doc or draw another diagram
        // It's algebra from the definition of a and A
        return (27.0 / 64.0) * P_r / (T_r * T_r);
    }

    public override double GetB(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return (1.0 / 8.0) * P_r / T_r;
    }

    public override double[] GetZRoots(double T, double P)
    {
        // Z^3 - (1 + B) Z^2 + A Z - AB = 0
        double A = GetA(T, P);
        double B = GetB(T, P);
        double c3 = 1.0;
        double c2 = -(1.0 + B);
        double c1 = A;
        double c0 = -A * B;
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }

    public override double GetUDeparture(double T, double v)
    {
        // After the rewrite, a is no longer stored anywhere so we have to recalculate it
        double a = (27.0 / 64.0) * (Constants.R * T_c) * (Constants.R * T_c) / P_c;
        return -a / v;
    }

    public override double GetP(double T, double v)
    {
        double T_r = T / T_c;
        double v_r = v / v_c;
        double P_r = (8.0 * T_r) / (3.0 * v_r - 1.0) - (3.0 / (v_r * v_r));
        return P_r * P_c;
    }

    public override double GetLogphi(double T, double P, double v)
    {
        double Z = P * v / (Constants.R * T);
        double A = GetA(T, P);
        double B = GetB(T, P);
        return Z - 1 - Math.Log(Z - B) - A / Z;
    }
}

public class RedlichKwongEquation : CubicEquationOfState
{
    // A and B are dimensionally the same as vdW, but with different coefficients
    public override double GetA(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return 0.42748 * P_r / (T_r * T_r);
    }

    public override double GetB(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return 0.08664 * P_r / T_r;
    }

    public override double[] GetZRoots(double T, double P)
    {
        // Z^3 - Z^2 + (A - B - B^2) Z - AB = 0
        double A = GetA(T, P);
        double B = GetB(T, P);
        double c3 = 1.0;
        double c2 = -1.0;
        double c1 = A - B - B * B;
        double c0 = -A * B;
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }

    public override double GetUDeparture(double T, double v)
    {
        double a = 0.42748 * Constants.R * Constants.R * Math.Pow(T_c, 2.5) / P_c; // Note the T_c^2.5
        double b = 0.8664 * Constants.R * T_c / P_c;
        return - (3.0 * a) / (2.0 * Math.Sqrt(T) * b) * Math.Log((v + b) / v);
    }

    public override double GetP(double T, double v)
    {
        double T_r = T / T_c;
        double v_r = v / v_c;
        double b2 = 0.25992; // A magic number only used here
        double P_r = (3.0 * T_r) / (v_r - b2) - 1.0 / (b2 * Math.Sqrt(T_r) * v_r * (v_r + b2));
        return P_r * P_c;
    }

    public override double GetLogphi(double T, double P, double v)
    {
        double Z = P * v / (Constants.R * T);
        double A = GetA(T, P);
        double B = GetB(T, P);
        return Z - 1 - Math.Log(Z - B) - (A / B) * Math.Log((Z + B) / Z);
    }
}

public class SoaveRedlichKwongEquation : CubicEquationOfState
{
    // Now with alpha
    // Which depends on m, a quadratic function of the acentric factor
    public double Getm(double T)
    {
        double omega = SpeciesPhase.Species.omega; // The acentric factor is stored in the species
        return 0.48508 + 1.55171 * omega - 0.15613 * omega * omega;
    }

    public double Getalpha(double T)
    {
        double m = Getm(T);
        return Math.Pow(1 + m * (1 - Math.Sqrt(T / T_c)), 2);
    }

    public override double GetA(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return 0.42748 * Getalpha(T) * P_r / (T_r * T_r);
    }

    public override double GetB(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return 0.08664 * P_r / T_r;
    }

    public override double[] GetZRoots(double T, double P)
    {
        // Same as RK
        // Z^3 - Z^2 + (A - B - B^2) Z - AB = 0
        double A = GetA(T, P);
        double B = GetB(T, P);
        double c3 = 1.0;
        double c2 = -1.0;
        double c1 = A - B - B * B;
        double c0 = -A * B;
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }

    public override double GetUDeparture(double T, double v)
    {
        double a = 0.42748 * Constants.R * Constants.R * T_c * T_c / P_c; // Note we are back to T_c^2. alpha absorbed the temperature dependence
        double b = 0.08664 * Constants.R * T_c / P_c;
        double m = Getm(T);
        double alpha = Getalpha(T);
        double dAlphaT_dT = alpha - m * Math.Sqrt(alpha * T / T_c);
        return - (a / b) * dAlphaT_dT * Math.Log((v + b) / v);
    }

    public override double GetP(double T, double v)
    {
        double T_r = T / T_c;
        double v_r = v / v_c;
        double Z_c = 0.33333; // compressibility factor at the critical point, (P_c * v_c) / (R * T_c)
        double P_r = T_r / (Z_c * (v_r - (0.08664 / Z_c))) - ((0.42748 / (Z_c * Z_c)) * Getalpha(T)) / (v_r * (v_r + (0.08664 / Z_c)));
        return P_r * P_c;
    }

    public override double GetLogphi(double T, double P, double v)
    {
        // Same as RK
        double Z = P * v / (Constants.R * T);
        double A = GetA(T, P);
        double B = GetB(T, P);
        return Z - 1 - Math.Log(Z - B) - (A / B) * Math.Log((Z + B) / Z);
    }
}

public class PengRobinsonEquation : CubicEquationOfState
{
    // m has different coefficients
    public double Getm(double T)
    {
        double omega = SpeciesPhase.Species.omega;
        return 0.37464 + 1.54226 * omega - 0.26992 * omega * omega;
    }

    public double Getalpha(double T)
    {
        double m = Getm(T);
        return Math.Pow(1 + m * (1 - Math.Sqrt(T / T_c)), 2);
    }

    public override double GetA(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return 0.42748 * Getalpha(T) * P_r / (T_r * T_r);
    }

    public override double GetB(double T, double P)
    {
        double T_r = T / T_c;
        double P_r = P / P_c;
        return 0.08664 * P_r / T_r;
    }

    public override double[] GetZRoots(double T, double P)
    {
        // Z^3 - (1 - B) Z^2 + (A - 2B - 3B^2) Z - (AB - B^2 - B^3) = 0
        double A = GetA(T, P);
        double B = GetB(T, P);
        double B2 = B * B;
        double B3 = B2 * B;
        double c3 = 1.0;
        double c2 = -(1.0 - B);
        double c1 = A - 2.0 * B - 3.0 * B2;
        double c0 = -(A * B - B2 - B3);
        return SolveCubicRealRoots(c3, c2, c1, c0);
    }

    public override double GetUDeparture(double T, double v)
    {
        double a = 0.45724 * Constants.R * Constants.R * T_c * T_c / P_c;
        double b = 0.07780 * Constants.R * T_c / P_c;
        double m = Getm(T);
        double alpha = Getalpha(T);
        double dAlphaT_dT = alpha - m * Math.Sqrt(alpha * T / T_c);
        double sqrt2 = Math.Sqrt(2.0); // Used three times so cache it
        return -(a / (2.0 * sqrt2 * b)) * dAlphaT_dT * Math.Log((v + (1.0 + sqrt2) * b) / (v + (1.0 - sqrt2) * b));
    }

    public override double GetP(double T, double v)
    {
        // There is no formula for P_r in my sources
        // Use the "normal" formula for dimensional P
        double a = 0.45724 * Constants.R * Constants.R * T_c * T_c / P_c;
        double b = 0.07780 * Constants.R * T_c / P_c;
        double alpha = Getalpha(T);
        return (Constants.R * T) / (v - b) - (alpha * a) / ((v * v) + (2.0 * b * v) - (b * b));
    }

    public override double GetLogphi(double T, double P, double v)
    {
        double Z = P * v / (Constants.R * T);
        double A = GetA(T, P);
        double B = GetB(T, P);
        double sqrt2 = Math.Sqrt(2.0);
        return Z - 1 - Math.Log(Z - B) - A / (2.0 * sqrt2 * B) * Math.Log((Z + (1.0 + sqrt2) * B) / (Z + (1.0 - sqrt2) * B));
    }
}
