using Godot;
using System;

public enum MaterialEnum
{
	Rock, // 0
	Ice, // 1
	Metal // 2
}

class Materials
{
    public static Color[] materialColors = new Color[]
    {
        new Color(0.5f, 0.5f, 0.5f), // Rock
        new Color(0.8f, 0.8f, 1.0f), // Ice
        new Color(0.4f, 0.2f, 0.2f) // Metal
    };

    public static float[] densities = new float[]
    {
        2.5f, // Rock
        1.0f, // Ice
        5.0f // Metal
    };

    // Use one material instance for each material
    // Apply the material to the MeshInstance3D.SurfaceMaterialOverride[0] of each rock instance
    public static StandardMaterial3D[] materialInstances = new StandardMaterial3D[]
    {
        new StandardMaterial3D() { AlbedoColor = materialColors[0] }, // Rock
        new StandardMaterial3D() { AlbedoColor = materialColors[1] }, // Ice
        new StandardMaterial3D() { AlbedoColor = materialColors[2] } // Metal
    };
}
