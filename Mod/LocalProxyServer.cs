using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinProxy.Mod
{
    /// <summary>
    /// 内置本地 HTTP/HTTPS 转发代理，实现 TMDB URL 改写和 IPv4 强制。
    /// 使用 TcpListener 而非 HttpListener，确保 HTTPS CONNECT 隧道正确工作。
    /// </summary>
    public class LocalProxyServer : IDisposable
    {
        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        private HashSet<string> _ipv4Domains = new HashSet<string>();
        private string _apiReplacement;
        private string _imageReplacement;
        private string _externalProxyUrl;
        private HashSet<string> _proxyDomains = new HashSet<string>();

        private static readonly string TmdbApiHost = "api.themoviedb.org";
        private static readonly string TmdbImageHost = "image.tmdb.org";

        public bool IsRunning => _listener != null;
        public int Port => _port;

        public LocalProxyServer(int port = 57891) => _port = port;

        public void Configure(
            bool enableProxy, string proxyUrl, string proxyDomains,
            bool enableIPv4, string ipv4Domains,
            bool enableTmdb, string tmdbApiUrl, string tmdbImageUrl)
        {
            _externalProxyUrl = enableProxy && !string.IsNullOrEmpty(proxyUrl)
                ? proxyUrl.Trim().TrimEnd('/') : null;
            _proxyDomains = enableProxy && !string.IsNullOrEmpty(proxyDomains)
                ? new HashSet<string>(proxyDomains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim()).Where(d => d.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();

            _ipv4Domains = enableIPv4 && !string.IsNullOrEmpty(ipv4Domains)
                ? new HashSet<string>(ipv4Domains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim()).Where(d => d.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();

            _apiReplacement = enableTmdb && !string.IsNullOrEmpty(tmdbApiUrl)
                ? tmdbApiUrl.Trim().TrimEnd('/') : null;
            _imageReplacement = enableTmdb && !string.IsNullOrEmpty(tmdbImageUrl)
                ? tmdbImageUrl.Trim().TrimEnd('/') : null;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _ = AcceptLoop(_cts.Token);
            Plugin.Log.LogInformation("Local proxy started on 127.0.0.1:{Port}", _port);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    _ = HandleClientAsync(client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                    var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(requestLine)) return;

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 3) return;

                    var method = parts[0];
                    var target = parts[1];

                    // Read headers (just consume them)
                    string header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync().ConfigureAwait(false)))
                    {
                        if (header.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    if (method == "CONNECT")
                    {
                        await HandleConnect(stream, target, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await HandleHttp(stream, method, target, ct).ConfigureAwait(false);
                    }
                }
            }
            catch { }
        }

        private async Task HandleConnect(NetworkStream clientStream, string target, CancellationToken ct)
        {
            var hostPort = target.Split(':');
            var host = RewriteHost(hostPort[0]);
            var port = hostPort.Length > 1 ? int.Parse(hostPort[1]) : 443;
            var useIPv4 = IsIPv4Domain(host);

            if (Plugin.DebugMode)
            {
                if (host != hostPort[0])
                    Plugin.Log.LogInformation("CONNECT rewrite: {Old} → {New}", hostPort[0], host);
                if (useIPv4)
                    Plugin.Log.LogInformation("CONNECT IPv4: {Host}:{Port}", host, port);
            }

            Socket targetSocket;
            if (useIPv4)
            {
                var addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                if (addrs.Length == 0) throw new InvalidOperationException($"No IPv4 for {host}");
                targetSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await targetSocket.ConnectAsync(new IPEndPoint(addrs[0], port), ct).ConfigureAwait(false);
            }
            else
            {
                targetSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await targetSocket.ConnectAsync(host, port, ct).ConfigureAwait(false);
            }

            // Send 200 to client
            var resp = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await clientStream.WriteAsync(resp, 0, resp.Length, ct).ConfigureAwait(false);
            await clientStream.FlushAsync(ct).ConfigureAwait(false);

            // Bidirectional tunnel
            using (targetSocket)
            using (var targetStream = new NetworkStream(targetSocket, ownsSocket: true))
            {
                var t1 = clientStream.CopyToAsync(targetStream, ct);
                var t2 = targetStream.CopyToAsync(clientStream, ct);
                await Task.WhenAny(t1, t2).ConfigureAwait(false);
            }
        }

        private async Task HandleHttp(NetworkStream clientStream, string method, string targetUrl, CancellationToken ct)
        {
            var uri = new Uri(targetUrl);
            var rewrittenUrl = RewriteUrl(uri);
            var host = new Uri(rewrittenUrl).Host;

            using var fwd = new HttpRequestMessage(new HttpMethod(method), rewrittenUrl);

            using var handler = CreateHandler(host);
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            HttpResponseMessage fwdResp;
            try
            {
                fwdResp = await httpClient.SendAsync(fwd, ct).ConfigureAwait(false);
            }
            catch
            {
                var err = Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\n\r\n");
                await clientStream.WriteAsync(err, 0, err.Length, ct).ConfigureAwait(false);
                return;
            }

            var body = await fwdResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var sb = new StringBuilder();
            sb.AppendLine($"HTTP/1.1 {(int)fwdResp.StatusCode} {fwdResp.ReasonPhrase}");
            foreach (var h in fwdResp.Headers)
                sb.AppendLine($"{h.Key}: {string.Join(",", h.Value)}");
            foreach (var h in fwdResp.Content.Headers)
                sb.AppendLine($"{h.Key}: {string.Join(",", h.Value)}");
            sb.AppendLine($"Content-Length: {body.Length}");
            sb.AppendLine();

            var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await clientStream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);
            await clientStream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
            await clientStream.FlushAsync(ct).ConfigureAwait(false);

            fwdResp.Dispose();
        }

        private SocketsHttpHandler CreateHandler(string host)
        {
            var h = new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None };

            if (IsIPv4Domain(host))
                h.ConnectCallback = async (ctx, ct) =>
                {
                    var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                    if (Plugin.DebugMode)
                        Plugin.Log.LogInformation("IPv4 forced: {Host} → {Addr}", ctx.DnsEndPoint.Host, addrs[0]);
                    var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await s.ConnectAsync(new IPEndPoint(addrs[0], ctx.DnsEndPoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(s, ownsSocket: true);
                };

            if (IsProxyDomain(host))
            {
                h.Proxy = new WebProxy(_externalProxyUrl);
                if (Plugin.DebugMode)
                    Plugin.Log.LogInformation("Proxy: {Host} → {Proxy}", host, _externalProxyUrl);
            }

            return h;
        }

        private string RewriteUrl(Uri url)
        {
            var s = url.ToString();
            if (_apiReplacement != null && s.Contains(TmdbApiHost))
                return s.Replace("https://" + TmdbApiHost, _apiReplacement).Replace("http://" + TmdbApiHost, _apiReplacement);
            if (_imageReplacement != null && s.Contains(TmdbImageHost))
                return s.Replace("https://" + TmdbImageHost, _imageReplacement).Replace("http://" + TmdbImageHost, _imageReplacement);
            return s;
        }

        private string RewriteHost(string host)
        {
            if (_apiReplacement != null && host.Equals(TmdbApiHost, StringComparison.OrdinalIgnoreCase))
                return new Uri(_apiReplacement).Host;
            if (_imageReplacement != null && host.Equals(TmdbImageHost, StringComparison.OrdinalIgnoreCase))
                return new Uri(_imageReplacement).Host;
            return host;
        }

        private bool IsIPv4Domain(string host)
        {
            foreach (var d in _ipv4Domains)
                if (host.EndsWith(d, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool IsProxyDomain(string host)
        {
            if (_externalProxyUrl == null) return false;
            foreach (var d in _proxyDomains)
                if (host.EndsWith(d, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void Dispose() => Stop();
    }
}
