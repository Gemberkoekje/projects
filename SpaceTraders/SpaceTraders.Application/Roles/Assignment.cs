namespace SpaceTraders.Application.Roles;

/// <summary>
/// The assignment problem (Kuhn–Munkres, the Hungarian method): give each row one column, no column twice, for the
/// largest total. The role board gives each ship one job this way (slice 6.9, D38): a ship that can only survey
/// surveys, and one that can do more does what pays most, because the total counts every ship.
/// </summary>
internal static class Assignment
{
    /// <summary>Gives each row the column that makes the total largest.</summary>
    /// <param name="values">The value of each row's column; at least as many columns as rows.</param>
    /// <returns>For each row, its column.</returns>
    public static int[] Maximize(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var rows = values.GetLength(0);
        var columns = values.GetLength(1);
        if (rows > columns)
        {
            throw new ArgumentException("An assignment needs at least as many columns as rows.", nameof(values));
        }

        // Minimises the negated values, with potentials (u for rows, v for columns), one row at a time; index 0 is
        // the free row and column the method starts each augmenting path from.
        var u = new double[rows + 1];
        var v = new double[columns + 1];
        var rowOf = new int[columns + 1];
        var previous = new int[columns + 1];
        for (var row = 1; row <= rows; row++)
        {
            rowOf[0] = row;
            var column = 0;
            var slack = Enumerable.Repeat(double.PositiveInfinity, columns + 1).ToArray();
            var used = new bool[columns + 1];
            do
            {
                used[column] = true;
                var current = rowOf[column];
                var delta = double.PositiveInfinity;
                var next = 0;
                for (var candidate = 1; candidate <= columns; candidate++)
                {
                    if (used[candidate])
                    {
                        continue;
                    }

                    var reduced = -values[current - 1, candidate - 1] - u[current] - v[candidate];
                    if (reduced < slack[candidate])
                    {
                        slack[candidate] = reduced;
                        previous[candidate] = column;
                    }

                    if (slack[candidate] < delta)
                    {
                        delta = slack[candidate];
                        next = candidate;
                    }
                }

                for (var candidate = 0; candidate <= columns; candidate++)
                {
                    if (used[candidate])
                    {
                        u[rowOf[candidate]] += delta;
                        v[candidate] -= delta;
                    }
                    else
                    {
                        slack[candidate] -= delta;
                    }
                }

                column = next;
            }
            while (rowOf[column] != 0);

            do
            {
                var before = previous[column];
                rowOf[column] = rowOf[before];
                column = before;
            }
            while (column != 0);
        }

        var assigned = new int[rows];
        for (var column = 1; column <= columns; column++)
        {
            if (rowOf[column] != 0)
            {
                assigned[rowOf[column] - 1] = column - 1;
            }
        }

        return assigned;
    }
}
