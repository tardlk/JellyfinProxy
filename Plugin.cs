using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Serialization;
using JellyfinProxy.Mod;
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
        public override string Description => "JellyfinProxy - Alt TMDB Config & Selective Proxy";

        public readonly EnableProxyServer EnableProxyServer;
        public readonly AltMovieDbConfig AltMovieDbConfig;
        public readonly ForceIPv4 ForceIPv4;
        public readonly IApplicationHost ApplicationHost;
        public bool DebugMode;

        public Plugin(IApplicationHost applicationHost, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            ApplicationHost = applicationHost;

            EnableProxyServer = new EnableProxyServer();
            AltMovieDbConfig = new AltMovieDbConfig();
            ForceIPv4 = new ForceIPv4();

            if (Debugger.IsAttached) DebugMode = true;

            var config = GetPluginConfiguration();
            if (config.EnableDebugMode) DebugMode = true;

            if (DebugMode)
                Logger.LogInformation("Debug mode enabled");

            Logger.LogInformation("Plugin is getting loaded.");

            if (config.ProxyEnabled)
                EnableProxyServer.Apply();
            if (config.EnableIPv4Only)
                ForceIPv4.Apply();
            if (config.EnableAltTmdb)
                AltMovieDbConfig.Apply();
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
