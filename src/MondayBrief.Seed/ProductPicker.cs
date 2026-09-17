using System.Runtime.CompilerServices;

namespace MondayBrief.Seed;

/// <summary> 
/// Chooses basket products in proportion to their (seasonal) weights usng smooth weighted round-robin
/// with a little jitter. Independent random draws leave low-volume products with ±20% month-to-month
/// noise, which drowns the planted decline; this keeps each product's realized units close to its
/// expected share while baskets still look random. Credits persist across days.
/// </summary>
public sealed class ProductPicker(IReadOnlyList<SeedProduct> pool, Func<SeedProduct, double> baseWeight, SeededRng rng)
{
    private const double Jitter = 1.0;

    private readonly double[] _credit = new double[pool.Count];

    public List<SimLine> Pick(DateOnly d, int lineCount, double[] quantityWeights)
    {
        Span<double> weights = stackalloc double[pool.Count];
        var total = 0.0;
        for (var i = 0; i < pool.Count; i++)
        {
            weights[i] = baseWeight(pool[i]) * DemandModel.ProductMultiplier(pool[i], d);
            total += weights[i];
        }

        var lines = new List<SimLine>(lineCount);
        if (total <= 0)
        {
            return lines;
        }

        Span<bool> used = stackalloc bool[pool.Count];
        for (var n = 0; n < lineCount; n++)
        {
            var best = -1;
            var bestScore = double.NegativeInfinity;
            for (var i = 0; i < pool.Count; i++)
            {
                if (weights[i] <= 0)
                {
                    continue;
                }

                _credit[i] += weights[i] / total;
                if (used[i])
                {
                    continue;
                }

                var score = _credit[i] + rng.NextDouble() * Jitter;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            used[best] = true;
            _credit[best] -= 1;
            lines.Add(new SimLine(pool[best], rng.PickCount(quantityWeights)));
        }

        return lines;
    }
}