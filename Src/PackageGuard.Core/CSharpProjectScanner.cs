using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.Common;
using Pathy;

namespace PackageGuard.Core;

public class CSharpProjectScanner(ILogger logger)
{
    /// <summary>
    /// Can be used to select a single solution in case the scanner finds more than one.
    /// </summary>
    public Func<string[], string>? SelectSolution { get; set;}

    public List<string> FindProjects(string path)
    {
        ChainablePath pathy = path;

        logger.LogHeader($"Finding projects in {path}");

        List<ChainablePath> projectFiles = new();

        // If it points to an existing C# project, use that one
        if (pathy.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            if (pathy.IsFile)
            {
                logger.LogDebug("Including project {Path}", path);
                projectFiles.Add(path);
            }
            else
            {
                throw new FileNotFoundException($"The project file \"{pathy}\" does not exist");
            }
        }

        ChainablePath? solution = null;

        // If it points an actual solution file, continue with that one
        if (pathy.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            if (pathy.IsFile)
            {
                solution = path;
            }
            else
            {
                logger.LogWarning("Solution {Path} does not exist", path);
                return new List<string>();
            }
        }

        if (solution is null && projectFiles.Count == 0)
        {
            if (pathy == ChainablePath.Empty)
            {
                pathy = ChainablePath.Current;
            }

            string[] solutions = Directory.GetFiles(pathy, "*.sln", SearchOption.TopDirectoryOnly);
            if (solutions.Length == 0)
            {
                logger.LogInformation("No solution found in {Path}", path);
            }
            else if (solutions.Length == 1)
            {
                solution = solutions[0];
            }
            else if (SelectSolution is not null)
            {
                logger.LogInformation("Multiple solutions found in {Path}", path);

                solution = SelectSolution(solutions);
            }
            else
            {
                throw new InvalidOperationException("Multiple solutions found, so please select one directly.");
            }
        }

        if (solution is not null)
        {
            logger.LogInformation("Using solution {Solution}", solution);

            var solutionFile = SolutionFile.Parse(Path.GetFullPath(solution!, Environment.CurrentDirectory));
            foreach (ProjectInSolution? project in solutionFile.ProjectsInOrder)
            {
                if (project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
                {
                    projectFiles.Add(project.AbsolutePath);
                    logger.LogInformation("Including project {Path}", project.AbsolutePath);
                }
                else
                {
                    logger.LogInformation("Skipping project {Path} of type {ProjectType}", project.AbsolutePath, project.ProjectType);
                }
            }
        }

        return projectFiles.Select(x => x.ToString()).ToList();
    }
}
