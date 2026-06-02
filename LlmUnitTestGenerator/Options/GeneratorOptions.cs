namespace LlmUnitTestGenerator.Options;

public sealed class GeneratorOptions
{
    public const string SectionName = "Generator";

    public GitHubOptions GitHub { get; init; } = new();
    public OllamaOptions Ollama { get; init; } = new();
    public WorkspaceOptions Workspace { get; init; } = new();
    public OutputOptions Output { get; init; } = new();
}

public sealed class GitHubOptions
{
    public string RepositoryFullName { get; init; } = string.Empty;
    public string RepositoryCloneUrl { get; init; } = string.Empty;
    public string TriggerBranch { get; init; } = "main";
    public string MainBranch { get; init; } = "main";
    public string WebhookSecret { get; init; } = string.Empty;
    public string PatToken { get; init; } = string.Empty;
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "codellama:7b";
}

public sealed class WorkspaceOptions
{
    public string LocalRepositoryRoot { get; init; } = "./work";
}

public sealed class OutputOptions
{
    public string TargetBranch { get; init; } = "Main-tested";
    public string TestProjectDirectory { get; init; } = "tests/GeneratedApiTests";
    public string TestProjectName { get; init; } = "GeneratedApiTests";
    public string CommitMessage { get; init; } = "chore: regenerate endpoint unit tests";
}
