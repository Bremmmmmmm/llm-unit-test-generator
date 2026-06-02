using System.Text;
using LlmUnitTestGenerator.Models;
using LlmUnitTestGenerator.Options;
using Microsoft.Extensions.Options;

namespace LlmUnitTestGenerator.Services;

public sealed class TestGenerationWorkflow(
    RepositorySyncService repositorySyncService,
    EndpointDiscoveryService endpointDiscoveryService,
    TestProjectService testProjectService,
    OllamaClient ollamaClient,
    IOptions<GeneratorOptions> options,
    ILogger<TestGenerationWorkflow> logger)
{
    public async Task ProcessPushAsync(PushWorkItem workItem, CancellationToken cancellationToken)
    {
        var repositoryPath = await repositorySyncService.SyncMainAsync(cancellationToken);

        var discoveredControllers = endpointDiscoveryService.DiscoverControllers(repositoryPath);
        if (discoveredControllers.Count == 0)
        {
            logger.LogWarning("No API endpoints discovered in repository {Repository}.", workItem.RepositoryFullName);
            return;
        }

        var apiProjectPath = testProjectService.FindApiProjectPath(repositoryPath);
        var testProjectDirectory = testProjectService.EnsureTestProject(repositoryPath, apiProjectPath);
        var generatedDirectory = Path.Combine(testProjectDirectory, "Generated");

        if (Directory.Exists(generatedDirectory))
        {
            Directory.Delete(generatedDirectory, recursive: true);
        }

        Directory.CreateDirectory(generatedDirectory);

        foreach (var controller in discoveredControllers)
        {
            var prompt = BuildPrompt(controller, options.Value.Output.TestProjectName);
            var llmResponse = await ollamaClient.GenerateAsync(prompt, cancellationToken);
            var testCode = NormalizeGeneratedCode(llmResponse, controller, $"{options.Value.Output.TestProjectName}.Generated");
            var filePath = Path.Combine(generatedDirectory, $"{controller.ControllerName}Tests.cs");
            await File.WriteAllTextAsync(filePath, testCode, cancellationToken);
        }

        await repositorySyncService.PublishGeneratedTestsAsync(repositoryPath, cancellationToken);
    }

    private static string BuildPrompt(DiscoveredController controller, string testProjectNamespace)
    {
        var endpointLines = string.Join(
            Environment.NewLine,
            controller.Endpoints.Select(e => $"- {e.HttpMethod} {JoinRoute(controller.RoutePrefix, e.RouteTemplate)} -> {e.ActionName}"));

        return $$"""
        Generate C# xUnit unit tests for this ASP.NET Core API controller.

        Requirements:
        - Return only valid C# code, no markdown fences.
        - Use namespace {{testProjectNamespace}}.Generated.
        - Use xUnit attributes.
        - Include at least one test per endpoint.
        - Keep tests deterministic and compile-ready.
        - Name test class {{controller.ControllerName}}Tests.

        Controller endpoints:
        {{endpointLines}}

        Controller source:
        {{controller.SourceCode}}
        """;
    }

    private static string NormalizeGeneratedCode(string generatedCode, DiscoveredController controller, string generatedNamespace)
    {
        var trimmed = generatedCode.Trim();

        // Step 1: Strip markdown code fences if present
        trimmed = StripMarkdownFences(trimmed);

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return BuildFallbackTest(controller, generatedNamespace);
        }

        return trimmed + Environment.NewLine;
    }

    /// <summary>
    /// Removes markdown code fences (``` or ```c#) from the beginning and end of the text.
    /// </summary>
    private static string StripMarkdownFences(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var startIndex = 0;
        var endIndex = lines.Length;

        // Find opening fence
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                startIndex = i + 1;
                break;
            }
        }

        // Find closing fence
        for (int i = lines.Length - 1; i >= startIndex; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                endIndex = i;
                break;
            }
        }

        // If we found fences, extract content between them
        if (startIndex < endIndex)
        {
            var contentLines = lines[startIndex..endIndex];
            return string.Join(Environment.NewLine, contentLines).Trim();
        }

        // No fences found, return original text
        return text;
    }

    /// <summary>
    /// Finds the index where actual C# code begins by looking for common C# keywords.
    /// </summary>
    private static int FindCodeStartIndex(string text)
    {
        var codeKeywords = new[] { "using", "namespace", "public", "private", "internal", "class", "[" };
        var earliestIndex = int.MaxValue;

        foreach (var keyword in codeKeywords)
        {
            var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < earliestIndex)
            {
                earliestIndex = index;
            }
        }

        return earliestIndex == int.MaxValue ? 0 : earliestIndex;
    }

    private static string BuildFallbackTest(DiscoveredController controller, string generatedNamespace)
    {
        var testMethods = new StringBuilder();
        foreach (var endpoint in controller.Endpoints)
        {
            testMethods.AppendLine("    [Fact(Skip = \"Ollama generation returned empty output\")]");
            testMethods.AppendLine($"    public void {endpoint.ActionName}_GeneratedPlaceholder() => Assert.True(true);");
            testMethods.AppendLine();
        }

        return $$"""
        using Xunit;

        namespace {{generatedNamespace}};

        public class {{controller.ControllerName}}Tests
        {
        {{testMethods.ToString().TrimEnd()}}
        }
        """;
    }

    private static string JoinRoute(string routePrefix, string routeTemplate)
    {
        var left = (routePrefix ?? string.Empty).Trim('/');
        var right = (routeTemplate ?? string.Empty).Trim('/');
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? "/" : $"/{right}";
        }

        return string.IsNullOrWhiteSpace(right) ? $"/{left}" : $"/{left}/{right}";
    }
}
