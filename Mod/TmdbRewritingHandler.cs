using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinProxy.Mod
{
    /// <summary>
    /// DelegatingHandler：拦截 TMDB API 和图片请求，改写 URL 到自定义镜像站。
    /// </summary>
    internal class TmdbRewritingHandler : DelegatingHandler
    {
        private static readonly string TmdbApiHost = "api.themoviedb.org";
        private static readonly string TmdbImageHost = "image.tmdb.org";

        private readonly string _apiReplacement;
        private readonly string _imageReplacement;
        private readonly bool _debug;

        public TmdbRewritingHandler(string apiReplacement, string imageReplacement, bool debug)
        {
            _apiReplacement = apiReplacement?.Trim().TrimEnd('/');
            _imageReplacement = imageReplacement?.Trim().TrimEnd('/');
            _debug = debug;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString();
            if (url != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_apiReplacement) && url.Contains(TmdbApiHost))
                    {
                        var newUrl = url.Replace("https://" + TmdbApiHost, _apiReplacement)
                                         .Replace("http://" + TmdbApiHost, _apiReplacement);
                        if (_debug)
                            Plugin.Log.LogInformation("TMDB API rewrite: {Old} → {New}", url, newUrl);
                        request.RequestUri = new Uri(newUrl);
                    }

                    if (!string.IsNullOrEmpty(_imageReplacement) && url.Contains(TmdbImageHost))
                    {
                        var newUrl = url.Replace("https://" + TmdbImageHost, _imageReplacement)
                                         .Replace("http://" + TmdbImageHost, _imageReplacement);
                        if (_debug)
                            Plugin.Log.LogInformation("TMDB Image rewrite: {Old} → {New}", url, newUrl);
                        request.RequestUri = new Uri(newUrl);
                    }
                }
                catch (UriFormatException) { }
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
