using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;

namespace Jellyfin.Plugin.TmdbTuner.Api
{
    /// <summary>
    /// TMDbClient 封装，支持自定义 Host、HTTP 代理。
    /// </summary>
    public class TmdbApiClient : IDisposable
    {
        public const string DefaultApiKey = "4219e299c89411838049ab0dab19ebd5";

        private readonly ILogger _logger;
        private TMDbClient _client;
        private string _currentHost = "api.tmdb.org";
        private string _apiKey;
        private string _proxyUrl;
        private bool _debug;

        public TmdbApiClient(ILogger logger)
        {
            _logger = logger;
        }

        public void Configure(string host, string apiKey, bool enableIPv4, string proxyUrl, bool debug)
        {
            _currentHost = !string.IsNullOrWhiteSpace(host) ? host.Trim().TrimEnd('/') : "api.tmdb.org";
            _apiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : DefaultApiKey;
            _proxyUrl = !string.IsNullOrWhiteSpace(proxyUrl) ? proxyUrl.Trim().TrimEnd('/') : null;
            _debug = debug;

            _client?.Dispose();
            _client = new TMDbClient(_apiKey, true, _currentHost, null,
                _proxyUrl != null ? new WebProxy(_proxyUrl) : null);

            _client.ThrowApiExceptions = false;
            _client.Timeout = TimeSpan.FromSeconds(30);

            if (_debug)
                _logger.LogInformation("TmdbTuner: host={Host}, proxy={Proxy}",
                    _currentHost, _proxyUrl ?? "none");
        }

        public TMDbClient Client
        {
            get
            {
                if (_client == null)
                    Configure(_currentHost, _apiKey, false, _proxyUrl, _debug);
                return _client;
            }
        }

        /// <summary>改写图片 URL：把 image.tmdb.org 替换为自定义镜像。</summary>
        public string RewriteImageUrl(string url, string imageHost)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(imageHost)) return url;
            if (!imageHost.StartsWith("https://") && !imageHost.StartsWith("http://")) return url;
            var host = imageHost.Trim().TrimEnd('/');
            return url.Replace("https://image.tmdb.org", host)
                      .Replace("http://image.tmdb.org", host);
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
