namespace MondayBrief.Seed;

/// <summary>
/// SplitMix64. Used instead of System.Random because seeded Random output is not guaranteed stable
/// across .NET versions. (Math.Log/Exp can still differ in the last bits across 0Ses, which is why the
/// generated files are committed rather than regenerated on every machine.)
/// </summary>
public sealed class SeededRng(ulong seed)
{
    private ulong _state = seed;

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1].</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public int Next(int minInclusive, int maxExclusive) => 
        minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));

    public double Normal(double mean, double stdDev)
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }

    public int Poisson(double lambda)
    {
        if (lambda <= 0)
        {
            return 0;
        }

        if (lambda >= 30)
        {
            return Math.Max(0, (int)Math.Round(Normal(lambda, Math.Sqrt(lambda))));
        }

        var limit = Math.Exp(-lambda);
        var k = 0;
        var p = NextDouble();
        while (p > limit)
        {
            k++;
            p *= NextDouble();
        }

        return k;
    }

    /// <summary>
    /// A count with noise scaled by <paramref name="dispersion"/> (1.0 = Poisson). Values below 1 keep
    /// short-window rates from wandering across alert thresholds on ordinary days.
    /// </summary>
    public int Count(double lambda, double dispersion)
    {
        if (lambda < 3)
        {
            return Poisson(lambda);
        }

        return Math.Max(0, (int)Math.Round(Normal(lambda, Math.Sqrt(lambda) * dispersion)));
    }

    /// <summary>Returns the index of a weighted choice, or -1 if every weight is zero.</summary>
    public int PickIndex(ReadOnlySpan<double> weights)
    {
        var total = 0.0;
        foreach (var w in weights)
        {
            total += w;
        }

        if (total <= 0)
        {
            return -1;
        }

        var target = NextDouble() * total;
        var last = -1;
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0)
            {
                continue;
            }

            last = i;
            target -= weights[i];
            if (target < 0)
            {
                return i;
            }
        }

        return last;
    }

    /// <summary>1-based count drawn from weights for 1, 2, 3, ...</summary>
    public int PickCount(ReadOnlySpan<double> weightsForOneTwoThree) => PickIndex(weightsForOneTwoThree) + 1;
}