using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace PackageGuard.Specs;

/// <summary>
/// Builds the restore output of a single project, so the dependency analysis can be tested without
/// running a real restore.
/// </summary>
internal sealed class LockFileBuilder(string projectName, string targetFramework = "net9.0")
{
    private readonly NuGetFramework framework = NuGetFramework.Parse(targetFramework);
    private readonly List<LibraryDependency> directPackages = [];
    private readonly List<string> directProjects = [];
    private readonly List<LockFileTargetLibrary> libraries = [];

    public LockFileBuilder WithPackageReference(string name, string version,
        (string Name, string Version)? dependsOn = null, bool privateAssets = false, bool autoReferenced = false)
    {
        directPackages.Add(new LibraryDependency
        {
            LibraryRange = new LibraryRange(name, VersionRange.Parse(version), LibraryDependencyTarget.Package),
            AutoReferenced = autoReferenced,
            SuppressParent = privateAssets ? LibraryIncludeFlags.All : LibraryIncludeFlags.None
        });

        AddLibrary(name, version, "package", dependsOn);

        return this;
    }

    public LockFileBuilder WithProjectReference(string name, (string Name, string Version)? provides = null)
    {
        directProjects.Add(name);
        AddLibrary(name, "1.0.0", "project", provides);

        return this;
    }

    public LockFile Build()
    {
        var frameworkInfo = new ProjectRestoreMetadataFrameworkInfo(framework);
        foreach (string name in directProjects)
        {
            frameworkInfo.ProjectReferences.Add(new ProjectRestoreReference
            {
                ProjectPath = $"/src/{name}/{name}.csproj",
                ProjectUniqueName = $"/src/{name}/{name}.csproj"
            });
        }

        var packageSpec = new PackageSpec([
            new TargetFrameworkInformation
            {
                FrameworkName = framework,
                Dependencies = [.. directPackages]
            }
        ])
        {
            Name = projectName,
            RestoreMetadata = new ProjectRestoreMetadata
            {
                ProjectName = projectName,
                ProjectPath = $"/src/{projectName}/{projectName}.csproj"
            }
        };

        packageSpec.RestoreMetadata.TargetFrameworks.Add(frameworkInfo);

        var target = new LockFileTarget
        {
            TargetFramework = framework
        };

        foreach (LockFileTargetLibrary library in libraries)
        {
            target.Libraries.Add(library);
        }

        var lockFile = new LockFile
        {
            PackageSpec = packageSpec
        };

        lockFile.Targets.Add(target);

        return lockFile;
    }

    private void AddLibrary(string name, string version, string type, (string Name, string Version)? dependsOn)
    {
        var library = new LockFileTargetLibrary
        {
            Name = name,
            Version = NuGetVersion.Parse(version),
            Type = type
        };

        if (dependsOn is not null)
        {
            library.Dependencies.Add(new PackageDependency(dependsOn.Value.Name,
                VersionRange.Parse(dependsOn.Value.Version)));

            if (libraries.All(x => x.Name != dependsOn.Value.Name))
            {
                libraries.Add(new LockFileTargetLibrary
                {
                    Name = dependsOn.Value.Name,
                    Version = NuGetVersion.Parse(dependsOn.Value.Version),
                    Type = "package"
                });
            }
        }

        LockFileTargetLibrary existing = libraries.SingleOrDefault(x => x.Name == name);
        if (existing is null)
        {
            libraries.Add(library);
        }
        else
        {
            // A direct reference wins over the version a transitive edge asked for, the way restore resolves it.
            existing.Version = library.Version;
            existing.Type = type;
        }
    }
}
