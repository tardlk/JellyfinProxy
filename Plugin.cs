using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using JellyfinProxy.Common;
using JellyfinProxy.Mod;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;

namespace JellyfinProxy
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public override string Name => "JellyfinProxy";
        public override Guid Id => Guid.Parse("B5C3E8A1-7D4F-4A2B-9E6C-1F3D8A5B2C7E");
        public override string Description => "JellyfinProxy - HTTP Selective Proxy & TMDB Rewrite & Force IPv4";

        public static ILogger Log { get; private set; }
        public readonly IApplicationHost ApplicationHost;
        private readonly HttpClientFilter _httpFilter;
        private IWebProxy _savedDefaultProxy;

        public Plugin(
            IApplicationHost applicationHost,
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILoggerFactory loggerFactory,
            HttpClientFilter httpFilter)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            ApplicationHost = applicationHost;
            _httpFilter = httpFilter;
            Log = loggerFactory.CreateLogger("JellyfinProxy");

            if (Debugger.IsAttached) Log.LogInformation("Debugger attached");

            Log.LogInformation("Plugin is getting loaded.");

            ApplyConfig(Configuration);

            ConfigurationChanged += (_, _) =>
            {
                ApplyConfig(Configuration);
                Log.LogInformation("Configuration hot-reloaded");
            };
        }

        public PluginConfiguration GetPluginConfiguration() => Configuration;

        public void ApplyConfig(PluginConfiguration config)
        {
            _httpFilter.UpdateConfig(
                config.EnableIPv4Only, config.IPv4OnlyDomains,
                config.EnableAltTmdb, config.AltTmdbApiUrl, config.AltTmdbImageUrl,
                config.EnableDebugMode);

            // 代理：通过 DefaultProxy + SelectiveProxy 白名单路由
            if (config.ProxyEnabled && !string.IsNullOrWhiteSpace(config.ProxyUrl))
            {
                if (CommonUtility.TryParseProxyUrl(config.ProxyUrl, out var scheme, out var host, out var port,
                        out var user, out var pass))
                {
                    _savedDefaultProxy = HttpClient.DefaultProxy;
                    var proxy = new SelectiveProxy($"{scheme}://{host}:{port}", config.ProxyDomains);
                    if (!string.IsNullOrEmpty(user))
                        proxy.Credentials = new NetworkCredential(user, pass);
                    HttpClient.DefaultProxy = proxy;

                    Log.LogInformation("Proxy enabled: {Url}", $"{scheme}://{host}:{port}");
                }
            }
            else if (_savedDefaultProxy != null)
            {
                HttpClient.DefaultProxy = _savedDefaultProxy;
                _savedDefaultProxy = null;
                Log.LogInformation("Proxy disabled");
            }

            if (config.EnableIPv4Only)
                Log.LogInformation("IPv4 force enabled for: {Domains}", config.IPv4OnlyDomains);
            if (config.EnableAltTmdb)
            {
                Log.LogInformation("TMDB API rewrite: {Url}", config.AltTmdbApiUrl);
                if (!string.IsNullOrEmpty(config.AltTmdbImageUrl))
                    Log.LogInformation("TMDB Image rewrite: {Url}", config.AltTmdbImageUrl);
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                },
            };
        }

        public Stream GetThumbImage()
        {
            return GetType().Assembly.GetManifestResourceStream("JellyfinProxy.Properties.thumb.png");
        }
    }
}
