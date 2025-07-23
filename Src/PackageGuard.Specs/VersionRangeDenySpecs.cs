using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class VersionRangeDenySpecs
{
    [TestMethod]
    public void Can_deny_prerelease_versions_using_version_range()
    {
        // Test the actual functionality by using the public API
        // Create test packages to verify version range functionality
        var preReleasePackage = new PackageInfo
        {
            Name = "TestPackage",
            Version = "0.9.0-alpha.1",
            License = "MIT"
        };
        
        var stablePreOnePackage = new PackageInfo
        {
            Name = "TestPackage", 
            Version = "0.9.0",
            License = "MIT"
        };
        
        var stablePostOnePackage = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT"
        };
        
        // Use SatisfiesRange method which is public
        preReleasePackage.SatisfiesRange("TestPackage", "(,1.0.0)").Should().BeTrue("pre-release version 0.9.0-alpha.1 should satisfy range (,1.0.0)");
        stablePreOnePackage.SatisfiesRange("TestPackage", "(,1.0.0)").Should().BeTrue("stable version 0.9.0 should satisfy range (,1.0.0)");
        stablePostOnePackage.SatisfiesRange("TestPackage", "(,1.0.0)").Should().BeFalse("stable version 1.0.0 should not satisfy range (,1.0.0)");
    }
    
    [TestMethod]
    public void Can_deny_specific_prerelease_version()
    {
        var specificPreRelease = new PackageInfo
        {
            Name = "TestPackage",
            Version = "2.0.0-alpha.2",
            License = "MIT"
        };
        
        var differentPreRelease = new PackageInfo
        {
            Name = "TestPackage",
            Version = "2.0.0-alpha.1",
            License = "MIT"
        };
        
        // Test exact version matching
        specificPreRelease.SatisfiesRange("TestPackage", "2.0.0-alpha.2").Should().BeTrue("specific pre-release 2.0.0-alpha.2 should match exactly");
        differentPreRelease.SatisfiesRange("TestPackage", "2.0.0-alpha.2").Should().BeFalse("different pre-release 2.0.0-alpha.1 should not match exactly");
    }
}