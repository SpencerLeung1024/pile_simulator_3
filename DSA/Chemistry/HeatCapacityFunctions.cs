using Godot;
using System;

public abstract class HeatCapacityFunction
{
    public abstract float Getc_p(float T); // J / (K * mol), molar heat capacity at constant pressure
    public abstract float GetH(float T); // J / mol, molar enthalpy relative to standard enthalpy of formation
    public abstract float GetS(float T); // J / (K * mol), molar entropy relative to standard entropy
}

public class ConstantHeatCapacityFunction : HeatCapacityFunction
{
    public float c_p;
    public float StandardEnthalpyOfFormation;
    public float StandardEntropy;
    public float StandardTemperature; // K, used to know where the standard enthalpy of formation and standard entropy are measured
    public override float Getc_p(float T)
    {
        return c_p;
    }
    public override float GetH(float T)
    {
        // H(T) = H(T_ref) + integral from T_ref to T of c_p(T) dT
        // c_p is constant, so c_p * integral from T_ref to T of 1 dT
        // H(T) = H(T_ref) + c_p * (T - T_ref)
        return StandardEnthalpyOfFormation + c_p * (T - StandardTemperature);
    }
    public override float GetS(float T)
    {
        // S(T) = S(T_ref) + integral from T_ref to T of c_p(T) / T dT
        // c_p is constant, so c_p * integral from T_ref to T of 1 / T dT
        // Antiderivative of 1 / T is ln(T)
        // S(T) = S(T_ref) + c_p * (ln(T) - ln(T_ref))
        return StandardEntropy + c_p * (MathF.Log(T) - MathF.Log(StandardTemperature));
    }
}

public class NASA7HeatCapacityFunction : HeatCapacityFunction
{
    // TODO
}

public class NASA9HeatCapacityFunction : HeatCapacityFunction
{
    // TODO
}

public class ShomateHeatCapacityFunction : HeatCapacityFunction
{
    // TODO
}