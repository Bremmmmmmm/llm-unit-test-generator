using System.Text.RegularExpressions;
using LlmUnitTestGenerator.Options;
using Microsoft.Extensions.Options;

namespace LlmUnitTestGenerator.Services;

public sealed class RepositorySyncService(IOptions<GeneratorOptions> options, GitCommandService git, ILogger<RepositorySyncService> logger)
{
    public async Task<string> SyncMainAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        ValidateConfiguration(config);

        var repoRoot = Path.GetFullPath(config.Workspace.LocalRepositoryRoot);
        var repositoryName = config.GitHub.RepositoryFullName.Split('/').Last();
        var localRepositoryPath = Path.Combine(repoRoot, repositoryName);

        // Clean up workspace directory at the start to ensure fresh clone/pull
        if (Directory.Exists(repoRoot))
        {
            logger.LogInformation("Cleaning up workspace directory {Path}.", repoRoot);
            try
            {
                Directory.Delete(repoRoot, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete workspace directory; attempting to continue.");
            }
        }

        Directory.CreateDirectory(repoRoot);

        var authenticatedUrl = BuildAuthenticatedUrl(config.GitHub.RepositoryCloneUrl, config.GitHub.PatToken);

        logger.LogInformation("Cloning repository {Repository} into {Path}.", config.GitHub.RepositoryFullName, localRepositoryPath);
        await git.RunAsync(repoRoot, cancellationToken, "clone", "--branch", config.GitHub.MainBranch, authenticatedUrl, localRepositoryPath);

        return localRepositoryPath;
    }

    public async Task PublishGeneratedTestsAsync(string localRepositoryPath, CancellationToken cancellationToken)
    {
        var output = options.Value.Output;
        var mainBranch = options.Value.GitHub.MainBranch;

        await git.RunAsync(localRepositoryPath, cancellationToken, "fetch", "origin");
        await git.RunAsync(localRepositoryPath, cancellationToken, "checkout", "-B", output.TargetBranch, $"origin/{mainBranch}");
        await git.RunAsync(localRepositoryPath, cancellationToken, "add", ".");

        var status = await git.RunAsync(localRepositoryPath, cancellationToken, "status", "--porcelain");
        if (string.IsNullOrWhiteSpace(status))
        {
            logger.LogInformation("No generated test changes detected; skipping push.");
            return;
        }

        await git.RunAsync(localRepositoryPath, cancellationToken, "commit", "-m", output.CommitMessage);
        await git.RunAsync(localRepositoryPath, cancellationToken, "push", "--set-upstream", "origin", output.TargetBranch, "--force-with-lease");
    }

    private static string BuildAuthenticatedUrl(string cloneUrl, string patToken)
    {
        if (string.IsNullOrWhiteSpace(patToken))
        {
            throw new InvalidOperationException("Generator:GitHub:PatToken is required.");
        }

        if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generator:GitHub:RepositoryCloneUrl must be a valid HTTPS URL.");
        }

        if (Regex.IsMatch(uri.UserInfo, @".+"))
        {
            return cloneUrl;
        }

        var token = Uri.EscapeDataString(patToken);
        var builder = new UriBuilder(uri)
        {
            UserName = token,
            Password = "x-oauth-basic"
        };

        return builder.ToString();
    }

    private static void ValidateConfiguration(GeneratorOptions config)
    {
        if (string.IsNullOrWhiteSpace(config.GitHub.RepositoryFullName))
        {
            throw new InvalidOperationException("Generator:GitHub:RepositoryFullName is required.");
        }

        if (string.IsNullOrWhiteSpace(config.GitHub.RepositoryCloneUrl))
        {
            throw new InvalidOperationException("Generator:GitHub:RepositoryCloneUrl is required.");
        }
    }
}
