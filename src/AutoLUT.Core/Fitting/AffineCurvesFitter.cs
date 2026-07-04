using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Numerics;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.Fitting;

/// <summary>
/// Fits an <see cref="AffineCurvesTransform"/> by alternating a closed-form weighted affine fit with
/// per-channel monotone curve fits, inside an IRLS loop (Tukey biweight on Oklab residuals) so
/// outliers have limited influence. The inner least squares minimizes RGB error while robust
/// weighting and diagnostics use Oklab - a pragmatic tradeoff that avoids a nonlinear solver;
/// a full Gauss-Newton-in-Oklab fitter can be swapped in via IColorTransformFitter later.
/// </summary>
public sealed class AffineCurvesFitter : IColorTransformFitter
{
    private const int MinimumSamples = 8;

    public FitResult Fit(IReadOnlyList<ColorCorrespondence> samples, FitOptions options, CancellationToken cancellationToken)
    {
        if (samples.Count < MinimumSamples)
            throw new ArgumentException($"At least {MinimumSamples} correspondences required, got {samples.Count}.", nameof(samples));

        int n = samples.Count;
        var robust = new double[n];
        Array.Fill(robust, 1.0);
        var weights = new double[n];

        float[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];
        var curves = new MonotoneCurve[3];
        AffineCurvesTransform transform = null!;
        var residuals = new float[n];

        for (int iteration = 0; iteration < options.RobustIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (int i = 0; i < n; i++)
                weights[i] = samples[i].Weight * robust[i];

            matrix = FitMatrix(samples, weights, options.MatrixRidge);

            for (int channel = 0; channel < 3; channel++)
                curves[channel] = FitCurve(samples, weights, matrix, channel, options);

            transform = new AffineCurvesTransform(matrix, curves[0], curves[1], curves[2]);

            for (int i = 0; i < n; i++)
                residuals[i] = Oklab.DeltaESrgb(transform.Apply(samples[i].Observed).Clamp01(), samples[i].Reference);

            UpdateRobustWeights(residuals, robust);
        }

        return new FitResult(transform, BuildDiagnostics(residuals, robust));
    }

    private static float[] FitMatrix(IReadOnlyList<ColorCorrespondence> samples, double[] weights, double ridgeRelative)
    {
        double totalWeight = weights.Sum();
        double ridge = Math.Max(ridgeRelative * totalWeight, 1e-9);
        var matrix = new float[12];
        Span<double> x = stackalloc double[4];
        x[3] = 1.0;

        for (int channel = 0; channel < 3; channel++)
        {
            var normal = new double[4, 4];
            var rhs = new double[4];

            for (int i = 0; i < samples.Count; i++)
            {
                double w = weights[i];
                if (w <= 0)
                    continue;
                var obs = samples[i].Observed;
                x[0] = obs.R;
                x[1] = obs.G;
                x[2] = obs.B;
                double y = Channel(samples[i].Reference, channel);
                for (int a = 0; a < 4; a++)
                {
                    rhs[a] += w * x[a] * y;
                    for (int b = 0; b < 4; b++)
                        normal[a, b] += w * x[a] * x[b];
                }
            }

            for (int a = 0; a < 4; a++)
            {
                normal[a, a] += ridge;
                // Identity row: coefficient 1 on the input channel matching this output channel.
                if (a == channel)
                    rhs[a] += ridge;
            }

            double[] row = LinearSolver.Solve(normal, rhs);
            for (int a = 0; a < 4; a++)
                matrix[channel * 4 + a] = (float)row[a];
        }

        return matrix;
    }

    private static MonotoneCurve FitCurve(
        IReadOnlyList<ColorCorrespondence> samples, double[] weights, float[] matrix, int channel, FitOptions options)
    {
        int knots = options.CurveKnots;
        double totalWeight = weights.Sum();
        double smooth = options.CurveSmoothness * totalWeight;
        double identityPull = Math.Max(options.CurveIdentityPull * totalWeight, 1e-9);

        var normal = new double[knots, knots];
        var rhs = new double[knots];
        var knotMass = new double[knots];

        for (int i = 0; i < samples.Count; i++)
        {
            double w = weights[i];
            if (w <= 0)
                continue;
            var obs = samples[i].Observed;
            float input = matrix[channel * 4] * obs.R + matrix[channel * 4 + 1] * obs.G
                + matrix[channel * 4 + 2] * obs.B + matrix[channel * 4 + 3];
            double position = Math.Clamp(input, 0f, 1f) * (knots - 1);
            int segment = Math.Min((int)position, knots - 2);
            double t = position - segment;
            double y = Channel(samples[i].Reference, channel);

            // Hat basis: only phi[segment] = 1-t and phi[segment+1] = t are nonzero.
            double p0 = 1 - t, p1 = t;
            normal[segment, segment] += w * p0 * p0;
            normal[segment, segment + 1] += w * p0 * p1;
            normal[segment + 1, segment] += w * p0 * p1;
            normal[segment + 1, segment + 1] += w * p1 * p1;
            rhs[segment] += w * p0 * y;
            rhs[segment + 1] += w * p1 * y;
            knotMass[segment] += w * p0;
            knotMass[segment + 1] += w * p1;
        }

        // Second-difference smoothness: smooth * ||D2 v||^2.
        for (int k = 1; k < knots - 1; k++)
        {
            normal[k - 1, k - 1] += smooth;
            normal[k, k] += 4 * smooth;
            normal[k + 1, k + 1] += smooth;
            normal[k - 1, k] += -2 * smooth;
            normal[k, k - 1] += -2 * smooth;
            normal[k, k + 1] += -2 * smooth;
            normal[k + 1, k] += -2 * smooth;
            normal[k - 1, k + 1] += smooth;
            normal[k + 1, k - 1] += smooth;
        }

        // Identity pull keeps knots with no data support anchored.
        for (int k = 0; k < knots; k++)
        {
            normal[k, k] += identityPull;
            rhs[k] += identityPull * (k / (double)(knots - 1));
        }

        double[] solved = LinearSolver.Solve(normal, rhs);

        var pavaWeights = new double[knots];
        for (int k = 0; k < knots; k++)
            pavaWeights[k] = knotMass[k] + identityPull;
        double[] monotone = Pava.FitNonDecreasing(solved, pavaWeights);

        var values = new float[knots];
        for (int k = 0; k < knots; k++)
            values[k] = (float)monotone[k];
        return new MonotoneCurve(values);
    }

    private static void UpdateRobustWeights(float[] residuals, double[] robust)
    {
        var sorted = (float[])residuals.Clone();
        Array.Sort(sorted);
        double median = sorted[sorted.Length / 2];

        var deviations = new float[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
            deviations[i] = (float)Math.Abs(sorted[i] - median);
        Array.Sort(deviations);
        double mad = deviations[deviations.Length / 2];

        // Tukey biweight cutoff from a robust scale estimate; floor keeps a near-perfect fit
        // from rejecting everything over float noise.
        double cutoff = Math.Max(4.685 * 1.4826 * mad + median, 1e-4);
        for (int i = 0; i < residuals.Length; i++)
        {
            double u = residuals[i] / cutoff;
            robust[i] = u >= 1 ? 0 : Math.Pow(1 - u * u, 2);
        }
    }

    private static FitDiagnostics BuildDiagnostics(float[] residuals, double[] robust)
    {
        var sorted = (float[])residuals.Clone();
        Array.Sort(sorted);
        float mean = residuals.Average();
        float median = sorted[sorted.Length / 2];
        float p95 = sorted[Math.Min((int)(sorted.Length * 0.95), sorted.Length - 1)];
        int inliers = robust.Count(w => w > 0);
        return new FitDiagnostics(mean, median, p95, inliers, residuals.Length, residuals);
    }

    private static double Channel(Rgb c, int channel) => channel switch
    {
        0 => c.R,
        1 => c.G,
        _ => c.B,
    };
}
