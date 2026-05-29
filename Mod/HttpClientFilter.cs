using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace JellyfinProxy.Mod
{
    /// <summary>
    /// 通过 IHttpMessageHandlerBuilderFilter 向 Jellyfin 的所有 IHttpClientFactory 客户端
    /// 注入 TMDB URL 改写 DelegatingHandler 和 IPv4 强制 ConnectCallback。
    /// </summary>
    public class HttpClientFilter : IHttpMessageHandlerBuilderFilter
    {
        private HashSet<string> _ipv4Domains = new HashSet<string>();
        private string _apiReplacement;
        private string _imageReplacement;
        private bool _debug;

        public void UpdateConfig(
            bool enableIPv4, string ipv4Domains,
            bool enableTmdb, string tmdbApiUrl, string tmdbImageUrl,
            bool debug)
        {
            _ipv4Domains = enableIPv4 && !string.IsNullOrEmpty(ipv4Domains)
                ? new HashSet<string>(
                    ipv4Domains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => d.Length > 0),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();

            _apiReplacement = enableTmdb && !string.IsNullOrEmpty(tmdbApiUrl)
                ? tmdbApiUrl.Trim().TrimEnd('/') : null;
            _imageReplacement = enableTmdb && !string.IsNullOrEmpty(tmdbImageUrl)
                ? tmdbImageUrl.Trim().TrimEnd('/') : null;
            _debug = debug;
        }

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                // 先执行原生配置（HappyEyeballs ConnectCallback 等）
                next(builder);

                // 注入 TMDB URL 改写 handler
                if (!string.IsNullOrEmpty(_apiReplacement) || !string.IsNullOrEmpty(_imageReplacement))
                {
                    builder.AdditionalHandlers.Insert(0,
                        new TmdbRewritingHandler(_apiReplacement, _imageReplacement, _debug));
                }

                // 包装 ConnectCallback 实现 IPv4 强制
                if (_ipv4Domains.Count > 0 && builder.PrimaryHandler is SocketsHttpHandler sh)
                {
                    var original = sh.ConnectCallback;
                    var domains = _ipv4Domains;
                    var debug = _debug;

                    sh.ConnectCallback = async (context, ct) =>
                    {
                        var host = context.DnsEndPoint.Host;
                        foreach (var d in domains)
                        {
                            if (!host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)
                                .ConfigureAwait(false);
                            if (addresses.Length == 0)
                                throw new InvalidOperationException($"No IPv4 address for {host}");

                            if (debug)
                                Plugin.Log.LogInformation("IPv4 forced: {Host} → {Address}", host, addresses[0]);

                            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            await sock.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), ct)
                                .ConfigureAwait(false);
                            return new NetworkStream(sock, ownsSocket: true);
                        }

                        // 不匹配任何 IPv4 域名，走原生回调
                        if (original != null)
                            return await original(context, ct).ConfigureAwait(false);

                        // Fallback: 默认连接
                        var defSock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        await defSock.ConnectAsync(context.DnsEndPoint, ct).ConfigureAwait(false);
                        return new NetworkStream(defSock, ownsSocket: true);
                    };
                }
            };
        }
    }
}
