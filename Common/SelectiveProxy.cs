using System;
using System.Collections.Generic;
using System.Net;

namespace JellyfinProxy.Common
{
    /// <summary>
    /// 选择性代理：仅对白名单域名走代理，其余直连。
    /// </summary>
    public class SelectiveProxy : IWebProxy
    {
        private readonly WebProxy _innerProxy;
        private readonly HashSet<string> _proxyDomains;

        public SelectiveProxy(string proxyUrl, string domainList)
        {
            _innerProxy = new WebProxy(proxyUrl);
            _proxyDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(domainList))
            {
                foreach (var line in domainList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var d = line.Trim();
                    if (!string.IsNullOrWhiteSpace(d))
                        _proxyDomains.Add(d);
                }
            }
        }

        public ICredentials Credentials
        {
            get => _innerProxy.Credentials;
            set => _innerProxy.Credentials = value;
        }

        public Uri GetProxy(Uri destination)
        {
            if (_proxyDomains.Count == 0) return null;
            foreach (var d in _proxyDomains)
                if (destination.Host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                    return _innerProxy.GetProxy(destination);
            return null;
        }

        public bool IsBypassed(Uri host)
        {
            foreach (var d in _proxyDomains)
                if (host.Host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
    }
}
