using System;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;

namespace JellyfinProxy.Mod
{
    public static class HandlerInterceptor
    {
        private static Delegate _originalFactory;
        private static FieldInfo _factoryField;
        private static object _httpClientManager;
        private static int _applyCount;

        private static bool _ipv4Enabled;
        private static string[] _ipv4Domains;
        private static bool _tmdbRewrite;
        private static string _tmdbApiReplacement;
        private static string _tmdbImageReplacement;

        public static void Apply()
        {
            _applyCount++;
            if (_applyCount > 1) return;

            try
            {
                _httpClientManager = Plugin.Instance.ApplicationHost.Resolve<IHttpClient>();
                
                if (_httpClientManager == null)
                {
                    Plugin.Instance.Logger.LogWarning("HandlerInterceptor: Cannot resolve IHttpClient");
                    return;
                }

                var type = _httpClientManager.GetType();

                _factoryField = type.GetField("_httpClientHandlerFactory",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (_factoryField == null)
                {
                    Plugin.Instance.Logger.LogWarning("HandlerInterceptor: _httpClientHandlerFactory field not found");
                    return;
                }

                _originalFactory = _factoryField.GetValue(_httpClientManager) as Delegate;
                if (_originalFactory == null)
                {
                    Plugin.Instance.Logger.LogWarning("HandlerInterceptor: _httpClientHandlerFactory is null");
                    return;
                }

                RefreshCache();

                var wrapper = CreateWrapper(_originalFactory);
                _factoryField.SetValue(_httpClientManager, wrapper);
                Plugin.Instance.Logger.LogInformation("HandlerInterceptor - factory wrapped");

                if (Plugin.Instance.DebugMode)
                {
                    if (_ipv4Enabled && _ipv4Domains.Length > 0)
                        Plugin.Instance.Logger.LogInformation("IPv4 only enabled for: {Domains}", string.Join(", ", _ipv4Domains));
                    if (_tmdbRewrite)
                    {
                        if (!string.IsNullOrEmpty(_tmdbApiReplacement))
                            Plugin.Instance.Logger.LogInformation("TMDB API rewrite: → {Url}", _tmdbApiReplacement);
                        if (!string.IsNullOrEmpty(_tmdbImageReplacement))
                            Plugin.Instance.Logger.LogInformation("TMDB Image rewrite: → {Url}", _tmdbImageReplacement);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.LogWarning("HandlerInterceptor failed: {Message}", e.Message);
            }
        }

        public static void RefreshCache()
        {
            var config = Plugin.Instance.GetPluginConfiguration();

            _ipv4Enabled = config.EnableIPv4Only;
            _ipv4Domains = config.IPv4OnlyDomains
                ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToArray() ?? Array.Empty<string>();

            _tmdbRewrite = config.EnableAltTmdb;
            _tmdbApiReplacement = config.AltTmdbApiUrl?.Trim().TrimEnd('/');
            _tmdbImageReplacement = config.AltTmdbImageUrl?.Trim().TrimEnd('/');
        }

        public static void Remove()
        {
            if (_applyCount <= 0) return;
            _applyCount--;
            if (_applyCount > 0) return;

            try
            {
                if (_factoryField != null && _httpClientManager != null && _originalFactory != null)
                    _factoryField.SetValue(_httpClientManager, _originalFactory);
                _applyCount = 0;
                Plugin.Instance.Logger.LogInformation("HandlerInterceptor - factory restored");
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.LogWarning("HandlerInterceptor remove failed: {Message}", e.Message);
            }
        }

        private static Delegate CreateWrapper(Delegate original)
        {
            var delegateType = original.GetType();
            var invokeMethod = delegateType.GetMethod("Invoke");
            var argParam = Expression.Parameter(invokeMethod.GetParameters()[0].ParameterType, "arg");
            var callOriginal = Expression.Call(Expression.Constant(original), invokeMethod, argParam);
            var processMethod = typeof(HandlerInterceptor).GetMethod(nameof(ProcessHandler),
                BindingFlags.NonPublic | BindingFlags.Static);
            var callProcess = Expression.Call(processMethod,
                Expression.Convert(callOriginal, typeof(HttpMessageHandler)));
            var body = Expression.Convert(callProcess, invokeMethod.ReturnType);
            return Expression.Lambda(delegateType, body, argParam).Compile();
        }

        private static HttpMessageHandler ProcessHandler(HttpMessageHandler original)
        {
            var handler = original;
            var debug = Plugin.Instance.DebugMode;

            if (_ipv4Enabled && handler is SocketsHttpHandler sh && _ipv4Domains.Length > 0)
            {
                var domains = _ipv4Domains;

                sh.ConnectCallback = async (context, cancellationToken) =>
                {
                    try
                    {
                        var host = context.DnsEndPoint.Host;
                        foreach (var d in domains)
                        {
                            if (!host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork,
                                cancellationToken).ConfigureAwait(false);
                            if (addresses.Length == 0)
                                throw new InvalidOperationException($"No IPv4 address for {host}");

                            if (debug)
                                Plugin.Instance.Logger.LogInformation("IPv4 forced: {Host} → {Address}", host, addresses[0]);

                            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            await sock.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port),
                                cancellationToken).ConfigureAwait(false);
                            return new NetworkStream(sock, ownsSocket: true);
                        }

                        var defSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        await defSocket.ConnectAsync(host, context.DnsEndPoint.Port, cancellationToken)
                            .ConfigureAwait(false);
                        return new NetworkStream(defSocket, ownsSocket: true);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        var fallback = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        await fallback.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port,
                            cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(fallback, ownsSocket: true);
                    }
                };
            }

            if (_tmdbRewrite)
            {
                handler = new UrlRewritingHandler(handler);
            }

            return handler;
        }

        internal class UrlRewritingHandler : DelegatingHandler
        {
            private static readonly string TmdbApiHost = "api.themoviedb.org";
            private static readonly string TmdbImageHost = "image.tmdb.org";

            public UrlRewritingHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri?.ToString();
                if (url == null)
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                var debug = Plugin.Instance.DebugMode;

                try
                {
                    if (!string.IsNullOrEmpty(_tmdbApiReplacement) && url.Contains(TmdbApiHost))
                    {
                        var newUrl = url.Replace("https://" + TmdbApiHost, _tmdbApiReplacement)
                            .Replace("http://" + TmdbApiHost, _tmdbApiReplacement);
                        if (debug)
                            Plugin.Instance.Logger.LogInformation("TMDB API rewrite: {Old} → {New}", url, newUrl);
                        request.RequestUri = new Uri(newUrl);
                    }

                    if (!string.IsNullOrEmpty(_tmdbImageReplacement) && url.Contains(TmdbImageHost))
                    {
                        var newUrl = url.Replace("https://" + TmdbImageHost, _tmdbImageReplacement)
                            .Replace("http://" + TmdbImageHost, _tmdbImageReplacement);
                        if (debug)
                            Plugin.Instance.Logger.LogInformation("TMDB Image rewrite: {Old} → {New}", url, newUrl);
                        request.RequestUri = new Uri(newUrl);
                    }
                }
                catch (UriFormatException) { }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
