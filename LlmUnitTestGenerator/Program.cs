using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LlmUnitTestGenerator.Models;
using LlmUnitTestGenerator.Options;
using LlmUnitTestGenerator.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GeneratorOptions>(builder.Configuration.GetSection(GeneratorOptions.SectionName));
builder.Services.AddHttpClient<OllamaClient>();
builder.Services.AddSingleton<GitCommandService>();
builder.Services.AddSingleton<RepositorySyncService>();
builder.Services.AddSingleton<EndpointDiscoveryService>();
builder.Services.AddSingleton<TestProjectService>();
builder.Services.AddSingleton<TestGenerationWorkflow>();
builder.Services.AddSingleton<WebhookQueue>();
builder.Services.AddHostedService<WebhookProcessorService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok("llm-unit-test-generator is running"));

app.MapPost("/webhook/github", async (HttpRequest request, WebhookQueue queue, IOptions<GeneratorOptions> options, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("GitHubWebhook");
    var configuredOptions = options.Value;

    request.EnableBuffering();
    using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
    var payload = await reader.ReadToEndAsync(cancellationToken);
    request.Body.Position = 0;

    if (!IsValidSignature(request, payload, configuredOptions.GitHub.WebhookSecret))
    {
        logger.LogWarning("Rejected webhook: invalid signature.");
        return Results.Unauthorized();
    }

    var eventName = request.Headers["X-GitHub-Event"].ToString();
    if (!string.Equals(eventName, "push", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok(new { ignored = true, reason = "event_not_push" });
    }

    GitHubPushPayload? pushPayload;
    try
    {
        pushPayload = JsonSerializer.Deserialize<GitHubPushPayload>(payload);
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Rejected webhook: invalid JSON payload.");
        return Results.BadRequest(new { error = "invalid_json" });
    }

    if (pushPayload is null || pushPayload.Repository is null)
    {
        return Results.BadRequest(new { error = "invalid_push_payload" });
    }

    if (!string.Equals(pushPayload.Repository.FullName, configuredOptions.GitHub.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok(new { ignored = true, reason = "repository_mismatch" });
    }

    var expectedRef = $"refs/heads/{configuredOptions.GitHub.TriggerBranch}";
    if (!string.Equals(pushPayload.Ref, expectedRef, StringComparison.Ordinal))
    {
        return Results.Ok(new { ignored = true, reason = "branch_mismatch" });
    }

    await queue.EnqueueAsync(new PushWorkItem(pushPayload.Repository.FullName, pushPayload.Ref, pushPayload.After), cancellationToken);

    return Results.Accepted(value: new { queued = true });
});

app.Run();

static bool IsValidSignature(HttpRequest request, string payload, string webhookSecret)
{
    if (string.IsNullOrWhiteSpace(webhookSecret))
    {
        return true;
    }

    var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();
    if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    var expected = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(signatureHeader),
        Encoding.UTF8.GetBytes(expected));
}
