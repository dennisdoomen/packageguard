namespace PackageGuard.Core.CSharp;

/// <summary>
/// Keeps the nested lookups that the dependency analysis builds readable.
/// </summary>
internal static class SortedDictionaryExtensions
{
    /// <summary>
    /// Returns the value stored under <paramref name="key" />, adding a new one when it is not there yet.
    /// </summary>
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        where TValue : new()
    {
        if (!dictionary.TryGetValue(key, out TValue? value))
        {
            dictionary[key] = value = new TValue();
        }

        return value;
    }
}
