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
        Generate ONLY xUnit unit test code for this ASP.NET Core API controller.

        *** CRITICAL: DO NOT include the controller class definition. DO NOT copy the controller code. ***
        *** Generate ONLY a test class with test methods. ***

        REQUIREMENTS:
        - Return ONLY valid C# code. NO markdown fences, NO explanations, NO comments outside the code.
        - Include REQUIRED using statements at the top (in this exact order):
          * using Xunit;
          * using Moq;
          * using Microsoft.AspNetCore.Mvc;
          * using NoTestApplication.Services;
          * using NoTestApplication.Controllers;
          * using NoTestApplication.Models;
        - Use namespace {{testProjectNamespace}};
        - Use Moq version 4.x syntax (NOT 5.x).
        - Use xUnit [Fact] attributes.
        - Test ONLY the controller methods by mocking dependencies - do NOT test the service.
        - Mock the IObjectService dependency that is injected into the controller constructor.
        - For synchronous controller methods: Use Setup().Returns() (NOT ReturnsAsync).

        *** CRITICAL RETURN TYPE HANDLING ***
        - For ActionResult<T> return types (GetAll, GetById, Create):
          * Extract result using: var result = _controller.MethodName();
          * Then access the Result property: var okResult = Assert.IsType<OkObjectResult>(result.Result);
          * Then check okResult.Value for the actual object
        - For IActionResult return types (Update, Delete):
          * Extract result using: var result = _controller.MethodName();
          * Do NOT use .Result - directly assert on result: Assert.IsType<NoContentResult>(result);
          * Do NOT use .Result - directly assert on result: Assert.IsType<NotFoundObjectResult>(result);

        - Ensure all required properties in DTOs are initialized (Name is required in CreateObjectRequest).
        - Include positive tests (happy path) and negative tests (error cases).
        - Make tests deterministic - use fixed test data, no random values.
        - Each test method must be independently runnable without shared state.
        - Name test class {{controller.ControllerName}}Tests.
        - The test class MUST have a constructor that instantiates the mock service and controller.

        EXAMPLE STRUCTURE (adapt to the actual controller, do not copy exactly):
        ```csharp
        using Xunit;
        using Moq;
        using Microsoft.AspNetCore.Mvc;
        using NoTestApplication.Services;
        using NoTestApplication.Controllers;
        using NoTestApplication.Models;

        namespace {{testProjectNamespace}};

        public class {{controller.ControllerName}}Tests
        {
            private readonly Mock<IObjectService> _mockService;
            private readonly {{controller.ControllerName}} _controller;

            public {{controller.ControllerName}}Tests()
            {
                _mockService = new Mock<IObjectService>();
                _controller = new {{controller.ControllerName}}(_mockService.Object);
            }

            [Fact]
            public void GetAll_ReturnsOkWithObjects()
            {
                // Arrange
                var testObjects = new List<ObjectModel> 
                { 
                    new ObjectModel { Id = 1, Name = "Test", Date = DateTime.Now } 
                };
                _mockService.Setup(s => s.GetAll()).Returns(testObjects);

                // Act
                var result = _controller.GetAll();

                // Assert - ActionResult<T>, use .Result
                var okResult = Assert.IsType<OkObjectResult>(result.Result);
                Assert.NotNull(okResult.Value);
                _mockService.Verify(s => s.GetAll(), Times.Once);
            }

            [Fact]
            public void Create_WithValidRequest_ReturnsCreatedAtAction()
            {
                // Arrange
                var request = new CreateObjectRequest { Name = "New Object", Date = DateTime.Now };
                var createdObject = new ObjectModel { Id = 1, Name = request.Name, Date = request.Date };
                _mockService.Setup(s => s.Create(request)).Returns(createdObject);

                // Act
                var result = _controller.Create(request);

                // Assert - ActionResult<T>, use .Result
                var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
                Assert.Equal(nameof({{controller.ControllerName}}.GetById), createdResult.ActionName);
                var returnedObject = Assert.IsType<ObjectModel>(createdResult.Value);
                Assert.Equal(1, returnedObject.Id);
            }

            [Fact]
            public void Update_WithValidId_ReturnsNoContent()
            {
                // Arrange
                var request = new UpdateObjectRequest { Name = "Updated", Date = DateTime.Now };
                _mockService.Setup(s => s.Update(1, request)).Returns(true);

                // Act
                var result = _controller.Update(1, request);

                // Assert - IActionResult, do NOT use .Result
                Assert.IsType<NoContentResult>(result);
                _mockService.Verify(s => s.Update(1, request), Times.Once);
            }

            [Fact]
            public void Update_WithInvalidId_ReturnsNotFound()
            {
                // Arrange
                var request = new UpdateObjectRequest { Name = "Updated", Date = DateTime.Now };
                _mockService.Setup(s => s.Update(999, request)).Returns(false);

                // Act
                var result = _controller.Update(999, request);

                // Assert - IActionResult, do NOT use .Result
                Assert.IsType<NotFoundObjectResult>(result);
            }

            [Fact]
            public void Delete_WithValidId_ReturnsNoContent()
            {
                // Arrange
                _mockService.Setup(s => s.Delete(1)).Returns(true);

                // Act
                var result = _controller.Delete(1);

                // Assert - IActionResult, do NOT use .Result
                Assert.IsType<NoContentResult>(result);
                _mockService.Verify(s => s.Delete(1), Times.Once);
            }

            [Fact]
            public void Delete_WithInvalidId_ReturnsNotFound()
            {
                // Arrange
                _mockService.Setup(s => s.Delete(999)).Returns(false);

                // Act
                var result = _controller.Delete(999);

                // Assert - IActionResult, do NOT use .Result
                Assert.IsType<NotFoundObjectResult>(result);
            }
        }
        ```

        CONTROLLER TO TEST (do not copy this into generated tests - only use it to understand what to test):
        {{controller.SourceCode}}

        Controller endpoints to test:
        {{endpointLines}}
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

    private static string BuildFallbackTest(DiscoveredController controller, string generatedNamespace)
    {
        var testMethods = new StringBuilder();
        foreach (var endpoint in controller.Endpoints)
        {
            testMethods.AppendLine("    [Fact(Skip = \"Ollama generation returned empty output\")]");
            testMethods.AppendLine($"    public void {endpoint.ActionName}_Placeholder() => Assert.True(true);");
            testMethods.AppendLine();
        }

        return $$"""
        using Xunit;
        using Moq;
        using Microsoft.AspNetCore.Mvc;
        using NoTestApplication.Services;
        using NoTestApplication.Controllers;
        using NoTestApplication.Models;

        namespace {{generatedNamespace}};

        public class {{controller.ControllerName}}Tests
        {
            private readonly Mock<IObjectService> _mockService;
            private readonly {{controller.ControllerName}} _controller;

            public {{controller.ControllerName}}Tests()
            {
                _mockService = new Mock<IObjectService>();
                _controller = new {{controller.ControllerName}}(_mockService.Object);
            }

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
