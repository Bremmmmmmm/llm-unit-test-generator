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

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                trimmed = trimmed[(firstLineEnd + 1)..];
            }

            var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                trimmed = trimmed[..fenceEnd].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return BuildFallbackTest(controller, generatedNamespace);
        }

        return trimmed + Environment.NewLine;
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
