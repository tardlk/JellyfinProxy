using MediaBrowser.Model.Plugins;

namespace JellyfinProxy
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool ProxyEnabled { get; set; } = false;

        public string ProxyUrl { get; set; } = string.Empty;

        public string ProxyDomains { get; set; } =
            "api.themoviedb.org\r\nimage.tmdb.org\r\napi.tmdb.org\r\napi.tvdb.com\r\nartworks.thetvdb.com\r\nwebservice.fanart.tv\r\nassets.fanart.tv";

        public bool EnableIPv4Only { get; set; } = false;

        public string IPv4OnlyDomains { get; set; } = "image.tmdb.org";

        public bool EnableAltTmdb { get; set; } = false;

        public string AltTmdbApiUrl { get; set; } = "https://api.tmdb.org";

        public string AltTmdbImageUrl { get; set; } = string.Empty;

        public bool EnableDebugMode { get; set; } = false;
    }
}
