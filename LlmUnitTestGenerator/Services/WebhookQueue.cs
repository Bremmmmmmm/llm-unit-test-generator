using System.Threading.Channels;
using LlmUnitTestGenerator.Models;

namespace LlmUnitTestGenerator.Services;

public sealed class WebhookQueue
{
    private readonly Channel<PushWorkItem> _channel = Channel.CreateUnbounded<PushWorkItem>();

    public ValueTask EnqueueAsync(PushWorkItem item, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<PushWorkItem> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
