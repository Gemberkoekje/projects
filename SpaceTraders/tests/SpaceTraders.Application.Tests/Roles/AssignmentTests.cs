using FluentAssertions;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>Slice 6.9: the role board shares the work by the assignment that earns the most in total.</summary>
public sealed class AssignmentTests
{
    [Fact]
    public void EachRow_GetsTheColumnThatMakesTheTotalLargest()
    {
        // Greedy by row would give row 0 column 0 (7), row 1 column 2 (9) and row 2 column 1 (8): 24, which is also the
        // best here; the second matrix is where greed fails.
        Assignment.Maximize(Matrix([7, 5, 1], [6, 4, 9], [2, 8, 3])).Should().Equal(0, 2, 1);

        // Row 0 wants column 0 (10), but row 1 can only use column 0 (9): 9 + 8 beats 10 + 0.
        Assignment.Maximize(Matrix([10, 8], [9, 0])).Should().Equal(1, 0);
    }

    [Fact]
    public void MoreColumnsThanRows_LeaveSomeUnused()
    {
        Assignment.Maximize(Matrix([1, 8, 3], [4, 6, 0])).Should().Equal(1, 0);
    }

    [Fact]
    public void FewerColumnsThanRows_IsRefused()
    {
        var refused = () => Assignment.Maximize(Matrix([1], [2]));

        refused.Should().Throw<ArgumentException>();
    }

    private static double[,] Matrix(params double[][] rows)
    {
        var matrix = new double[rows.Length, rows[0].Length];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                matrix[row, column] = rows[row][column];
            }
        }

        return matrix;
    }
}
