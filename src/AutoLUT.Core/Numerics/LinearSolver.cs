namespace AutoLUT.Core.Numerics;

public static class LinearSolver
{
    /// <summary>Solves A·x = b via Gaussian elimination with partial pivoting. Mutates both arguments.</summary>
    public static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        if (a.GetLength(0) != n || a.GetLength(1) != n)
            throw new ArgumentException("Matrix must be square and match the right-hand side length.");

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                    pivot = row;

            if (Math.Abs(a[pivot, col]) < 1e-12)
                throw new InvalidOperationException("Matrix is singular or nearly singular.");

            if (pivot != col)
            {
                for (int k = col; k < n; k++)
                    (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }

            for (int row = col + 1; row < n; row++)
            {
                double factor = a[row, col] / a[col, col];
                if (factor == 0)
                    continue;
                for (int k = col; k < n; k++)
                    a[row, k] -= factor * a[col, k];
                b[row] -= factor * b[col];
            }
        }

        var x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double sum = b[row];
            for (int k = row + 1; k < n; k++)
                sum -= a[row, k] * x[k];
            x[row] = sum / a[row, row];
        }

        return x;
    }
}
