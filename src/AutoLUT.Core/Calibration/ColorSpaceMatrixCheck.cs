namespace AutoLUT.Core.Calibration;

/// <summary>
/// Detects a Rec.601 vs Rec.709 color-matrix mismatch in the capture source, read from the fitted
/// correction matrix M. A wrong YCbCr-to-RGB decode matrix upstream is a pure linear transform on
/// gamma-encoded RGB whose rows sum to 1, so it preserves grays and rotates only chromatic colors -
/// ColorRangeCheck (which lives on the gray captures) is blind to it. The fit's affine matrix M
/// already absorbs the recoverable part, so this check is advisory: a mismatch also clips saturated
/// colors (unrecoverable) and thins the identifier's margins, so the user is told to fix the source.
///
/// M maps observed-to-reference, i.e. it approximates the inverse of the distortion. Because a YCbCr
/// decode is the inverse of its encode, the correction for a 601-as-709 distortion is exactly
/// N_B = Decode601 * Encode709, and for 709-as-601 it is N_A = Decode709 * Encode601. So M is matched
/// against {N_A, N_B}. Matching is gain-invariant (each row normalized to sum 1, cancelling the
/// per-channel gain a mismatch shares with any capture) and requires both structural alignment
/// (Frobenius cosine) and sufficient magnitude - generic cross-channel bleed is unstructured and
/// fails the cosine gate.
/// </summary>
public static class ColorSpaceMatrixCheck
{
    // Chosen from the simulation sweep in ColorSpaceMatrixCheckTests. The magnitude fraction is the
    // real separator: over random in-bounds degradations (no mismatch) it stays under ~0.26, while a
    // real mismatch composed with the same degradations stays above ~0.52. Cosine alone does NOT
    // separate (a clean fit can align structurally by chance at low magnitude), so it is only a loose
    // direction confirmation and the magnitude gate carries the decision.
    private const double SimThreshold = 0.5;
    private const double MagThreshold = 0.4;

    private static readonly double[] Identity = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    private static readonly double[] Encode601 = Encode(0.299, 0.114);
    private static readonly double[] Encode709 = Encode(0.2126, 0.0722);
    private static readonly double[] Decode601 = Inverse(Encode601);
    private static readonly double[] Decode709 = Inverse(Encode709);

    // Row-normalized correction residuals (N - I), row-major 3x3.
    private static readonly double[] Advise601 = Residual(Decode601, Encode709); // N_B: source decoding as 709
    private static readonly double[] Advise709 = Residual(Decode709, Encode601); // N_A: source decoding as 601

    /// <summary>
    /// Returns a user-facing warning when the fitted 3x4 correction matrix carries a 601/709
    /// mismatch signature, or null otherwise.
    /// </summary>
    public static string? Detect(IReadOnlyList<float> matrix3x4)
    {
        // 3x3 linear part of the row-major 3x4 (drop the offset column).
        double[] l =
        [
            matrix3x4[0], matrix3x4[1], matrix3x4[2],
            matrix3x4[4], matrix3x4[5], matrix3x4[6],
            matrix3x4[8], matrix3x4[9], matrix3x4[10],
        ];
        if (!RowNormalizeInPlace(l))
        {
            return null; // degenerate fit
        }

        var e = new double[9];
        for (int i = 0; i < 9; i++)
        {
            e[i] = l[i] - Identity[i];
        }

        (double sim601, double mag601) = Match(e, Advise601);
        (double sim709, double mag709) = Match(e, Advise709);

        if (sim601 >= sim709)
        {
            if (sim601 >= SimThreshold && mag601 >= MagThreshold)
            {
                return Message(rec601: true);
            }
        }
        else if (sim709 >= SimThreshold && mag709 >= MagThreshold)
        {
            return Message(rec601: false);
        }

        return null;
    }

    private static string Message(bool rec601) =>
        rec601
            ? "Colors look rotated as if the capture source is decoding with the wrong YCbCr color "
                + "matrix (Rec. 601 vs Rec. 709). The LUT compensates, but a mismatch clips saturated "
                + "colors and lowers accuracy - in the capture source's Properties set Color Space to "
                + "'Rec. 601', then re-capture."
            : "Colors look rotated as if the capture source is decoding with the wrong YCbCr color "
                + "matrix (Rec. 601 vs Rec. 709). The LUT compensates, but a mismatch clips saturated "
                + "colors and lowers accuracy - in the capture source's Properties set Color Space to "
                + "'Rec. 709', then re-capture.";

    /// <summary>Frobenius cosine similarity and magnitude fraction of residual e against signature c.</summary>
    private static (double Similarity, double Magnitude) Match(double[] e, double[] c)
    {
        double dot = 0, ee = 0, cc = 0;
        for (int i = 0; i < 9; i++)
        {
            dot += e[i] * c[i];
            ee += e[i] * e[i];
            cc += c[i] * c[i];
        }

        if (ee < 1e-12 || cc < 1e-12)
        {
            return (0, 0);
        }

        return (dot / Math.Sqrt(ee * cc), dot / cc);
    }

    private static double[] Encode(double kr, double kb)
    {
        double kg = 1 - kr - kb;
        double cbDen = 2 * (1 - kb);
        double crDen = 2 * (1 - kr);
        return
        [
            kr, kg, kb,
            -kr / cbDen, -kg / cbDen, 0.5,
            0.5, -kg / crDen, -kb / crDen,
        ];
    }

    private static double[] Residual(double[] decode, double[] encode)
    {
        double[] n = Mul(decode, encode);
        RowNormalizeInPlace(n);
        for (int i = 0; i < 9; i++)
        {
            n[i] -= Identity[i];
        }

        return n;
    }

    private static double[] Mul(double[] a, double[] b)
    {
        var r = new double[9];
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                r[row * 3 + col] = a[row * 3] * b[col]
                    + a[row * 3 + 1] * b[3 + col]
                    + a[row * 3 + 2] * b[6 + col];
            }
        }

        return r;
    }

    private static double[] Inverse(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        double inv = 1.0 / det;
        return
        [
            (e * i - f * h) * inv, (c * h - b * i) * inv, (b * f - c * e) * inv,
            (f * g - d * i) * inv, (a * i - c * g) * inv, (c * d - a * f) * inv,
            (d * h - e * g) * inv, (b * g - a * h) * inv, (a * e - b * d) * inv,
        ];
    }

    private static bool RowNormalizeInPlace(double[] m)
    {
        for (int row = 0; row < 3; row++)
        {
            double s = m[row * 3] + m[row * 3 + 1] + m[row * 3 + 2];
            if (Math.Abs(s) < 1e-6)
            {
                return false;
            }

            m[row * 3] /= s;
            m[row * 3 + 1] /= s;
            m[row * 3 + 2] /= s;
        }

        return true;
    }
}
