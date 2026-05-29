using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinProxy.Mod
{
    /// <summary>
    /// DelegatingHandler：拦截 TMDB API 和图片请求，改写 URL 到自定义镜像站。
    /// 配置从 HttpClientFilter 动态读取，支持热更新。
    /// </summary>
    internal class TmdbRewritingHandler : DelegatingHandler
    {
        private static readonly string TmdbApiHost = "api.themoviedb.org";
        private static readonly string TmdbImageHost = "image.tmdb.org";

        private readonly HttpClientFilter _filter;

        public TmdbRewritingHandler(HttpClientFilter filter)
        {
            _filter = filter;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString();
            if (url == null)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var api = _filter.ApiReplacement;
            var img = _filter.ImageReplacement;
            var debug = _filter.DebugMode;

            try
            {
                if (!string.IsNullOrEmpty(api) && url.Contains(TmdbApiHost))
                {
                    var newUrl = url.Replace("https://" + TmdbApiHost, api)
                                     .Replace("http://" + TmdbApiHost, api);
                    if (debug)
                        Plugin.Log.LogInformation("TMDB API rewrite: {Old} → {New}", url, newUrl);
                    request.RequestUri = new Uri(newUrl);
                }

                if (!string.IsNullOrEmpty(img) && url.Contains(TmdbImageHost))
                {
                    var newUrl = url.Replace("https://" + TmdbImageHost, img)
                                     .Replace("http://" + TmdbImageHost, img);
                    if (debug)
                        Plugin.Log.LogInformation("TMDB Image rewrite: {Old} → {New}", url, newUrl);
                    request.RequestUri = new Uri(newUrl);
                }
            }
            catch (UriFormatException) { }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
