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

namespace JellyfinProxy
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public override string Name => "JellyfinProxy";
        public override Guid Id => Guid.Parse("B5C3E8A1-7D4F-4A2B-9E6C-1F3D8A5B2C7E");
        public override string Description => "JellyfinProxy - HTTP Selective Proxy for Jellyfin";

        public static ILogger Log { get; private set; }
        public readonly EnableProxyServer EnableProxyServer;
        public readonly IApplicationHost ApplicationHost;
        public bool DebugMode;

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

            EnableProxyServer = new EnableProxyServer();

            if (Debugger.IsAttached) DebugMode = true;

            var config = GetPluginConfiguration();
            if (config.EnableDebugMode) DebugMode = true;

            if (DebugMode)
                Log.LogInformation("Debug mode enabled");

            Log.LogInformation("Plugin is getting loaded.");

            if (config.ProxyEnabled)
                EnableProxyServer.Apply();
        }

        public PluginConfiguration GetPluginConfiguration()
        {
            return Configuration;
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
