using System;

namespace JellyfinProxy.Mod
{
    public class ForceIPv4
    {
        public bool IsActive { get; private set; }

        public void Apply()
        {
            if (IsActive) return;

            var config = Plugin.Instance.GetPluginConfiguration();
            if (!config.EnableIPv4Only) return;

            try
            {
                HandlerInterceptor.Apply();
                IsActive = true;

                var count = string.IsNullOrEmpty(config.IPv4OnlyDomains) ? 0 :
                    config.IPv4OnlyDomains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Plugin.Instance.Logger.LogInformation("ForceIPv4 enabled for {Count} domains", count);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.LogWarning("ForceIPv4 failed: {Message}", e.Message);
            }
        }

        public void Remove()
        {
            if (!IsActive) return;
            HandlerInterceptor.Remove();
            IsActive = false;
            Plugin.Instance.Logger.LogInformation("ForceIPv4 disabled");
        }
    }
}
