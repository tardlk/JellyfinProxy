using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.TmdbTuner.Api;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.TmdbTuner
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public override string Name => "TmdbTuner";
        public override Guid Id => Guid.Parse("C8D3E9A2-6D5F-4C3B-0A7D-2F4E9B6C3D8F");
        public override string Description => "TmdbTuner - TMDB metadata provider with custom host, proxy and IPv4 force";

        public static ILogger Log { get; private set; }
        public TmdbApiClient TmdbClient { get; }

        public Plugin(
            IApplicationHost applicationHost,
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILoggerFactory loggerFactory)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            TmdbClient = new TmdbApiClient(loggerFactory.CreateLogger("TmdbTuner.Api"));
            Log = loggerFactory.CreateLogger("TmdbTuner");
            Log.LogInformation("Plugin is getting loaded.");

            ApplyConfig(Configuration);

            ConfigurationChanged += (_, _) =>
            {
                ApplyConfig(Configuration);
                Log.LogInformation("Configuration hot-reloaded");
            };
        }

        private void ApplyConfig(PluginConfiguration config)
        {
            TmdbClient.Configure(
                config.TmdbHost,
                config.TmdbApiKey,
                config.EnableIPv4Only,
                config.EnableProxy ? config.ProxyUrl : null,
                config.EnableDebugMode);
        }

        public PluginConfiguration GetPluginConfiguration() => Configuration;

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
            return GetType().Assembly.GetManifestResourceStream(
                "TmdbTuner.Properties.thumb.png");
        }
    }
}
