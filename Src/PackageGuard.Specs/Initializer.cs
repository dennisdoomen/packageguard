using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PackageGuard.Specs;

[TestClass]
public class Initializer
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        License.Accepted = true;
    }
}
