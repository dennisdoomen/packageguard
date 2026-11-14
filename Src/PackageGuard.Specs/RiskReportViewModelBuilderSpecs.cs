using System.IO;
using System.Linq;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Package;
using PackageGuard.Reports;

namespace PackageGuard.Specs;

[TestClass]
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal sealed class RiskReportViewModelBuilderSpecs
{
    [TestMethod]
    public void Leaves_out_the_evidence_groups_without_evidence()
    {
        var package = new PackageInfo { Name = "Bare", Version = "1.0.0", Source = "nuget" };

        EvidenceGroup group = RiskReportViewModelBuilder.BuildEvidenceGroups(package).Should().ContainSingle().Subject;

        group.Title.Should().Be("Package");
        group.Entries.Should().Equal(new EvidenceEntry("License", "Missing or undetermined"));
    }

    [TestMethod]
    public void Groups_the_evidence_of_a_package_by_topic()
    {
        var package = new PackageInfo
        {
            Name = "Contoso",
            Version = "1.0.0",
            Source = "npm",
            License = "MIT",
            IsPackageSigned = true,
            ContributorCount = 4,
            HasBranchProtection = false,
            PrereleaseRatio = 0.25,
            StaleTransitiveDependencyCount = 2,
            StaleTransitiveDependencyDetails = ["left-pad 1.0.0", "is-odd 0.1.0"]
        };

        package.TrackAsUsedInProject("frontend");

        var groups = RiskReportViewModelBuilder.BuildEvidenceGroups(package).ToDictionary(group => group.Title);

        groups.Keys.Should().Equal("Package", "Security", "Maintenance", "Releases");
        groups["Package"].Entries.Should().Contain(new EvidenceEntry("Used by", Path.Combine("frontend", "package.json")));

        groups["Package"].Entries.Should()
            .Contain(new EvidenceEntry("License", "MIT", Url: "https://spdx.org/licenses/MIT.html"));
        groups["Security"].Entries.Should().Contain(new EvidenceEntry("Package signature", "Signed"));
        groups["Security"].Entries.Should().ContainSingle(entry => entry.Label == "Stale transitive dependencies")
            .Which.Items.Should().Equal("left-pad 1.0.0", "is-odd 0.1.0");

        groups["Maintenance"].Entries.Should().Equal(
            new EvidenceEntry("Contributors", "4"),
            new EvidenceEntry("Branch protection", "Not detected"));

        groups["Releases"].Entries.Should().Equal(new EvidenceEntry("Prerelease ratio", "25 %"));
    }

    [TestMethod]
    [DataRow("License URL (+0.0)", "https://github.com/contoso/lib/blob/HEAD/LICENSE")]
    [DataRow("Public repository available (+0.0)", "https://github.com/contoso/lib.git")]
    [DataRow("README looks present and non-default (+0.0)", "https://github.com/contoso/lib#readme")]
    [DataRow("CONTRIBUTING guide is present (+0.0)", "https://github.com/contoso/lib/blob/HEAD/CONTRIBUTING.md")]
    [DataRow("CHANGELOG or release notes are present (+0.0)", "https://github.com/contoso/lib/releases")]
    [DataRow("OpenSSF Scorecard score is low (4.0) (+1.5)", "https://securityscorecards.dev/viewer/?uri=github.com/contoso/lib")]
    public void Links_a_rationale_to_the_page_on_GitHub_that_backs_it(string rationale, string expectedUrl)
    {
        var package = new PackageInfo { RepositoryUrl = "https://github.com/contoso/lib.git", HasReleaseNotes = true };

        RiskReportViewModelBuilder.TryGetRationaleUrl(package, rationale).Should().Be(expectedUrl);
    }

    [TestMethod]
    public void Does_not_link_a_rationale_that_has_no_page_to_link_to()
    {
        var package = new PackageInfo { RepositoryUrl = "https://gitlab.com/contoso/lib", HasChangelog = true };

        RiskReportViewModelBuilder.TryGetRationaleUrl(package, "README looks present and non-default (+0.0)").Should().BeNull();
        RiskReportViewModelBuilder.TryGetRationaleUrl(package, "CHANGELOG or release notes are present (+0.0)").Should().BeNull();
        RiskReportViewModelBuilder.TryGetRationaleUrl(package, "Contributor count is healthy (5) (+0.0)").Should().BeNull();
    }

    [TestMethod]
    public void Does_not_link_an_unknown_license_without_a_license_url()
    {
        RiskReportViewModelBuilder.TryGetLicenseInfoUrl("Unknown", null).Should().BeNull();
        RiskReportViewModelBuilder.TryGetLicenseInfoUrl(null, "ftp://example.com/license").Should().BeNull();
        RiskReportViewModelBuilder.TryGetLicenseInfoUrl("Custom", "https://example.com/license")
            .Should().Be("https://example.com/license");
    }
}
