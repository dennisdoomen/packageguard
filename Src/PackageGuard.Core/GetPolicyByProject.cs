namespace PackageGuard.Core;

/// <summary>
/// Represents a delegate that retrieves the policy for a given project based on its file path.
/// </summary>
/// <param name="projectPath">The file path of the .csproj or package.json for which to fetch the policy.</param>
/// <returns>The <see cref="ProjectPolicy"/> configured for the specified project.</returns>
public delegate ProjectPolicy GetPolicyByProject(string projectPath);
