using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
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
        public readonly LocalProxyServer LocalProxy;
        public bool DebugMode;

        private IWebProxy _savedDefaultProxy;

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

            if (Debugger.IsAttached) DebugMode = true;

            var config = GetPluginConfiguration();
            if (config.EnableDebugMode) DebugMode = true;

            if (DebugMode)
                Log.LogInformation("Debug mode enabled");

            Log.LogInformation("Plugin is getting loaded.");
            Log.LogInformation("Jellyfin version: {Version}", applicationHost.ApplicationVersionString);

            // 启动本地代理（处理 TMDB 改写 + IPv4 强制）
            LocalProxy = new LocalProxyServer(config.LocalProxyPort);
            ApplyConfig(config);
        }

        public PluginConfiguration GetPluginConfiguration()
        {
            return Configuration;
        }

        /// <summary>配置变更时自动热更新，无需重启</summary>
        protected override void OnConfigurationChanged()
        {
            base.OnConfigurationChanged();
            var config = Configuration;
            DebugMode = config.EnableDebugMode;
            ApplyConfig(config);
            Log.LogInformation("Configuration hot-reloaded");
        }

        /// <summary>应用配置并启动/更新本地代理</summary>
        public void ApplyConfig(PluginConfiguration config)
        {
            // 配置本地代理
            LocalProxy.Configure(
                config.ProxyEnabled, config.ProxyUrl, config.ProxyDomains,
                config.EnableIPv4Only, config.IPv4OnlyDomains,
                config.EnableAltTmdb, config.AltTmdbApiUrl, config.AltTmdbImageUrl);

            // 默认代理指向本地代理
            if (config.ProxyEnabled || config.EnableIPv4Only || config.EnableAltTmdb)
            {
                _savedDefaultProxy = HttpClient.DefaultProxy;
                HttpClient.DefaultProxy = new WebProxy($"http://127.0.0.1:{config.LocalProxyPort}");
                LocalProxy.Start();

                Log.LogInformation("Proxy enabled → 127.0.0.1:{Port}", config.LocalProxyPort);
                if (config.EnableIPv4Only)
                    Log.LogInformation("  IPv4 force: {Domains}", config.IPv4OnlyDomains);
                if (config.EnableAltTmdb)
                {
                    Log.LogInformation("  TMDB API: {Url}", config.AltTmdbApiUrl);
                    if (!string.IsNullOrEmpty(config.AltTmdbImageUrl))
                        Log.LogInformation("  TMDB Image: {Url}", config.AltTmdbImageUrl);
                }
            }
            else
            {
                LocalProxy.Stop();
                if (_savedDefaultProxy != null)
                    HttpClient.DefaultProxy = _savedDefaultProxy;
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
