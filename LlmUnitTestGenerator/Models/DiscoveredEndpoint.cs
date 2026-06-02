namespace LlmUnitTestGenerator.Models;

public sealed class DiscoveredController
{
    public string ControllerName { get; init; } = string.Empty;
    public string RoutePrefix { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public required IReadOnlyList<DiscoveredEndpoint> Endpoints { get; init; }
}

public sealed class DiscoveredEndpoint
{
    public string HttpMethod { get; init; } = string.Empty;
    public string RouteTemplate { get; init; } = string.Empty;
    public string ActionName { get; init; } = string.Empty;
}
