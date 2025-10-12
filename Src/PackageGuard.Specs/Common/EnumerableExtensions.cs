using System;
using System.Collections.Generic;

namespace PackageGuard.Specs.Common;

public static class EnumerableExtensions
{
    // Executes the specified action for each item in the sequence.
    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            action(item);
        }
    }
}
