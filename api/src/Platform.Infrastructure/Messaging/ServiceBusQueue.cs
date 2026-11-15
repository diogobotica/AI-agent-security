using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Platform.Core.External;

namespace Platform.Infrastructure.Messaging;

/// <summary>
/// The queue on Service Bus (the emulator locally). Send sets the Service Bus MessageId, so a queue with duplicate
/// detection delivers the same MessageId once (idempotent job messages). Receive is receive-and-delete for now (D-021).
/// Senders and receivers are closed when the client factory is disposed.
/// </summary>
public sealed class ServiceBusQueue(ServiceBusClientFactory clients) : IQueue
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> senders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ServiceBusReceiver> receivers = new(StringComparer.Ordinal);

    public async Task SendAsync(string queueName, QueueMessage message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.MessageId);
        ArgumentNullException.ThrowIfNull(message.Body);

        var client = await clients.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var sender = senders.GetOrAdd(queueName, client.CreateSender);
        var outgoing = new ServiceBusMessage(message.Body) { MessageId = message.MessageId };
        await sender.SendMessageAsync(outgoing, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueueMessage?> ReceiveAsync(string queueName, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxWait, TimeSpan.Zero);

        var client = await clients.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var receiver = receivers.GetOrAdd(
            queueName,
            name => client.CreateReceiver(name, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete }));
        var received = await receiver.ReceiveMessageAsync(maxWait, cancellationToken).ConfigureAwait(false);
        return received is null ? null : new QueueMessage(received.MessageId, received.Body.ToArray());
    }

    public override string ToString() => nameof(ServiceBusQueue);
}
