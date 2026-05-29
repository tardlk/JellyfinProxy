using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using JellyfinProxy.Mod;

namespace JellyfinProxy
{
    /// <summary>
    /// 注册插件服务到 Jellyfin DI 容器。
    /// </summary>
    public class ServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection services, MediaBrowser.Controller.IServerApplicationHost appHost)
        {
            // 注册 HttpClient 拦截过滤器（单例，热更新通过 UpdateConfig 实现）
            var filter = new HttpClientFilter();
            services.AddSingleton(filter);

            // 注册 IHttpMessageHandlerBuilderFilter
            services.AddSingleton<IHttpMessageHandlerBuilderFilter>(filter);
        }
    }
}
