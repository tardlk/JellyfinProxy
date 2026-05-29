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

        /// <summary>插件卸载时清理资源</summary>
        public override void OnUninstalling()
        {
            _localProxy.Dispose();
            if (_savedDefaultProxy != null)
                HttpClient.DefaultProxy = _savedDefaultProxy;
            base.OnUninstalling();
        }

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
                ApplicationHost.NotifyPendingRestart();
            };
        }

        public PluginConfiguration GetPluginConfiguration() => Configuration;

        public void ApplyConfig(PluginConfiguration config)
        {
            DebugMode = config.EnableDebugMode;
            var needLocalProxy = config.EnableIPv4Only || config.EnableAltTmdb;

            if (needLocalProxy)
            {
                // 需要 TMDB 改写或 IPv4 强制 → 起本地代理
                _localProxy.Configure(
                    config.ProxyEnabled, config.ProxyUrl, config.ProxyDomains,
                    config.EnableIPv4Only, config.IPv4OnlyDomains,
                    config.EnableAltTmdb, config.AltTmdbApiUrl, config.AltTmdbImageUrl);

                    _savedDefaultProxy ??= HttpClient.DefaultProxy;
                HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:57891");
                _localProxy.Start();
            }
            else if (config.ProxyEnabled && !string.IsNullOrWhiteSpace(config.ProxyUrl))
            {
                // 纯代理模式 → 直接用 SelectiveProxy，不起本地代理
                _localProxy.Stop();
                if (CommonUtility.TryParseProxyUrl(config.ProxyUrl, out var scheme, out var host, out var port,
                        out var user, out var pass))
                {
                _savedDefaultProxy ??= HttpClient.DefaultProxy;
                    var proxy = new SelectiveProxy($"{scheme}://{host}:{port}", config.ProxyDomains);
                    if (!string.IsNullOrEmpty(user))
                        proxy.Credentials = new NetworkCredential(user, pass);
                    HttpClient.DefaultProxy = proxy;
                }
            }
            else
            {
                // 全关 → 恢复默认
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
