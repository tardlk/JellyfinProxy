using System;

namespace JellyfinProxy.Mod
{
    public class AltMovieDbConfig
    {
        public bool IsActive { get; private set; }

        public void Apply()
        {
            if (IsActive) return;

            var config = Plugin.Instance.GetPluginConfiguration();
            if (!config.EnableAltTmdb) return;

            try
            {
                HandlerInterceptor.Apply();
                IsActive = true;

                Plugin.Instance.Logger.LogInformation("AltMovieDbConfig enabled");
                if (!string.IsNullOrEmpty(config.AltTmdbApiUrl))
                    Plugin.Instance.Logger.LogInformation("  API URL: {Url}", config.AltTmdbApiUrl);
                if (!string.IsNullOrEmpty(config.AltTmdbImageUrl))
                    Plugin.Instance.Logger.LogInformation("  Image URL: {Url}", config.AltTmdbImageUrl);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.LogWarning("AltMovieDbConfig failed: {Message}", e.Message);
            }
        }

        public void Remove()
        {
            if (!IsActive) return;
            HandlerInterceptor.Remove();
            IsActive = false;
            Plugin.Instance.Logger.LogInformation("AltMovieDbConfig disabled");
        }
    }
}
