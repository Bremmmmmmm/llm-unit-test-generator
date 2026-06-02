using System.Text.Json.Serialization;

namespace LlmUnitTestGenerator.Models;

public sealed class GitHubPushPayload
{
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = string.Empty;

    [JsonPropertyName("after")]
    public string After { get; init; } = string.Empty;

    [JsonPropertyName("repository")]
    public GitHubRepositoryPayload Repository { get; init; } = new();
}

public sealed class GitHubRepositoryPayload
{
    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = string.Empty;
}

public sealed record PushWorkItem(string RepositoryFullName, string Ref, string CommitSha);
