using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal class DependencyHealthCountEnricherSpecs
{
    [TestMethod]
    internal async Task Counts_stale_direct_transitive_dependency()
    {
        string depKey = PackageInfo.CreatePackageKey("Stale.Lib", "1.0.0");
        var stale = new PackageInfo { Name = "Stale.Lib", Version = "1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30) };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = stale
        });

        await enricher.EnrichAsync(root);

        root.StaleTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Does_not_count_fresh_dependency_as_stale()
    {
        string depKey = PackageInfo.CreatePackageKey("Fresh.Lib", "1.0.0");
        var fresh = new PackageInfo { Name = "Fresh.Lib", Version = "1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2) };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = fresh
        });

        await enricher.EnrichAsync(root);

        root.StaleTransitiveDependencyCount.Should().Be(0);
        root.AbandonedTransitiveDependencyCount.Should().Be(0);
    }

    [TestMethod]
    internal async Task Counts_abandoned_dependency_when_stale_and_low_contributor_count()
    {
        string depKey = PackageInfo.CreatePackageKey("Abandoned.Lib", "1.0.0");
        var abandoned = new PackageInfo
        {
            Name = "Abandoned.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            ContributorCount = 1
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = abandoned
        });

        await enricher.EnrichAsync(root);

        root.AbandonedTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Counts_abandoned_dependency_when_stale_and_has_known_vulnerabilities()
    {
        string depKey = PackageInfo.CreatePackageKey("Vuln.Stale.Lib", "1.0.0");
        var staleWithVulns = new PackageInfo
        {
            Name = "Vuln.Stale.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            ContributorCount = 10,
            VulnerabilityCount = 2
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = staleWithVulns
        });

        await enricher.EnrichAsync(root);

        root.AbandonedTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Does_not_count_stale_dependency_with_good_health_as_abandoned()
    {
        string depKey = PackageInfo.CreatePackageKey("Healthy.Stale.Lib", "1.0.0");
        var healthyStale = new PackageInfo
        {
            Name = "Healthy.Stale.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            ContributorCount = 10,
            VulnerabilityCount = 0,
            MaxVulnerabilitySeverity = 0
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = healthyStale
        });

        await enricher.EnrichAsync(root);

        root.StaleTransitiveDependencyCount.Should().Be(1);
        root.AbandonedTransitiveDependencyCount.Should().Be(0);
    }

    [TestMethod]
    internal async Task Counts_deprecated_transitive_dependency()
    {
        string depKey = PackageInfo.CreatePackageKey("Deprecated.Lib", "1.0.0");
        var deprecated = new PackageInfo { Name = "Deprecated.Lib", Version = "1.0.0", IsDeprecated = true };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = deprecated
        });

        await enricher.EnrichAsync(root);

        root.DeprecatedTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Counts_unmaintained_critical_transitive_dependency()
    {
        string depKey = PackageInfo.CreatePackageKey("Critical.Stale.Lib", "1.0.0");
        var criticalStale = new PackageInfo
        {
            Name = "Critical.Stale.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            VulnerabilityCount = 1,
            MaxVulnerabilitySeverity = 9.0
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = criticalStale
        });

        await enricher.EnrichAsync(root);

        root.UnmaintainedCriticalTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Does_not_count_stale_low_severity_as_unmaintained_critical()
    {
        string depKey = PackageInfo.CreatePackageKey("Low.Severity.Lib", "1.0.0");
        var staleLowSeverity = new PackageInfo
        {
            Name = "Low.Severity.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            VulnerabilityCount = 1,
            MaxVulnerabilitySeverity = 3.0
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [depKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [depKey] = staleLowSeverity
        });

        await enricher.EnrichAsync(root);

        root.UnmaintainedCriticalTransitiveDependencyCount.Should().Be(0);
    }

    [TestMethod]
    internal async Task Counts_stale_dependencies_nested_transitively()
    {
        string directKey = PackageInfo.CreatePackageKey("Direct.Lib", "1.0.0");
        string transitiveKey = PackageInfo.CreatePackageKey("Transitive.Stale.Lib", "1.0.0");

        var transitiveStale = new PackageInfo
        {
            Name = "Transitive.Stale.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30)
        };
        var direct = new PackageInfo
        {
            Name = "Direct.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            DependencyKeys = [transitiveKey]
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [directKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [directKey] = direct,
            [transitiveKey] = transitiveStale
        });

        await enricher.EnrichAsync(root);

        root.StaleTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Does_not_double_count_shared_stale_dependency()
    {
        string sharedKey = PackageInfo.CreatePackageKey("Shared.Stale.Lib", "1.0.0");
        string dep1Key = PackageInfo.CreatePackageKey("Dep1.Lib", "1.0.0");
        string dep2Key = PackageInfo.CreatePackageKey("Dep2.Lib", "1.0.0");

        var shared = new PackageInfo
        {
            Name = "Shared.Stale.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30)
        };
        var dep1 = new PackageInfo { Name = "Dep1.Lib", Version = "1.0.0", DependencyKeys = [sharedKey] };
        var dep2 = new PackageInfo { Name = "Dep2.Lib", Version = "1.0.0", DependencyKeys = [sharedKey] };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [dep1Key, dep2Key] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [dep1Key] = dep1,
            [dep2Key] = dep2,
            [sharedKey] = shared
        });

        await enricher.EnrichAsync(root);

        root.StaleTransitiveDependencyCount.Should().Be(1);
    }

    [TestMethod]
    internal async Task Handles_missing_key_in_dictionary_gracefully()
    {
        var root = new PackageInfo
        {
            Name = "Root",
            Version = "1.0.0",
            DependencyKeys = ["nuget|Missing.Lib|1.0.0"]
        };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>());

        await enricher.EnrichAsync(root);

        root.StaleTransitiveDependencyCount.Should().Be(0);
        root.AbandonedTransitiveDependencyCount.Should().Be(0);
        root.DeprecatedTransitiveDependencyCount.Should().Be(0);
        root.UnmaintainedCriticalTransitiveDependencyCount.Should().Be(0);
    }

    [TestMethod]
    internal async Task Handles_circular_dependency_without_stack_overflow()
    {
        string aKey = PackageInfo.CreatePackageKey("A.Lib", "1.0.0");
        string bKey = PackageInfo.CreatePackageKey("B.Lib", "1.0.0");

        var packageA = new PackageInfo
        {
            Name = "A.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            DependencyKeys = [bKey]
        };
        var packageB = new PackageInfo
        {
            Name = "B.Lib",
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            DependencyKeys = [aKey]
        };
        var root = new PackageInfo { Name = "Root", Version = "1.0.0", DependencyKeys = [aKey] };

        var enricher = new DependencyHealthCountEnricher(new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [aKey] = packageA,
            [bKey] = packageB
        });

        var act = async () => await enricher.EnrichAsync(root);
        await act.Should().NotThrowAsync();
        root.StaleTransitiveDependencyCount.Should().Be(2);
    }
}
