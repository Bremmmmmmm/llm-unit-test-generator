using System.Text.RegularExpressions;
using LlmUnitTestGenerator.Models;

namespace LlmUnitTestGenerator.Services;

public sealed partial class EndpointDiscoveryService
{
    public IReadOnlyList<DiscoveredController> DiscoverControllers(string repositoryPath)
    {
        var files = Directory
            .EnumerateFiles(repositoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(".g.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var controllers = new List<DiscoveredController>();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("ControllerBase", StringComparison.Ordinal) || !source.Contains("[Http", StringComparison.Ordinal))
            {
                continue;
            }

            var classMatch = ControllerClassRegex().Match(source);
            if (!classMatch.Success)
            {
                continue;
            }

            var controllerName = classMatch.Groups[1].Value;
            var routePrefix = RouteRegex().Match(source).Groups[1].Value;
            var endpointMatches = EndpointRegex().Matches(source);

            var endpoints = new List<DiscoveredEndpoint>();
            foreach (Match endpointMatch in endpointMatches)
            {
                var httpMethod = endpointMatch.Groups[1].Value;
                var routeTemplate = endpointMatch.Groups[2].Success ? endpointMatch.Groups[2].Value : string.Empty;
                var actionName = endpointMatch.Groups[3].Value;

                endpoints.Add(new DiscoveredEndpoint
                {
                    HttpMethod = httpMethod,
                    RouteTemplate = routeTemplate,
                    ActionName = actionName
                });
            }

            if (endpoints.Count == 0)
            {
                continue;
            }

            controllers.Add(new DiscoveredController
            {
                ControllerName = controllerName,
                RoutePrefix = routePrefix,
                SourceCode = source,
                Endpoints = endpoints
            });
        }

        return controllers;
    }

    [GeneratedRegex(@"class\s+(\w+Controller)\b")]
    private static partial Regex ControllerClassRegex();

    [GeneratedRegex(@"\[Route\(""([^""\)]*)""\)\]")]
    private static partial Regex RouteRegex();

    [GeneratedRegex(@"\[(HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch|HttpHead|HttpOptions)(?:\(""([^""\)]*)""\))?\][\s\S]*?(?:public|protected)\s+[\w<>,\[\]\?\s]+\s+(\w+)\s*\(")]
    private static partial Regex EndpointRegex();
}
