using JellyfinProxy.Common;
using System;
using System.Net;
using System.Net.Http;
using static JellyfinProxy.Common.CommonUtility;

namespace JellyfinProxy.Mod
{
    public class EnableProxyServer
    {
        private SelectiveProxy _selectiveProxy;
        private IWebProxy _savedDefaultProxy;

        public bool IsActive { get; private set; }

        public void Apply()
        {
            try
            {
                if (IsActive) return;

                var config = Plugin.Instance.GetPluginConfiguration();

                if (!config.ProxyEnabled || string.IsNullOrWhiteSpace(config.ProxyUrl))
                    return;

                if (!TryParseProxyUrl(config.ProxyUrl, out var schema, out var host, out var port,
                        out var username, out var password))
                    return;

                var proxyUrl = $"{schema}://{host}:{port}";

                _selectiveProxy = new SelectiveProxy(proxyUrl, config.ProxyDomains);

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    _selectiveProxy.Credentials = new NetworkCredential(username, password);

                _savedDefaultProxy = HttpClient.DefaultProxy;
                HttpClient.DefaultProxy = _selectiveProxy;
                IsActive = true;

                Plugin.Instance.Logger.LogInformation("Proxy enabled: {Url} for {Count} domains", proxyUrl, _selectiveProxy.DomainCount);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.LogError("Failed to apply proxy: {Message}", e.Message);
            }
        }

        public void Remove()
        {
            try
            {
                if (!IsActive) return;

                HttpClient.DefaultProxy = _savedDefaultProxy;
                _selectiveProxy = null;
                IsActive = false;

                Plugin.Instance.Logger.LogInformation("Proxy disabled");
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.LogError("Failed to remove proxy: {Message}", e.Message);
            }
        }
    }
}
