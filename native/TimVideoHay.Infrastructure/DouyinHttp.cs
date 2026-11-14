using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// All media traffic stays in this client. Native tools only receive local files
// or the narrow loopback range bridge; they cannot follow remote redirects.
public static class DouyinHttp
{
    private static readonly string[] Domains = ["douyin.com", "iesdouyin.com", "douyinvod.com", "douyinpic.com", "byteimg.com", "bytedance.com", "bytecdn.cn", "zjcdn.com", "snssdk.com"];
    public const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0 || uri.HostNameType != UriHostNameType.Dns
            || !(uri.IdnHost.Equals("api5-normal-c-hl.amemv.com", StringComparison.OrdinalIgnoreCase) || Domains.Any(d => uri.IdnHost.Equals(d, StringComparison.OrdinalIgnoreCase) || uri.IdnHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))))
            throw new NeedsActionException("Nguồn trả địa chỉ tải ngoài miền Douyin/CDN được phép. App đã chặn địa chỉ này.");
        return uri;
    }
    public static bool Allowed(string value) { try { _ = Validate(value); return true; } catch (NeedsActionException) { return false; } }
    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] is not (0 or 10 or 127) && b[0] < 224
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2))
                && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100))
                && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Only global unicast; reject local, multicast, transition and documentation ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20
            && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] <= 1 || b[2] == 0x0d && b[3] == 0xb8))
            && !(b[0] == 0x20 && b[1] == 0x02);
    }
    public static SocketsHttpHandler Handler(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null) => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = async (context, ct) =>
        {
            _ = Validate("https://" + context.DnsEndPoint.Host + "/");
            if (context.DnsEndPoint.Port != 443) throw new HttpRequestException("Blocked port");
            var addresses = await (resolve is null ? Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct) : resolve(context.DnsEndPoint.Host, ct));
            if (addresses.Length == 0 || addresses.Any(a => !PublicAddress(a))) throw new HttpRequestException("Blocked non-public network address");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(addresses, 443, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };
    public static HttpClient Create() => new(Handler()) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<HttpResponseMessage> GetAsync(HttpClient http, string value, string? referer, CancellationToken ct,
        RangeHeaderValue? range = null, string? ifRange = null, bool head = false, bool closeConnection = false)
    {
        var uri = Validate(value);
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(Agent);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (referer is not null) request.Headers.Referrer = Rules.DouyinUri(referer);
            request.Headers.Range = range;
            if (closeConnection) request.Headers.ConnectionClose = true;
            if (ifRange is not null) request.Headers.TryAddWithoutValidation("If-Range", ifRange);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) throw new HttpRequestException("Missing redirect");
                uri = Validate(new Uri(uri, location).AbsoluteUri); continue;
            }
            return response;
        }
        throw new HttpRequestException("Too many redirects");
    }
}
