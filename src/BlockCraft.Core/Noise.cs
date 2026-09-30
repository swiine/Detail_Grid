namespace BlockCraft.Core;

/// <summary>Seeded 2D value noise with fractal octaves. Deterministic and allocation free.</summary>
public sealed class Noise
{
    private readonly int _seed;

    public Noise(int seed) => _seed = seed;

    private float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + _seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    /// <summary>Value noise in [0, 1].</summary>
    public float Sample(float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float tx = Smooth(x - x0);
        float tz = Smooth(z - z0);

        float a = Hash(x0, z0);
        float b = Hash(x0 + 1, z0);
        float c = Hash(x0, z0 + 1);
        float d = Hash(x0 + 1, z0 + 1);

        float ab = a + (b - a) * tx;
        float cd = c + (d - c) * tx;
        return ab + (cd - ab) * tz;
    }

    /// <summary>Fractal (fBm) noise in [0, 1].</summary>
    public float Fractal(float x, float z, int octaves, float persistence = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0, freq = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += Sample(x * freq, z * freq) * amp;
            norm += amp;
            amp *= persistence;
            freq *= 2;
        }
        return sum / norm;
    }

    /// <summary>Deterministic per-cell random in [0, 1), for scattering trees etc.</summary>
    public float Cell(int x, int z) => Hash(x * 31 + 7, z * 17 + 3);
}
