using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Versioning;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class GitHubRepositoryRiskEnricherSpecs
{
    [TestMethod]
    public void ComputeMajorReleaseRatio_should_be_zero_when_releases_stay_on_same_major_line()
    {
        // Arrange
        (DateTimeOffset PublishedAt, NuGetVersion Version)[] releases =
        [
            (new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("8.7.0")),
            (new DateTimeOffset(2025, 3, 5, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("8.8.0")),
            (new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("8.9.0"))
        ];

        // Act
        double? ratio = GitHubRepositoryRiskEnricher.ComputeMajorReleaseRatio(releases);

        // Assert
        ratio.Should().Be(0.0);
    }

    [TestMethod]
    public void ComputeMajorReleaseRatio_should_reflect_major_version_jumps_between_releases()
    {
        // Arrange
        (DateTimeOffset PublishedAt, NuGetVersion Version)[] releases =
        [
            (new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("1.4.0")),
            (new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("2.0.0")),
            (new DateTimeOffset(2025, 3, 10, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("2.1.0")),
            (new DateTimeOffset(2025, 4, 10, 0, 0, 0, TimeSpan.Zero), NuGetVersion.Parse("3.0.0"))
        ];

        // Act
        double? ratio = GitHubRepositoryRiskEnricher.ComputeMajorReleaseRatio(releases);

        // Assert
        ratio.Should().BeApproximately(2.0 / 3.0, 0.0001);
    }
}
