﻿using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

public class ProjectScanner(ILogger logger)
{
    /// <summary>
    /// Can be used to select a single solution in case the scanner finds more than one.
    /// </summary>
    public Func<string[], string>? SelectSolution { get; set;}

    public List<string> FindProjects(string path)
    {
        logger.LogHeader($"Finding projects in {path}");

        List<string> projectFiles = new();

        // If it points to a valid csproj, use that one
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                logger.LogDebug("Including project {Path}", path);
                projectFiles.Add(path);
            }
            else
            {
                logger.LogWarning("Project {Path} does not exist", path);
                return new List<string>();
            }
        }

        string? solution = null;

        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            if (Path.Exists(path))
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
            if (path.Length == 0)
            {
                path = Directory.GetCurrentDirectory();
            }

            string[] solutions = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
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

        return projectFiles;
    }
}
