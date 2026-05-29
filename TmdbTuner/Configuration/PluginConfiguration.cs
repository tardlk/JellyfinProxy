using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TmdbTuner
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string TmdbHost { get; set; } = "api.tmdb.org";

        public string TmdbImageHost { get; set; } = "image.tmdb.org";

        public string TmdbApiKey { get; set; } = string.Empty;

        public bool EnableIPv4Only { get; set; } = true;

        public bool EnableProxy { get; set; } = false;

        public string ProxyUrl { get; set; } = string.Empty;

        public bool EnableDebugMode { get; set; } = false;
    }
}
