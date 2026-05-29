using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinProxy.Mod
{
    /// <summary>
    /// 内置本地 HTTP/HTTPS 转发代理，实现 TMDB URL 改写和 IPv4 强制功能。
    /// </summary>
    public class LocalProxyServer : IDisposable
    {
        private readonly int _port;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _listenTask;

        private HashSet<string> _ipv4Domains;
        private HashSet<string> _proxyDomains;
        private string _proxyUrl;
        private string _tmdbApiSourceHost = "api.themoviedb.org";
        private string _tmdbApiTargetUrl;
        private string _tmdbImageSourceHost = "image.tmdb.org";
        private string _tmdbImageTargetUrl;

        private static readonly HttpClient _httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None
            })
        { Timeout = TimeSpan.FromSeconds(30) };

        public bool IsRunning => _listener?.IsListening ?? false;
        public int Port => _port;

        public LocalProxyServer(int port = 57891)
        {
            _port = port;
        }

        public void Configure(
            bool enableProxy, string proxyUrl, string proxyDomains,
            bool enableIPv4, string ipv4Domains,
            bool enableTmdbRewrite, string tmdbApiUrl, string tmdbImageUrl)
        {
            _proxyUrl = enableProxy && !string.IsNullOrEmpty(proxyUrl)
                ? proxyUrl.Trim().TrimEnd('/') : null;
            _proxyDomains = enableProxy && !string.IsNullOrEmpty(proxyDomains)
                ? new HashSet<string>(
                    proxyDomains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => d.Length > 0),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();

            _ipv4Domains = enableIPv4 && !string.IsNullOrEmpty(ipv4Domains)
                ? new HashSet<string>(
                    ipv4Domains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => d.Length > 0),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();

            _tmdbApiTargetUrl = enableTmdbRewrite && !string.IsNullOrEmpty(tmdbApiUrl)
                ? tmdbApiUrl.Trim().TrimEnd('/') : null;
            _tmdbImageTargetUrl = enableTmdbRewrite && !string.IsNullOrEmpty(tmdbImageUrl)
                ? tmdbImageUrl.Trim().TrimEnd('/') : null;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            _listenTask = Task.Run(() => ListenLoop(_cts.Token));

            Plugin.Log.LogInformation("Local proxy started on 127.0.0.1:{Port}", _port);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener?.Close();
            _listener = null;
            Plugin.Log.LogInformation("Local proxy stopped");
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = HandleRequestAsync(context, ct);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                if (request.HttpMethod == "CONNECT")
                    await HandleConnectAsync(context, ct).ConfigureAwait(false);
                else
                    await HandleHttpAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (Plugin.Instance.DebugMode)
                    Plugin.Log.LogWarning("LocalProxy error: {Message}", ex.Message);
                try { context.Response.StatusCode = 502; context.Response.Close(); } catch { }
            }
        }

        /// <summary>处理 HTTP 请求：改写 URL 后转发</summary>
        private async Task HandleHttpAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            var targetUrl = request.Url;
            var rewrittenUrl = RewriteUrl(targetUrl);

            using var forwardRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), rewrittenUrl);

            // Copy headers
            foreach (string key in request.Headers)
            {
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)) continue;
                try { forwardRequest.Headers.TryAddWithoutValidation(key, request.Headers.GetValues(key)); } catch { }
            }

            // Copy body
            if (request.HasEntityBody)
            {
                using var ms = new MemoryStream();
                await request.InputStream.CopyToAsync(ms, ct).ConfigureAwait(false);
                ms.Position = 0;
                forwardRequest.Content = new ByteArrayContent(ms.ToArray());
                if (request.ContentType != null)
                    forwardRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
            }

            // Use IPv4 if target host matches
            var targetHost = new Uri(rewrittenUrl).Host;
            var useIPv4 = IsIPv4Domain(targetHost);
            var useProxy = IsProxyDomain(targetHost);

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None
            };

            if (useIPv4)
            {
                handler.ConnectCallback = async (ctx, cancelToken) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, AddressFamily.InterNetwork, cancelToken).ConfigureAwait(false);
                    if (addresses.Length == 0) throw new InvalidOperationException($"No IPv4 for {ctx.DnsEndPoint.Host}");
                    var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await sock.ConnectAsync(new IPEndPoint(addresses[0], ctx.DnsEndPoint.Port), cancelToken).ConfigureAwait(false);
                    return new NetworkStream(sock, ownsSocket: true);
                };
            }

            if (useProxy)
            {
                handler.Proxy = new WebProxy(_proxyUrl);
                if (Plugin.Instance.DebugMode)
                    Plugin.Log.LogInformation("Proxy: {Host} → {Proxy}", targetHost, _proxyUrl);
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var fwdResp = await client.SendAsync(forwardRequest, ct).ConfigureAwait(false);

            response.StatusCode = (int)fwdResp.StatusCode;
            response.ContentType = fwdResp.Content.Headers.ContentType?.ToString();

            foreach (var h in fwdResp.Headers)
                try { response.Headers[h.Key] = string.Join(",", h.Value); } catch { }

            if (fwdResp.Content != null)
            {
                var respBody = await fwdResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                response.ContentLength64 = respBody.Length;
                await response.OutputStream.WriteAsync(respBody, 0, respBody.Length, ct).ConfigureAwait(false);
            }

            response.Close();
        }

        /// <summary>处理 HTTPS CONNECT 隧道：可选 IPv4 强制和主机改写</summary>
        private async Task HandleConnectAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            var hostPort = request.Url.Host.Split(':');
            var targetHost = hostPort[0];
            var targetPort = hostPort.Length > 1 ? int.Parse(hostPort[1]) : 443;

            // Rewrite host if TMDB
            var connectHost = RewriteHost(targetHost);
            var useIPv4 = IsIPv4Domain(targetHost);

            if (Plugin.Instance.DebugMode)
            {
                if (connectHost != targetHost)
                    Plugin.Log.LogInformation("LocalProxy CONNECT rewrite: {Old} → {New}", targetHost, connectHost);
                if (useIPv4)
                    Plugin.Log.LogInformation("LocalProxy CONNECT IPv4: {Host}", connectHost);
            }

            // Resolve and connect
            Socket targetSocket;
            if (useIPv4)
            {
                var addresses = await Dns.GetHostAddressesAsync(connectHost, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                if (addresses.Length == 0) throw new InvalidOperationException($"No IPv4 for {connectHost}");
                targetSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await targetSocket.ConnectAsync(new IPEndPoint(addresses[0], targetPort), ct).ConfigureAwait(false);
            }
            else
            {
                targetSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await targetSocket.ConnectAsync(connectHost, targetPort, ct).ConfigureAwait(false);
            }

            // Send 200 to client
            response.StatusCode = 200;
            response.StatusDescription = "Connection Established";
            response.Close();

            // Tunnel
            var clientStream = request.InputStream;
            var targetStream = new NetworkStream(targetSocket, ownsSocket: true);

            var t1 = clientStream.CopyToAsync(targetStream, ct);
            var t2 = targetStream.CopyToAsync(clientStream, ct);
            await Task.WhenAny(t1, t2).ConfigureAwait(false);
        }

        private string RewriteUrl(Uri url)
        {
            var urlStr = url.ToString();
            if (_tmdbApiTargetUrl != null && urlStr.Contains(_tmdbApiSourceHost))
                return urlStr.Replace("https://" + _tmdbApiSourceHost, _tmdbApiTargetUrl)
                             .Replace("http://" + _tmdbApiSourceHost, _tmdbApiTargetUrl);
            if (_tmdbImageTargetUrl != null && urlStr.Contains(_tmdbImageSourceHost))
                return urlStr.Replace("https://" + _tmdbImageSourceHost, _tmdbImageTargetUrl)
                             .Replace("http://" + _tmdbImageSourceHost, _tmdbImageTargetUrl);
            return urlStr;
        }

        private string RewriteHost(string host)
        {
            if (_tmdbApiTargetUrl != null && host.Equals(_tmdbApiSourceHost, StringComparison.OrdinalIgnoreCase))
                return new Uri(_tmdbApiTargetUrl).Host;
            if (_tmdbImageTargetUrl != null && host.Equals(_tmdbImageSourceHost, StringComparison.OrdinalIgnoreCase))
                return new Uri(_tmdbImageTargetUrl).Host;
            return host;
        }

        private bool IsIPv4Domain(string host)
        {
            foreach (var d in _ipv4Domains)
                if (host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private bool IsProxyDomain(string host)
        {
            if (_proxyUrl == null || _proxyDomains.Count == 0) return false;
            foreach (var d in _proxyDomains)
                if (host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private HttpClient GetForwardClient(string targetHost)
        {
            if (!IsProxyDomain(targetHost)) return null;

            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                Proxy = new WebProxy(_proxyUrl)
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
