using LlmUnitTestGenerator.Models;

namespace LlmUnitTestGenerator.Services;

public sealed class WebhookProcessorService(WebhookQueue queue, TestGenerationWorkflow workflow, ILogger<WebhookProcessorService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (PushWorkItem item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                logger.LogInformation("Processing push webhook for {Repository} at {CommitSha}.", item.RepositoryFullName, item.CommitSha);
                await workflow.ProcessPushAsync(item, stoppingToken);
                logger.LogInformation("Completed webhook processing for {Repository}.", item.RepositoryFullName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook processing failed for {Repository}.", item.RepositoryFullName);
            }
        }
    }
}
