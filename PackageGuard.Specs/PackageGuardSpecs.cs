using FluentAssertions;
using Xunit;

namespace PackageGuard.Specs;

public class PackageGuardSpecs
{
    // To group tests related to a certain API or capability and avoid having to use long test names
    public class Capability
    {
        // Use a fact-based name
        [Fact]
        public void If_something_happens_it_does_something()
        {
            // Arrange

            // Act

            // Assert
            1.Should().Be(1);
        }
    }
}
