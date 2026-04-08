using Godot;
using System;

namespace DSA
{
    /// <summary>
    /// Procedural asteroid generator using 3D noise.
    /// Generates bumpy asteroid shapes with material distribution based on depth.
    /// </summary>
    public class ProceduralAsteroid
    {
        private ulong _seed;
        private float _radius;

        // Noise generators
        private FastNoiseLite _shapeNoise;      // For asteroid surface deformation
        private FastNoiseLite _materialNoise;   // For material boundary variation
        private FastNoiseLite _detailNoise;     // For fine material variation

        // Noise parameters
        private const float ShapeNoiseFrequency = 0.02f;
        private const float ShapeNoiseAmplitude = 8.0f;
        private const float MaterialNoiseFrequency = 0.03f;
        private const float MaterialNoiseAmplitude = 0.1f;
        private const float DetailNoiseFrequency = 0.05f;

        /// <summary>
        /// Creates a new procedural asteroid generator.
        /// </summary>
        /// <param name="seed">Random seed for deterministic generation</param>
        /// <param name="radius">Base asteroid radius</param>
        public ProceduralAsteroid(ulong seed, float radius)
        {
            _seed = seed;
            _radius = radius;

            InitializeNoise();
        }

        /// <summary>
        /// Initializes the noise generators with the seed.
        /// </summary>
        private void InitializeNoise()
        {
            // Shape noise - creates bumpy surface
            _shapeNoise = new FastNoiseLite();
            _shapeNoise.Seed = (int)(_seed & 0x7FFFFFFF); // Convert to int, keeping lower 31 bits
            _shapeNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
            _shapeNoise.Frequency = ShapeNoiseFrequency;
            _shapeNoise.FractalOctaves = 4;
            _shapeNoise.FractalLacunarity = 2.0f;
            _shapeNoise.FractalGain = 0.5f;

            // Material noise - adds variation to material boundaries
            _materialNoise = new FastNoiseLite();
            _materialNoise.Seed = (int)((_seed + 1) & 0x7FFFFFFF);
            _materialNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
            _materialNoise.Frequency = MaterialNoiseFrequency;
            _materialNoise.FractalOctaves = 3;
            _materialNoise.FractalLacunarity = 2.0f;
            _materialNoise.FractalGain = 0.5f;

            // Detail noise - fine variation within materials
            _detailNoise = new FastNoiseLite();
            _detailNoise.Seed = (int)((_seed + 2) & 0x7FFFFFFF);
            _detailNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
            _detailNoise.Frequency = DetailNoiseFrequency;
            _detailNoise.FractalOctaves = 2;
        }

        /// <summary>
        /// Gets the surface deformation offset at a position.
        /// Positive = bulge outward, Negative = indent inward
        /// </summary>
        public float GetSurfaceDeformation(Vector3 position)
        {
            // Sample 3D noise at position for surface deformation
            float noise = _shapeNoise.GetNoise3D(position.X, position.Y, position.Z);
            
            // Scale noise output (-1 to 1) to deformation amplitude
            return noise * ShapeNoiseAmplitude;
        }

        /// <summary>
        /// Checks if a point is inside the asteroid shape.
        /// </summary>
        public bool IsInsideAsteroid(Vector3 position)
        {
            float distFromCenter = position.Length();
            float deformation = GetSurfaceDeformation(position);
            float effectiveRadius = _radius + deformation;

            return distFromCenter < effectiveRadius;
        }

        /// <summary>
        /// Gets the normalized distance from asteroid center (0 = core, 1 = surface).
        /// Accounts for surface deformation.
        /// </summary>
        public float GetNormalizedDepth(Vector3 position)
        {
            float distFromCenter = position.Length();
            float deformation = GetSurfaceDeformation(position);
            float effectiveRadius = _radius + deformation;

            // Clamp to valid range
            return Mathf.Clamp(distFromCenter / effectiveRadius, 0.0f, 1.0f);
        }

        /// <summary>
        /// Samples the material at a world position.
        /// Returns null if position is outside asteroid.
        /// </summary>
        public MaterialEnum? SampleMaterial(Vector3 position)
        {
            // Check if inside asteroid
            if (!IsInsideAsteroid(position))
            {
                return null;
            }

            // Get normalized depth (0 = center, 1 = surface)
            float depth = GetNormalizedDepth(position);

            // Add noise variation to depth for non-spherical boundaries
            float materialVariation = _materialNoise.GetNoise3D(position.X, position.Y, position.Z) * MaterialNoiseAmplitude;
            float adjustedDepth = Mathf.Clamp(depth + materialVariation, 0.0f, 1.0f);

            // Sample detail noise for fine variation
            float detail = _detailNoise.GetNoise3D(position.X, position.Y, position.Z);
            
            // Sample additional noise for material mixing (5% chance of other materials)
            float mixNoise = _detailNoise.GetNoise3D(position.X * 0.1f, position.Y * 0.1f, position.Z * 0.1f);

            // Material distribution based on depth with 5% mixing of other materials
            // Core: ~90% metal, 5% rock, 5% ice
            // Mantle: Mixed rock with metal inclusions
            // Surface: ~90% ice, 5% rock, 5% metal

            MaterialEnum material;

            if (adjustedDepth < 0.4f)
            {
                // Core region - mostly metal
                // Add 5% chance for other materials even at core
                if (mixNoise > 0.90f)
                {
                    // 5% chance of ice even at core
                    material = MaterialEnum.Ice;
                }
                else if (mixNoise > 0.75f)
                {
                    // 15% more rock at core (total ~20% rock)
                    material = MaterialEnum.Rock;
                }
                else
                {
                    // ~75% metal at core
                    float metalThreshold = 0.3f - detail * 0.2f;
                    float t = adjustedDepth / 0.4f;
                    metalThreshold = Mathf.Lerp(0.1f, 0.5f, t);
                    material = (detail < metalThreshold) ? MaterialEnum.Metal : MaterialEnum.Rock;
                }
            }
            else if (adjustedDepth < 0.8f)
            {
                // Mantle region - mostly rock with metal inclusions
                // Add some random ice in mantle (rare)
                if (mixNoise > 0.95f && adjustedDepth > 0.6f)
                {
                    material = MaterialEnum.Ice;
                }
                else
                {
                    float metalThreshold = 0.8f - (adjustedDepth - 0.4f) * 0.75f;
                    metalThreshold = Mathf.Clamp(metalThreshold, 0.05f, 0.4f);
                    material = (detail < metalThreshold) ? MaterialEnum.Metal : MaterialEnum.Rock;
                }
            }
            else
            {
                // Surface region - mostly ice
                // Add 5% chance for other materials at surface
                if (mixNoise > 0.90f)
                {
                    // 5% chance of metal even at surface
                    material = MaterialEnum.Metal;
                }
                else if (mixNoise > 0.80f)
                {
                    // 10% more rock at surface (total ~15-20% rock)
                    material = MaterialEnum.Rock;
                }
                else
                {
                    // ~75-80% ice at surface
                    float iceStart = 0.8f;
                    float surfaceT = (adjustedDepth - iceStart) / (1.0f - iceStart);
                    float iceThreshold = surfaceT * 0.7f + 0.1f;
                    iceThreshold += detail * 0.15f;
                    material = (detail < iceThreshold) ? MaterialEnum.Ice : MaterialEnum.Rock;
                }
            }

            return material;
        }

        /// <summary>
        /// Gets the effective radius (including deformation) in a direction.
        /// </summary>
        public float GetRadiusInDirection(Vector3 direction)
        {
            direction = direction.Normalized();
            Vector3 surfacePoint = direction * _radius;
            float deformation = GetSurfaceDeformation(surfacePoint);
            return _radius + deformation;
        }

        /// <summary>
        /// Gets the base radius of the asteroid (without deformation).
        /// </summary>
        public float BaseRadius => _radius;

        /// <summary>
        /// Gets the seed used for generation.
        /// </summary>
        public ulong Seed => _seed;
    }
}
