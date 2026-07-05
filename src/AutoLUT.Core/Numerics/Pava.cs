namespace AutoLUT.Core.Numerics;

public static class Pava
{
    /// <summary>
    /// Weighted isotonic regression: returns the non-decreasing sequence minimizing
    /// weighted squared error against <paramref name="values"/> (pool-adjacent-violators).
    /// </summary>
    public static double[] FitNonDecreasing(ReadOnlySpan<double> values, ReadOnlySpan<double> weights)
    {
        int n = values.Length;
        if (weights.Length != n)
        {
            throw new ArgumentException("Values and weights must have the same length.");
        }

        // Blocks of pooled values: mean, weight, count.
        var mean = new double[n];
        var weight = new double[n];
        var count = new int[n];
        int blocks = 0;

        for (int i = 0; i < n; i++)
        {
            mean[blocks] = values[i];
            weight[blocks] = weights[i];
            count[blocks] = 1;
            blocks++;
            while (blocks > 1 && mean[blocks - 2] > mean[blocks - 1])
            {
                double w = weight[blocks - 2] + weight[blocks - 1];
                mean[blocks - 2] = w > 0
                    ? (mean[blocks - 2] * weight[blocks - 2] + mean[blocks - 1] * weight[blocks - 1]) / w
                    : (mean[blocks - 2] + mean[blocks - 1]) / 2;
                weight[blocks - 2] = w;
                count[blocks - 2] += count[blocks - 1];
                blocks--;
            }
        }

        var result = new double[n];
        int index = 0;
        for (int blockIndex = 0; blockIndex < blocks; blockIndex++)
        {
            for (int k = 0; k < count[blockIndex]; k++)
            {
                result[index++] = mean[blockIndex];
            }
        }

        return result;
    }
}
