using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Platform.Api.Health;

namespace Platform.Api.Tests.Health;

/// <summary>
/// US-102: the dependency probes behind GET /health/ready. Every test talks only to a fake server on
/// 127.0.0.1 started by the test, or to a closed loopback port. No real services, no other hosts.
///
/// Contract: <c>new TcpPostgresProbe(string host, int port, TimeSpan timeout)</c> (Name "postgres") sends the
/// PostgreSQL SSLRequest and succeeds on a one-byte 'S' or 'N' reply;
/// <c>new HttpDependencyProbe(string name, HttpClient client, Uri endpoint, TimeSpan timeout)</c> sends GET to the
/// endpoint and succeeds on any HTTP response. Both return false (never throw) when the dependency is unreachable,
/// and give up after the timeout.
/// </summary>
public sealed class DependencyProbeTests
{
    private const int SslRequestCode = 80877103;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TimeoutMargin = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData('S')]
    [InlineData('N')]
    public async Task PostgresProbe_SucceedsWhenServerAnswersSslRequest(char reply)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartLoopbackListener(out var port);
        var server = Task.Run(
            async () =>
            {
                using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
                var stream = connection.GetStream();
                var request = new byte[8];
                await stream.ReadExactlyAsync(request, cancellationToken);
                await stream.WriteAsync(new[] { (byte)reply }, cancellationToken);
                return request;
            },
            cancellationToken);
        var probe = new TcpPostgresProbe("127.0.0.1", port, ProbeTimeout);

        var reachable = await probe.CheckAsync(cancellationToken);

        Assert.True(reachable);
        Assert.Equal("postgres", probe.Name);
        var request = await server;
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(request.AsSpan(0, 4)));
        Assert.Equal(SslRequestCode, BinaryPrimitives.ReadInt32BigEndian(request.AsSpan(4, 4)));
    }

    [Fact]
    public async Task PostgresProbe_FailsWhenServerRepliesWithSomethingElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartLoopbackListener(out var port);
        var server = Task.Run(
            async () =>
            {
                using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
                var stream = connection.GetStream();
                await stream.ReadExactlyAsync(new byte[8], cancellationToken);
                await stream.WriteAsync("X"u8.ToArray(), cancellationToken);
            },
            cancellationToken);
        var probe = new TcpPostgresProbe("127.0.0.1", port, ProbeTimeout);

        var reachable = await probe.CheckAsync(cancellationToken);

        Assert.False(reachable);
        await server;
    }

    [Fact]
    public async Task PostgresProbe_FailsWithinTimeoutOnClosedPort()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var port = ClosedLoopbackPort();
        var probe = new TcpPostgresProbe("127.0.0.1", port, ProbeTimeout);

        var stopwatch = Stopwatch.StartNew();
        var reachable = await probe.CheckAsync(cancellationToken);
        stopwatch.Stop();

        Assert.False(reachable);
        Assert.True(
            stopwatch.Elapsed < ProbeTimeout + TimeoutMargin,
            $"The probe took {stopwatch.Elapsed.TotalSeconds:F1} s; it must give up after its {ProbeTimeout.TotalSeconds:F0} s timeout.");
    }

    [Fact]
    public async Task PostgresProbe_FailsWithinTimeoutWhenServerNeverAnswers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartLoopbackListener(out var port);
        using var release = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var server = HoldConnectionOpen(listener, release.Token);
        var probe = new TcpPostgresProbe("127.0.0.1", port, ProbeTimeout);

        var stopwatch = Stopwatch.StartNew();
        var reachable = await probe.CheckAsync(cancellationToken);
        stopwatch.Stop();
        await release.CancelAsync();
        await server;

        Assert.False(reachable);
        Assert.True(
            stopwatch.Elapsed < ProbeTimeout + TimeoutMargin,
            $"The probe took {stopwatch.Elapsed.TotalSeconds:F1} s; it must give up after its {ProbeTimeout.TotalSeconds:F0} s timeout.");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task HttpProbe_SucceedsOnAnyHttpResponse(int statusCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartLoopbackListener(out var port);
        var server = AnswerOneHttpRequest(listener, statusCode, cancellationToken);
        using var client = LoopbackHttpClient();
        var probe = new HttpDependencyProbe("ollama", client, new Uri($"http://127.0.0.1:{port}/api/tags"), ProbeTimeout);

        var reachable = await probe.CheckAsync(cancellationToken);

        Assert.True(reachable);
        Assert.Equal("ollama", probe.Name);
        var requestLine = await server;
        Assert.StartsWith("GET /api/tags ", requestLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpProbe_FailsOnRefusedConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var port = ClosedLoopbackPort();
        using var client = LoopbackHttpClient();
        var probe = new HttpDependencyProbe("blob", client, new Uri($"http://127.0.0.1:{port}/"), ProbeTimeout);

        var stopwatch = Stopwatch.StartNew();
        var reachable = await probe.CheckAsync(cancellationToken);
        stopwatch.Stop();

        Assert.False(reachable);
        Assert.True(
            stopwatch.Elapsed < ProbeTimeout + TimeoutMargin,
            $"The probe took {stopwatch.Elapsed.TotalSeconds:F1} s; it must give up after its {ProbeTimeout.TotalSeconds:F0} s timeout.");
    }

    [Fact]
    public async Task HttpProbe_FailsWithinTimeoutWhenServerNeverAnswers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartLoopbackListener(out var port);
        using var release = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var server = HoldConnectionOpen(listener, release.Token);
        using var client = LoopbackHttpClient();
        var probe = new HttpDependencyProbe("servicebus", client, new Uri($"http://127.0.0.1:{port}/health"), ProbeTimeout);

        var stopwatch = Stopwatch.StartNew();
        var reachable = await probe.CheckAsync(cancellationToken);
        stopwatch.Stop();
        await release.CancelAsync();
        await server;

        Assert.False(reachable);
        Assert.True(
            stopwatch.Elapsed < ProbeTimeout + TimeoutMargin,
            $"The probe took {stopwatch.Elapsed.TotalSeconds:F1} s; it must give up after its {ProbeTimeout.TotalSeconds:F0} s timeout.");
    }

    private static TcpListener StartLoopbackListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    /// <summary>A loopback port that was free a moment ago and has no listener.</summary>
    private static int ClosedLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpClient LoopbackHttpClient() => new(new SocketsHttpHandler { UseProxy = false });

    /// <summary>Accepts one connection and keeps it open without answering until released.</summary>
    private static Task HoldConnectionOpen(TcpListener listener, CancellationToken release) =>
        Task.Run(async () =>
        {
            try
            {
                using var connection = await listener.AcceptTcpClientAsync(release);
                await Task.Delay(Timeout.Infinite, release);
            }
            catch (OperationCanceledException)
            {
                // Released by the test.
            }
        });

    /// <summary>Accepts one HTTP request, answers with the given status and returns the request line.</summary>
    private static Task<string> AnswerOneHttpRequest(TcpListener listener, int statusCode, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
                var stream = connection.GetStream();
                var received = new StringBuilder();
                var buffer = new byte[1024];
                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var response = $"HTTP/1.1 {statusCode} Marker\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
                return received.ToString().Split("\r\n")[0];
            },
            cancellationToken);
}
