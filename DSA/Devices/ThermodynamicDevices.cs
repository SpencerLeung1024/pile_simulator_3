using System;
using System.Collections.Generic;

public class Radiator : Device
{
    public Volume Side; // Will move heat into and out of this volume
    public double RadiationArea; // m^2
    public double ConvectionArea; // m^2, has no effect if the device is in a vacuum

    // Derived quantities:
    public double RadiantExitance; // W / m^2
    public double RadiantFlux { get { return RadiantExitance * RadiationArea; } } // W
    public double HeatTransferCoefficient; // W / (m^2 K), for convection
    public double ConvectiveExitance; // W / m^2
    public double ConvectiveFlux { get { return ConvectiveExitance * ConvectionArea; } } // W
    public double Flux { get { return RadiantFlux + ConvectiveFlux; } } // W

    protected override void DeriveQuantities()
    {
        // Assume the radiator is a perfect blackbody and always magically angles itself to receive zero radiation apart from the CMB
        double SideT = Side.T; // K
        //Volume Atmosphere = World.Atmosphere; // Possibly null
        Volume Atmosphere = null; // World doesn't have an atmosphere field yet
        double WorldT = Atmosphere != null ? Atmosphere.T : Constants.CMBTemperature; // K
        double Irradiance = Constants.sigma * Math.Pow(WorldT, 4); // W / m^2
        double PureRadiantExitance = Constants.sigma * Math.Pow(SideT, 4); // W / m^2
        RadiantExitance = PureRadiantExitance - Irradiance; // W / m^2

        // Get heat transfer coefficient
        // Placeholder
        HeatTransferCoefficient = 1e2; // W / (m^2 K)
        if (Atmosphere == null)
        {
            HeatTransferCoefficient = 0.0;
        }
        else
        {
            HeatTransferCoefficient *= Atmosphere.P / Constants.atm; // Scale linearly with pressure, so that at 1 atm we get the placeholder value
        }

        ConvectiveExitance = HeatTransferCoefficient * (SideT - WorldT); // W / m^2
    }
}

// TODO
/*
public class HeatExchanger : Device
{
    public Volume LeftSide;
    public Volume RightSide;
    public double Area; // m^2
}

public class HeatPump : Device
{
    public Volume WasteSide;
    public Volume ControlledSide;
    public double Power; // W, input power
    public double TTarget; // K, the heat pump will try to keep the controlled side at this temperature
}

public class Heater : Device
{
    public Volume Side;
    public double Power; // W, input power
    public double TTarget; // K, the heater will turn off if the side is above this temperature
}

public abstract class HeatEngine : Device
{
    public Volume HotSide;
    public Volume ColdSide;
    public Network PowerNetwork;
    public double ThermalPower; // W, the maximum heat flow from the hot side to the cold side

    // Derived quantities:
    public double Efficiency; // 0 to 1, the fraction of thermal power that is converted to work instead of being dumped into the cold side
    public double OutputPower { get { return ThermalPower * Efficiency; } } // W, the power output of the engine
}

public class CarnotEngine : HeatEngine
{
    // The ideal heat engine, achieves the Carnot efficiency
    // 1. Isothermal expansion
    // 2. Isentropic expansion
    // 3. Isothermal compression
    // 4. Isentropic compression
}

public class StirlingEngine : HeatEngine
{
    // The Carnot efficiency requires infinitesimal differences, requiring the cycle to take an infinite amount of time
    // 1. Isothermal expansion
    // 2. Isochoric cooling
    // 3. Isothermal compression
    // 4. Isochoric heating
}

public class ClosedBraytonEngine : HeatEngine
{
    // The hot side is used to heat an internal working fluid, which spins a turbine
    // 1. Adiabatic compression
    // 2. Isobaric heating
    // 3. Adiabatic expansion
    // 4. Isobaric cooling
}

public class OpenBraytonEngine : HeatEngine
{
    // The hot side's gas spins the turbine, then exits into the cold side
    // Same cycle as ClosedBraytonEngine
}
*/
