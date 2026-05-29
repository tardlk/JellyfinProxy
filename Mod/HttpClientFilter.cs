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
    /// 支持热更新：属性由 Plugin 实时刷新，handler 每次都读取最新值。
    /// </summary>
    public class HttpClientFilter : IHttpMessageHandlerBuilderFilter
    {
        private HashSet<string> _ipv4Domains = new HashSet<string>();

        /// <summary>TMDB API 替换地址（热更新由 Plugin 写入）</summary>
        public string ApiReplacement { get; set; }

        /// <summary>TMDB 图片替换地址（热更新由 Plugin 写入）</summary>
        public string ImageReplacement { get; set; }

        /// <summary>调试模式</summary>
        public bool DebugMode { get; set; }

        /// <summary>更新 IPv4 域名和 TMDB 设置</summary>
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

            ApiReplacement = enableTmdb && !string.IsNullOrEmpty(tmdbApiUrl)
                ? tmdbApiUrl.Trim().TrimEnd('/') : null;
            ImageReplacement = enableTmdb && !string.IsNullOrEmpty(tmdbImageUrl)
                ? tmdbImageUrl.Trim().TrimEnd('/') : null;
            DebugMode = debug;
        }

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                // 先执行原生配置（HappyEyeballs ConnectCallback 等）
                next(builder);

                // 注入 TMDB URL 改写 handler（动态读取配置）
                builder.AdditionalHandlers.Insert(0, new TmdbRewritingHandler(this));

                // 包装 ConnectCallback 实现 IPv4 强制（动态读取配置）
                if (builder.PrimaryHandler is SocketsHttpHandler sh)
                {
                    var original = sh.ConnectCallback;

                    sh.ConnectCallback = async (context, ct) =>
                    {
                        var host = context.DnsEndPoint.Host;
                        var domains = _ipv4Domains; // 每次请求读最新值

                        foreach (var d in domains)
                        {
                            if (!host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)
                                .ConfigureAwait(false);
                            if (addresses.Length == 0)
                                throw new InvalidOperationException($"No IPv4 address for {host}");

                            if (DebugMode)
                                Plugin.Log.LogInformation("IPv4 forced: {Host} → {Address}", host, addresses[0]);

                            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            await sock.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), ct)
                                .ConfigureAwait(false);
                            return new NetworkStream(sock, ownsSocket: true);
                        }

                        if (original != null)
                            return await original(context, ct).ConfigureAwait(false);

                        var defSock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        await defSock.ConnectAsync(context.DnsEndPoint, ct).ConfigureAwait(false);
                        return new NetworkStream(defSock, ownsSocket: true);
                    };
                }
            };
        }
    }
}
