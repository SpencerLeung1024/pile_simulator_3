using Godot;
using System;

public abstract class EquationOfState
{
    public abstract float GetU(float S, float v); // J / mol, molar internal energy // TODO: Is internal energy always an extensive property (scales linearly with n)?
    public abstract float GetP(float T, float v); // Pa, pressure
    public abstract float Getv(float T, float P); // m^3 / mol, molar volume
}

public class IdealGasEquation : EquationOfState
{
    public override float GetU(float S, float v)
    {
        // https://en.wikipedia.org/wiki/Mayer%27s_relation
        // c_p - c_v = R so c_v = c_p - R
        return (c_p - Constants.R) * T;
    }
    public override float GetP(float T, float v)
    {
        return (Constants.R * T) / v;
    }

    public override float Getv(float T, float P)
    {
        return (Constants.R * T) / P;
    }
}

// Used for solids, and for liquids when gases use the ideal gas law
public class IncompressiblePhaseEquation : EquationOfState
{
    public float v; // m^3 / mol, constant molar volume

    public override float GetU(float S, float v)
    {
        // TODO
    }
    public override float GetP(float T, float v)
    {
        return 0.0f;
    }

    public override float Getv(float T, float P)
    {
        return v;
    }
}

public abstract class CubicEquationOfState : EquationOfState
{
    public Phase Phase; // Used to know which root (liquid or gas) to return when there are three real roots
    public float T_c; // K, critical temperature
    public float P_c; // Pa, critical pressure
    public float v_c; // m^3 / mol, critical molar volume
    public abstract float[] GetvRoots(float T, float P); // m^3 / mol, returns the real roots of the cubic equation for v

    // Implemented here because all four cubic EOS below use the same logic
    public override float Getv(float T, float P)
    {
        float[] vRoots = GetvRoots(T, P); // Assume smallest to largest
        if (Phase == Phase.Liquid)
        {
            if (vRoots.Length == 3)
            {
                return vRoots[0]; // The smallest root is the liquid root
            }
            else // This is a supercritical fluid
            {
                throw new Exception("No liquid root exists at these conditions");
            }
        }
        else if (Phase == Phase.Gas)
        {
            if (vRoots.Length == 3)
            {
                return vRoots[2]; // The largest root is the gas root
            }
            else // This is a supercritical fluid
            {
                throw new Exception("No gas root exists at these conditions");
            }
        }
        else if (Phase == Phase.Supercritical)
        {
            if (vRoots.Length == 3)
            {
                throw new Exception("Multiple roots exist at these conditions, so the phase cannot be determined");
            }
            else
            {
                return vRoots[0]; // The only root is the supercritical root
            }
        }
        else
        {
            throw new Exception("Cubic equations of state cannot model this phase");
        }
    }
}

public class VanDerWaalsEquation : CubicEquationOfState
{
    public float a; // attraction parameter
    public float b; // displaced volume parameter

    public void CalculateCriticalConstants()
    {
        // See `docs/chemistry/cubic_eos.md`
        a = (27.0f/64.0f) * MathF.Pow(Constants.R, 2) * MathF.Pow(T_c, 2) / P_c;
        b = (1.0f/8.0f) * Constants.R * T_c / P_c;
    }

    public override float GetP(float T, float v)
    {
        // P = ((RT) / (v-b)) - (a / v^2)
        return ((Constants.R * T) / (v - b)) - (a / MathF.Pow(v, 2));
    }

    public override float[] GetvRoots(float T, float P)
    {
        // v^3 - ((RT + Pb) / P) v^2 + (a / P) v - (ab / P) = 0
        // Coefficients of the cubic equation for v
        float c3 = 1.0f;
        float c2 = -((Constants.R * T) + (P * b)) / P;
        float c1 = a / P;
        float c0 = -(a * b) / P;

        // TODO
    }
}

public class RedlichKwongEquation : CubicEquationOfState
{
    // TODO
}

public class SoaveRedlichKwongEquation : CubicEquationOfState
{
    // TODO
}

public class PengRobinsonEquation : CubicEquationOfState
{
    // TODO
}