namespace PackageGuard.Core.Common;

/// <summary>Small statistical helpers used when turning raw signals into risk metrics.</summary>
internal static class Statistics
{
    /// <summary>
    /// Computes the median of <paramref name="values"/>, or returns <see langword="null"/> if the list is empty.
    /// </summary>
    /// <remarks>Sorts <paramref name="values"/> in place.</remarks>
    public static double? ComputeMedian(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2.0
            : values[middle];
    }
}
