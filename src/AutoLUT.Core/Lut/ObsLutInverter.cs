using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Lut;

/// <summary>
/// Numerically inverts a baked OBS LUT: for each pixel it finds the input color the LUT would
/// have mapped to that pixel's color. Used to reverse a LUT that was already active when
/// AutoSplit reference images were captured, before applying a newly generated LUT.
///
/// The inverse is best-effort, not exact: LUT output is quantized to bytes, and regions the LUT
/// crushed or clipped are not injective, so those colors resolve to the nearest achievable input.
/// Solved per unique color with a damped Newton iteration in sRGB-encoded output space
/// (residual measured in output byte units), against the same trilinear-in-linear-light
/// sampling OBS uses.
/// </summary>
public sealed class ObsLutInverter
{
    // Output byte units. Well below the 0.5 quantization the captured reference already
    // carries, so the solve error is never the dominant loss - including for the float
    // composition path, where no later rounding would mask it.
    private const float ResidualTolerance = 0.05f;
    private const int MaxIterations = 40;
    private const float JacobianStep = 1f / 510f;

    private readonly ObsLutApplier _applier;
    private readonly Dictionary<int, (float R, float G, float B)> _cache = [];

    public ObsLutInverter(ObsLutApplier applier) => _applier = applier;

    public RawImage Invert(RawImage source)
    {
        var output = new RawImage(source.Width, source.Height);
        byte[] sourcePixels = source.Pixels;
        byte[] outputPixels = output.Pixels;
        for (int i = 0; i < sourcePixels.Length; i += 3)
        {
            var (r, g, b) = SolveInverse(sourcePixels[i], sourcePixels[i + 1], sourcePixels[i + 2]);
            outputPixels[i] = (byte)MathF.Round(r * 255f);
            outputPixels[i + 1] = (byte)MathF.Round(g * 255f);
            outputPixels[i + 2] = (byte)MathF.Round(b * 255f);
        }

        return output;
    }

    /// <summary>
    /// Reverses this LUT and applies another on top, composing in float: the solved inverse is
    /// fed straight into the new LUT's continuous sampler, so the only quantization is the final
    /// output rounding. Less lossy than Invert followed by Apply, which rounds the intermediate
    /// image to bytes.
    /// </summary>
    public RawImage InvertThenApply(ObsLutApplier newLut, RawImage source)
    {
        var output = new RawImage(source.Width, source.Height);
        byte[] sourcePixels = source.Pixels;
        byte[] outputPixels = output.Pixels;
        for (int i = 0; i < sourcePixels.Length; i += 3)
        {
            var (inverseR, inverseG, inverseB) = SolveInverse(sourcePixels[i], sourcePixels[i + 1], sourcePixels[i + 2]);
            var (r, g, b) = newLut.ApplyContinuous(inverseR, inverseG, inverseB);
            outputPixels[i] = r;
            outputPixels[i + 1] = g;
            outputPixels[i + 2] = b;
        }

        return output;
    }

    /// <summary>Solves for the continuous sRGB input this LUT maps to the target color; cached per color.</summary>
    private (float R, float G, float B) SolveInverse(byte targetR, byte targetG, byte targetB)
    {
        int key = targetR << 16 | targetG << 8 | targetB;
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Work in sRGB-encoded [0,1] coordinates; residuals in output byte units so the
        // tolerance maps directly onto the final rounding step.
        Span<float> input = [targetR / 255f, targetG / 255f, targetB / 255f];
        Span<float> residual = stackalloc float[3];
        Span<float> candidate = stackalloc float[3];
        Span<float> candidateResidual = stackalloc float[3];
        Span<float> steppedResidual = stackalloc float[3];
        Span<float> jacobian = stackalloc float[9];
        float bestError = Residual(input, targetR, targetG, targetB, residual);

        for (int iteration = 0; iteration < MaxIterations && bestError >= ResidualTolerance; iteration++)
        {
            // Numeric Jacobian of the output (in byte units) by forward differences,
            // stepping backward at the domain boundary.
            for (int column = 0; column < 3; column++)
            {
                float step = input[column] + JacobianStep <= 1f ? JacobianStep : -JacobianStep;
                float saved = input[column];
                input[column] = saved + step;
                Residual(input, targetR, targetG, targetB, steppedResidual);
                input[column] = saved;
                for (int row = 0; row < 3; row++)
                {
                    jacobian[row * 3 + column] = (steppedResidual[row] - residual[row]) / step;
                }
            }

            if (!SolveDamped(jacobian, residual, out float deltaR, out float deltaG, out float deltaB))
            {
                break;
            }

            // Backtracking line search: crushed LUT regions have near-flat spots where a
            // full Newton step overshoots.
            bool improved = false;
            for (float scale = 1f; scale >= 0.125f; scale *= 0.5f)
            {
                candidate[0] = Math.Clamp(input[0] - scale * deltaR, 0f, 1f);
                candidate[1] = Math.Clamp(input[1] - scale * deltaG, 0f, 1f);
                candidate[2] = Math.Clamp(input[2] - scale * deltaB, 0f, 1f);
                float error = Residual(candidate, targetR, targetG, targetB, candidateResidual);
                if (error < bestError)
                {
                    candidate.CopyTo(input);
                    candidateResidual.CopyTo(residual);
                    bestError = error;
                    improved = true;
                    break;
                }
            }

            if (!improved)
            {
                break;
            }
        }

        // Input only ever moves on improvement, so it always holds the best solution found.
        var result = (input[0], input[1], input[2]);
        _cache[key] = result;
        return result;
    }

    /// <summary>Residual of applying the LUT at the given input versus the target bytes, in output byte units.</summary>
    private float Residual(ReadOnlySpan<float> input, byte targetR, byte targetG, byte targetB, Span<float> residual)
    {
        var (linearR, linearG, linearB) = _applier.SampleContinuous(input[0], input[1], input[2]);
        residual[0] = ColorSpace.LinearToSrgb(linearR) * 255f - targetR;
        residual[1] = ColorSpace.LinearToSrgb(linearG) * 255f - targetG;
        residual[2] = ColorSpace.LinearToSrgb(linearB) * 255f - targetB;
        return MathF.Max(MathF.Abs(residual[0]), MathF.Max(MathF.Abs(residual[1]), MathF.Abs(residual[2])));
    }

    /// <summary>Solves (jacobian + lambda I) delta = residual by Cramer's rule; lambda regularizes flat LUT regions.</summary>
    private static bool SolveDamped(ReadOnlySpan<float> jacobian, ReadOnlySpan<float> residual, out float deltaR, out float deltaG, out float deltaB)
    {
        const float lambda = 1e-3f;
        float m00 = jacobian[0] + lambda, m01 = jacobian[1], m02 = jacobian[2];
        float m10 = jacobian[3], m11 = jacobian[4] + lambda, m12 = jacobian[5];
        float m20 = jacobian[6], m21 = jacobian[7], m22 = jacobian[8] + lambda;

        float determinant = m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
        deltaR = deltaG = deltaB = 0f;
        if (MathF.Abs(determinant) < 1e-12f)
        {
            return false;
        }

        deltaR = (residual[0] * (m11 * m22 - m12 * m21) - m01 * (residual[1] * m22 - m12 * residual[2]) + m02 * (residual[1] * m21 - m11 * residual[2])) / determinant;
        deltaG = (m00 * (residual[1] * m22 - m12 * residual[2]) - residual[0] * (m10 * m22 - m12 * m20) + m02 * (m10 * residual[2] - residual[1] * m20)) / determinant;
        deltaB = (m00 * (m11 * residual[2] - residual[1] * m21) - m01 * (m10 * residual[2] - residual[1] * m20) + residual[0] * (m10 * m21 - m11 * m20)) / determinant;
        return true;
    }
}
