using System.Buffers.Binary;
using System.Net.Sockets;

namespace Platform.Api.Health;

/// <summary>
/// Checks PostgreSQL without a driver or credentials: opens a TCP connection, sends the protocol's 8-byte SSLRequest
/// and expects the one-byte answer 'S' (TLS available) or 'N' (no TLS). Then it closes the connection.
/// </summary>
public sealed class TcpPostgresProbe(string host, int port, TimeSpan timeout) : IDependencyProbe
{
    private const int SslRequestLength = 8;
    private const int SslRequestCode = 80877103;

    /// <inheritdoc />
    public string Name => "postgres";

    /// <inheritdoc />
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var token = timeoutSource.Token;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token);
            var stream = client.GetStream();

            var request = new byte[SslRequestLength];
            BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(0, 4), SslRequestLength);
            BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(4, 4), SslRequestCode);
            await stream.WriteAsync(request, token);

            var reply = new byte[1];
            var read = await stream.ReadAsync(reply, token);
            return read == 1 && reply[0] is (byte)'S' or (byte)'N';
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }
}
