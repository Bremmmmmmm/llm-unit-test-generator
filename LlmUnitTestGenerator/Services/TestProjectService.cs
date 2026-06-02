using System.Xml.Linq;
using LlmUnitTestGenerator.Options;
using Microsoft.Extensions.Options;

namespace LlmUnitTestGenerator.Services;

public sealed class TestProjectService(IOptions<GeneratorOptions> options)
{
    public string EnsureTestProject(string repositoryPath, string apiProjectPath)
    {
        var outputOptions = options.Value.Output;
        var testProjectDir = Path.Combine(repositoryPath, outputOptions.TestProjectDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(testProjectDir);

        var csprojPath = Path.Combine(testProjectDir, $"{outputOptions.TestProjectName}.csproj");
        var targetFramework = ReadTargetFramework(apiProjectPath);
        if (!File.Exists(csprojPath))
        {
            File.WriteAllText(csprojPath, BuildProjectFileContent(outputOptions.TestProjectName, apiProjectPath, testProjectDir, targetFramework));
            return testProjectDir;
        }

        EnsureProjectReference(csprojPath, apiProjectPath, testProjectDir);
        return testProjectDir;
    }

    public string FindApiProjectPath(string repositoryPath)
    {
        var projectFiles = Directory
            .EnumerateFiles(repositoryPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsTestProject(path))
            .ToList();

        if (projectFiles.Count == 0)
        {
            throw new InvalidOperationException("Could not find a non-test API .csproj in the target repository.");
        }

        return projectFiles[0];
    }

    /// <summary>
    /// Determines if a .csproj file is a test project based on naming conventions.
    /// </summary>
    private static bool IsTestProject(string projectPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(projectPath);
        var dirName = Path.GetDirectoryName(projectPath) ?? "";

        // Check if the project file name ends with .Tests or .Test
        if (fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) || 
            fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if the project is in a Tests/ or Test/ directory
        if (dirName.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            dirName.Contains($"{Path.DirectorySeparatorChar}Test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            dirName.EndsWith($"{Path.DirectorySeparatorChar}Tests", StringComparison.OrdinalIgnoreCase) ||
            dirName.EndsWith($"{Path.DirectorySeparatorChar}Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string BuildProjectFileContent(string projectName, string apiProjectPath, string testProjectDir, string targetFramework)
    {
        var projectReference = Path.GetRelativePath(testProjectDir, apiProjectPath).Replace('\\', '/');

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
            <PackageReference Include="xunit" Version="2.9.0" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
          </ItemGroup>

          <ItemGroup>
            <ProjectReference Include="{{projectReference}}" />
          </ItemGroup>
        </Project>
        """;
    }

    private static void EnsureProjectReference(string csprojPath, string apiProjectPath, string testProjectDir)
    {
        var doc = XDocument.Load(csprojPath);
        var relativeRef = Path.GetRelativePath(testProjectDir, apiProjectPath).Replace('\\', '/');

        var existingReference = doc.Descendants("ProjectReference")
            .FirstOrDefault(element => string.Equals(element.Attribute("Include")?.Value, relativeRef, StringComparison.OrdinalIgnoreCase));

        if (existingReference is not null)
        {
            return;
        }

        var itemGroup = doc.Descendants("ItemGroup").FirstOrDefault() ?? new XElement("ItemGroup");
        if (itemGroup.Parent is null)
        {
            doc.Root?.Add(itemGroup);
        }

        itemGroup.Add(new XElement("ProjectReference", new XAttribute("Include", relativeRef)));
        doc.Save(csprojPath);
    }

    private static string ReadTargetFramework(string apiProjectPath)
    {
        var apiProjectDoc = XDocument.Load(apiProjectPath);
        var targetFramework = apiProjectDoc.Descendants("TargetFramework").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            return targetFramework;
        }

        return "net8.0";
    }
}
