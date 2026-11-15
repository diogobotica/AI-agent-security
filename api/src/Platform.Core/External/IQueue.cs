namespace Platform.Core.External;

/// <summary>An opaque queue message. <see cref="MessageId"/> is the idempotency key (duplicate detection).</summary>
public sealed record QueueMessage(string MessageId, byte[] Body);

/// <summary>The message queue between the API and the worker (Service Bus; the emulator locally).</summary>
public interface IQueue
{
    /// <summary>Sends a message; its <see cref="QueueMessage.MessageId"/> becomes the queue's message ID.</summary>
    Task SendAsync(string queueName, QueueMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Receives and removes one message (receive-and-delete, D-021), or returns null if none arrives within
    /// <paramref name="maxWait"/>.
    /// </summary>
    Task<QueueMessage?> ReceiveAsync(string queueName, TimeSpan maxWait, CancellationToken cancellationToken);
}
