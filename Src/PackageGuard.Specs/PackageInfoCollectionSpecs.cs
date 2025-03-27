using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs
{
    [TestClass]
    public class PackageInfoSpecs
    {
        [TestMethod]
        public void Can_match_on_case_insensitive_name_only()
        {
            // Arrange
            var package = new PackageInfo
            {
                Id = "Bogus",
                Version = "1.0.0",
            };

            // Act
            bool isMatch = package.SatisfiesRange("bogus", "1.0.0");

            // Assert
            isMatch.Should().BeTrue();
        }

        [TestMethod]
        public void Can_match_without_version()
        {
            // Arrange
            var package = new PackageInfo
            {
                Id = "Bogus",
                Version = "1.0.0",
            };

            // Act
            bool isMatch = package.SatisfiesRange("Bugos");

            // Assert
            isMatch.Should().BeFalse();
        }

        [TestMethod]
        public void An_exact_version_must_match()
        {
            // Arrange
            var package = new PackageInfo
            {
                Id = "Bogus",
                Version = "2.0.0",
            };

            // Act
            bool isMatch = package.SatisfiesRange("Bugos", "2.0.1");

            // Assert
            isMatch.Should().BeFalse();
        }

        [TestMethod]
        public void An_exact_version_matches()
        {
            // Arrange
            var package = new PackageInfo
            {
                Id = "Bogus",
                Version = "2.0.1",
            };

            // Act
            bool isMatch = package.SatisfiesRange("Bugos", "2.0.1");

            // Assert
            isMatch.Should().BeFalse();
        }

        [TestMethod]
        public void Can_use_an_exclusive_range()
        {
            // Arrange
            var package = new PackageInfo
            {
                Id = "Bogus",
                Version = "2.0.0",
            };

            // Act
            bool isMatch = package.SatisfiesRange("Bugos", "[1.0.0,2.0.0)");

            // Assert
            isMatch.Should().BeFalse();
        }

        [TestMethod]
        public void Can_use_an_inclusive_range()
        {
            // Arrange
            var package = new PackageInfo
            {
                Id = "Bogus",
                Version = "2.0.0",
            };

            // Act
            bool isMatch = package.SatisfiesRange("Bogus", "[1.0.0,2.0.0]");

            // Assert
            isMatch.Should().BeTrue();
        }
    }
}
