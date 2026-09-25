namespace PackageGuard.Core.Init;

/// <summary>
/// Summarizes how often a single license (or the absence of one) occurs among the packages found by
/// <c>packageguard init</c>.
/// </summary>
/// <param name="License">The SPDX license identifier, or <see langword="null"/> when it could not be resolved.</param>
/// <param name="PackageCount">The number of packages that resolved to this license.</param>
/// <param name="Category">The copyleft classification of <paramref name="License"/>.</param>
/// <param name="ExamplePackages">A handful of "name version" examples of packages using this license.</param>
public record LicenseUsage(string? License, int PackageCount, LicenseCategory Category, IReadOnlyList<string> ExamplePackages);
