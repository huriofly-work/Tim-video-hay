using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace TimVideoHay.Infrastructure;

// Ephemeral, token-protected loopback bridge for ffprobe's range requests.
// The upstream URI is fixed and every redirect/DNS connection goes through DouyinHttp.
public sealed class MediaRangeBridge : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly string upstream;
    private readonly string referer;
    private readonly string route = "/" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private readonly Task server;
    public string Url { get; }
    public string? LastFailure { get; private set; }
    public MediaRangeBridge(string upstream, string referer, CancellationToken ct, HttpClient? client = null)
    {
        this.upstream = DouyinHttp.Validate(upstream).AbsoluteUri; this.referer = referer;
        http = client ?? DouyinHttp.Create(); ownsHttp = client is null;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        listener.Start(4);
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + route;
        server = ServeAsync();
    }
    private async Task ServeAsync()
    {
        using var slots = new SemaphoreSlim(4, 4);
        var handlers = new List<Task>();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var peer = await listener.AcceptTcpClientAsync(lifetime.Token);
                if (!slots.Wait(0)) { peer.Dispose(); continue; }
                handlers.RemoveAll(task => task.IsCompleted);
                handlers.Add(HandleAsync(peer));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally { await Task.WhenAll(handlers); }
        async Task HandleAsync(TcpClient peer)
        {
            using (peer)
                try { await RespondAsync(peer, lifetime.Token); }
                catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or TimVideoHay.Core.NeedsActionException) { LastFailure = ex.GetType().Name; }
                finally { slots.Release(); }
        }
    }
    private async Task RespondAsync(TcpClient peer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        using var stream = peer.GetStream();
        // Read bounded ASCII headers without StreamReader buffering bytes from a body.
        var header = new List<byte>(); var one = new byte[1];
        while (header.Count < 8192)
        {
            if (await stream.ReadAsync(one, ct) == 0) return;
            header.Add(one[0]);
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        var start = lines[0].Split(' ');
        if (header.Count >= 8192 || start.Length != 3 || start[1] != route || start[0] is not ("GET" or "HEAD"))
        { await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), ct); return; }
        RangeHeaderValue? range = null;
        foreach (var line in lines.Skip(1))
            if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
            {
                if (range is not null || !RangeHeaderValue.TryParse(line[6..].Trim(), out range) || range.Unit != "bytes" || range.Ranges.Count != 1)
                    return;
            }
        using var response = await DouyinHttp.GetAsync(http, upstream, referer, ct, range, head: start[0] == "HEAD");
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
        { await stream.WriteAsync("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), ct); return; }
        var reply = new StringBuilder($"HTTP/1.1 {(int)response.StatusCode} OK\r\nConnection: close\r\nAccept-Ranges: bytes\r\n");
        if (response.Content.Headers.ContentLength is long length) reply.Append($"Content-Length: {length}\r\n");
        if (response.Content.Headers.ContentRange is not null) reply.Append($"Content-Range: {response.Content.Headers.ContentRange}\r\n");
        reply.Append("\r\n"); await stream.WriteAsync(Encoding.ASCII.GetBytes(reply.ToString()), ct);
        if (start[0] == "GET")
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await input.CopyToAsync(stream, ct);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync(); listener.Stop();
        await server; if (ownsHttp) http.Dispose(); lifetime.Dispose();
    }
}
