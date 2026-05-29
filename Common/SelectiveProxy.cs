using System;
using System.Collections.Generic;
using System.Net;

namespace JellyfinProxy.Common
{
    public class SelectiveProxy : IWebProxy
    {
        private WebProxy _innerProxy;
        private HashSet<string> _proxyDomains;

        public int DomainCount => _proxyDomains.Count;

        public ICredentials Credentials
        {
            get => _innerProxy.Credentials;
            set => _innerProxy.Credentials = value;
        }

        public SelectiveProxy(string proxyUrl, string domainList)
        {
            _innerProxy = new WebProxy(proxyUrl);
            _proxyDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(domainList))
            {
                foreach (var line in domainList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrWhiteSpace(domain))
                        _proxyDomains.Add(domain);
                }
            }
        }

        public Uri GetProxy(Uri destination)
        {
            if (_proxyDomains.Count == 0) return null;

            foreach (var domain in _proxyDomains)
            {
                if (destination.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                {
                    var proxyUri = _innerProxy.GetProxy(destination);
                    if (Plugin.Instance.DebugMode)
                        Plugin.Instance.Logger.LogInformation("Proxy: {Host} → {Proxy}", destination.Host, proxyUri);
                    return proxyUri;
                }
            }

            return null;
        }

        public bool IsBypassed(Uri host)
        {
            foreach (var domain in _proxyDomains)
            {
                if (host.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}
