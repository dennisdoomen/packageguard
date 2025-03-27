using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using PublicApiGenerator;
using VerifyTests;
using VerifyTests.DiffPlex;
using VerifyXunit;
using Xunit;

namespace PackageGuard.ApiVerificationTests;

public class ApiApproval
{
    static ApiApproval() => VerifyDiffPlex.Initialize(OutputType.Minimal);

    [Fact]
    public Task ApproveApi()
    {
        var configuration = typeof(ApiApproval).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assemblyFile = CombinedPaths("PackageGuard.Core", "bin", configuration, "net8.0", "PackageGuard.Core.dll");
        var assembly = Assembly.LoadFile(assemblyFile);
        var publicApi = assembly.GeneratePublicApi(options: null);

        return Verifier
            .Verify(publicApi)
            .ScrubLinesContaining("FrameworkDisplayName")
            .UseDirectory("ApprovedApi")
            .UseFileName("net8.0")
            .DisableDiff();
    }

    private static string CombinedPaths(params string[] paths) =>
        Path.GetFullPath(Path.Combine(paths.Prepend(GetSolutionDirectory()).ToArray()));

    private static string GetSolutionDirectory([CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "..");
}
