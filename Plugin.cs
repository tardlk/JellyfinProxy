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
        private readonly LocalProxyServer _localProxy = new LocalProxyServer();
        private IWebProxy _savedDefaultProxy;

        /// <summary>调试模式（热更新同步）</summary>
        public static bool DebugMode { get; private set; }

        public Plugin(
            IApplicationHost applicationHost,
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILoggerFactory loggerFactory)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            ApplicationHost = applicationHost;
            Log = loggerFactory.CreateLogger("JellyfinProxy");

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
            DebugMode = config.EnableDebugMode;
            var needProxy = config.ProxyEnabled || config.EnableIPv4Only || config.EnableAltTmdb;

            if (needProxy)
            {
                _localProxy.Configure(
                    config.ProxyEnabled, config.ProxyUrl, config.ProxyDomains,
                    config.EnableIPv4Only, config.IPv4OnlyDomains,
                    config.EnableAltTmdb, config.AltTmdbApiUrl, config.AltTmdbImageUrl);

                _savedDefaultProxy = HttpClient.DefaultProxy;
                HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:57891");
                _localProxy.Start();
            }
            else
            {
                _localProxy.Stop();
                if (_savedDefaultProxy != null)
                    HttpClient.DefaultProxy = _savedDefaultProxy;
            }

            if (config.ProxyEnabled && !string.IsNullOrWhiteSpace(config.ProxyUrl))
                Log.LogInformation("Proxy enabled for: {Domains}", config.ProxyDomains);
            if (config.EnableIPv4Only)
                Log.LogInformation("IPv4 force: {Domains}", config.IPv4OnlyDomains);
            if (config.EnableAltTmdb)
            {
                Log.LogInformation("TMDB API → {Url}", config.AltTmdbApiUrl);
                if (!string.IsNullOrEmpty(config.AltTmdbImageUrl))
                    Log.LogInformation("TMDB Image → {Url}", config.AltTmdbImageUrl);
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
