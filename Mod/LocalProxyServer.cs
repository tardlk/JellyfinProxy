using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinProxy.Mod
{
    /// <summary>
    /// 内置本地 HTTP/HTTPS 转发代理，实现 TMDB URL 改写和 IPv4 强制。
    /// 因为 Jellyfin 的 TMDb 提供者使用 TMDbLib（不走 IHttpClientFactory），只能用代理拦截。
    /// </summary>
    public class LocalProxyServer : IDisposable
    {
        private readonly int _port;
        private HttpListener _listener;
        private CancellationTokenSource _cts;

        private HashSet<string> _ipv4Domains = new HashSet<string>();
        private string _apiReplacement;
        private string _imageReplacement;
        private string _externalProxyUrl;
        private HashSet<string> _proxyDomains = new HashSet<string>();

        private static readonly string TmdbApiHost = "api.themoviedb.org";
        private static readonly string TmdbImageHost = "image.tmdb.org";

        public bool IsRunning => _listener?.IsListening ?? false;
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
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            _ = ListenLoop(_cts.Token);
            Plugin.Log.LogInformation("Local proxy started on 127.0.0.1:{Port}", _port);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener?.Close();
            _listener = null;
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = HandleAsync(ctx, ct);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            try
            {
                if (ctx.Request.HttpMethod == "CONNECT")
                    await HandleConnectAsync(ctx, ct).ConfigureAwait(false);
                else
                    await HandleHttpAsync(ctx, ct).ConfigureAwait(false);
            }
            catch { try { ctx.Response.StatusCode = 502; ctx.Response.Close(); } catch { } }
        }

        private async Task HandleHttpAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            var targetUrl = RewriteUrl(req.Url);
            var host = new Uri(targetUrl).Host;

            using var fwd = new HttpRequestMessage(new HttpMethod(req.HttpMethod), targetUrl);
            foreach (string key in req.Headers)
            {
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)) continue;
                try { fwd.Headers.TryAddWithoutValidation(key, req.Headers.GetValues(key)); } catch { }
            }

            if (req.HasEntityBody)
            {
                using var ms = new MemoryStream();
                await req.InputStream.CopyToAsync(ms, ct).ConfigureAwait(false);
                fwd.Content = new ByteArrayContent(ms.ToArray());
                if (req.ContentType != null)
                    fwd.Content.Headers.TryAddWithoutValidation("Content-Type", req.ContentType);
            }

            using var handler = CreateHandler(host);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var fwdResp = await client.SendAsync(fwd, ct).ConfigureAwait(false);

            resp.StatusCode = (int)fwdResp.StatusCode;
            if (fwdResp.Content?.Headers.ContentType != null)
                resp.ContentType = fwdResp.Content.Headers.ContentType.ToString();
            foreach (var h in fwdResp.Headers)
                try { resp.Headers[h.Key] = string.Join(",", h.Value); } catch { }

            if (fwdResp.Content != null)
            {
                var body = await fwdResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                resp.ContentLength64 = body.Length;
                await resp.OutputStream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
            }
            resp.Close();
        }

        private async Task HandleConnectAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var parts = ctx.Request.Url.Host.Split(':');
            var host = RewriteHost(parts[0]);
            var port = parts.Length > 1 ? int.Parse(parts[1]) : 443;
            var useIPv4 = IsIPv4Domain(host);

            if (Plugin.DebugMode && useIPv4)
                Plugin.Log.LogInformation("LocalProxy CONNECT IPv4: {Host}", host);

            Socket sock;
            if (useIPv4)
            {
                var addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await sock.ConnectAsync(new IPEndPoint(addrs[0], port), ct).ConfigureAwait(false);
            }
            else
            {
                sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await sock.ConnectAsync(host, port, ct).ConfigureAwait(false);
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.StatusDescription = "Connection Established";
            ctx.Response.Close();

            var client = ctx.Request.InputStream;
            var target = new NetworkStream(sock, ownsSocket: true);
            var t1 = client.CopyToAsync(target, ct);
            var t2 = target.CopyToAsync(client, ct);
            await Task.WhenAny(t1, t2).ConfigureAwait(false);
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
