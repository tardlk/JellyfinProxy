using MediaBrowser.Model.Plugins;

namespace JellyfinProxy
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // 代理服务器
        public bool ProxyEnabled { get; set; } = false;
        public string ProxyUrl { get; set; } = string.Empty;
        public string ProxyDomains { get; set; } =
            "api.themoviedb.org\r\nimage.tmdb.org\r\napi.tmdb.org\r\napi.tvdb.com\r\nartworks.thetvdb.com\r\nwebservice.fanart.tv\r\nassets.fanart.tv";

        // 本地代理端口
        public int LocalProxyPort { get; set; } = 57891;

        // IPv4 强制
        public bool EnableIPv4Only { get; set; } = false;
        public string IPv4OnlyDomains { get; set; } = "image.tmdb.org";

        // TMDB 替代
        public bool EnableAltTmdb { get; set; } = false;
        public string AltTmdbApiUrl { get; set; } = "https://api.tmdb.org";
        public string AltTmdbImageUrl { get; set; } = string.Empty;

        // 调试
        public bool EnableDebugMode { get; set; } = false;
    }
}
