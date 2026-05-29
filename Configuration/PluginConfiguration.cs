using MediaBrowser.Model.Plugins;

namespace JellyfinProxy
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool ProxyEnabled { get; set; } = false;

        public string ProxyUrl { get; set; } = string.Empty;

        public string ProxyDomains { get; set; } =
            "api.themoviedb.org\r\nimage.tmdb.org\r\napi.tmdb.org\r\napi.tvdb.com\r\nartworks.thetvdb.com\r\nwebservice.fanart.tv\r\nassets.fanart.tv";

        public bool EnableDebugMode { get; set; } = false;
    }
}
