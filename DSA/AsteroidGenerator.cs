using Godot;
using System;

public class AsteroidGenerator
{
    public ulong Seed;
    public float Radius; // m and Godot units
    public float GravitationalMass; // ton, used for gravity calculations, may be different from actual mass calculated by realizing all voxels, does not change as the asteroid is modified
    public float IdealVolume // m^3, assuming a perfect sphere
    {
        get
        {
            return (4.0f / 3.0f) * Mathf.Pi * Mathf.Pow(Radius, 3);
        }
    }
    public float BulkDensity // ton / m^3. Note that Godot UI shows mass as "kg" but setting every rock to a few thousand in mass makes physics unstable, so 1 Godot unit = 1 ton
    {
        get
        {
            return GravitationalMass / IdealVolume;
        }
    }
    public FastNoiseLite TerrainNoise;
    public float MaxHeight; // m
    public float MaxRadius
    {
        get
        {
            return Radius + MaxHeight;
        }
    }

    public void InitializeNoise()
    {
        TerrainNoise = new FastNoiseLite();
        TerrainNoise.Seed = (int)(Seed & 0x7FFFFFFF); // Convert to int, keeping lower 31 bits
        TerrainNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        TerrainNoise.Frequency = 2.71f;
        TerrainNoise.FractalOctaves = 5;
        TerrainNoise.FractalLacunarity = 2.0f;
        TerrainNoise.FractalGain = 0.5f;
    }

    public AsteroidGenerator(ulong seed, float radius, float gravitationalMass, float maxHeight)
    {
        Seed = seed;
        Radius = radius;
        GravitationalMass = gravitationalMass;
        MaxHeight = maxHeight;

        InitializeNoise();
    }

    public float GetHeight(Vector3 position)
    {
        Vector3 normalizedPos = position.Normalized(); // Project to unit sphere. This means you are only distorting the surface of a sphere and can't get overhangs or caves, but this is easier to understand
        float noiseValue = TerrainNoise.GetNoise3Dv(normalizedPos);
        noiseValue *= MathF.Abs(noiseValue); // Test: try to generate mountains by squaring the height
        return noiseValue * MaxHeight;
    }

    public float GetDepth(Vector3 position)
    {
        float localHeight = GetHeight(position);
        float surfaceHeight = Radius + localHeight;
        return surfaceHeight - position.Length();
    }

    public MaterialEnum Sample(Vector3 position)
    {
        float depth = GetDepth(position);
        float normalizedDistanceToCenter = position.Length() / Radius; // 0 at center, 1 at ideal sphere radius, >1 outside

        // Above the surface
        if (depth < 0)
        {
            // Test: generate ice oceans by filling in anything under the radius
            // Minmus from Kerbal Space Program?
            if (normalizedDistanceToCenter < 1.0f)
            {
                return MaterialEnum.Ice;
            }
            else
            {
                return MaterialEnum.Empty;
            }
        }
        else
        {
            // Below the surface
            if (normalizedDistanceToCenter < 0.5f) // Metal core
            {
                return MaterialEnum.Metal;
            }
            else // Rock mantle and crust
            {
                return MaterialEnum.Rock;
            }
        }
    }
}