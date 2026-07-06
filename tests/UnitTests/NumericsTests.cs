using AutoLUT.Core.Numerics;

namespace UnitTests;

public class NumericsTests
{
    [Test]
    public void LinearSolver_SolvesKnownSystem()
    {
        // Arrange
        double[,] a = { { 2, 1, -1 }, { -3, -1, 2 }, { -2, 1, 2 } };
        double[] b = [8, -11, -3];

        // Act
        double[] x = LinearSolver.Solve(a, b);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(x[0], Is.EqualTo(2.0).Within(1e-10));
            Assert.That(x[1], Is.EqualTo(3.0).Within(1e-10));
            Assert.That(x[2], Is.EqualTo(-1.0).Within(1e-10));
        }
    }

    [Test]
    public void LinearSolver_ThrowsOnSingularMatrix()
    {
        double[,] a = { { 1, 2 }, { 2, 4 } };
        double[] b = [1, 2];
        Assert.Throws<InvalidOperationException>(() => LinearSolver.Solve(a, b));
    }

    [Test]
    public void Pava_PoolsViolators()
    {
        double[] result = Pava.FitNonDecreasing([3, 1, 2], [1, 1, 1]);
        Assert.That(result, Is.EqualTo(new double[] { 2, 2, 2 }));
    }

    [Test]
    public void Pava_LeavesMonotoneInputUnchanged()
    {
        double[] result = Pava.FitNonDecreasing([1, 2, 2, 5], [1, 1, 1, 1]);
        Assert.That(result, Is.EqualTo(new double[] { 1, 2, 2, 5 }));
    }

    [Test]
    public void Pava_RespectsWeights()
    {
        // Pooled weighted mean of (10, w=1) and (0, w=3) is 2.5.
        double[] result = Pava.FitNonDecreasing([10, 0], [1, 3]);
        Assert.That(result, Is.EqualTo([2.5, 2.5]));
    }

    [Test]
    public void Pava_ResultIsNonDecreasing()
    {
        // Arrange
        var rng = new Random(5);
        var values = new double[50];
        var weights = new double[50];
        for (int i = 0; i < 50; i++)
        {
            values[i] = rng.NextDouble() * 10;
            weights[i] = rng.NextDouble() + 0.1;
        }

        // Act
        double[] result = Pava.FitNonDecreasing(values, weights);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            for (int i = 1; i < result.Length; i++)
            {
                Assert.That(result[i], Is.GreaterThanOrEqualTo(result[i - 1]));
            }
        }
    }
}
