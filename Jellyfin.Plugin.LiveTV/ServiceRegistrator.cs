using Jellyfin.Plugin.LiveTV.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTV;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ScheduleManager>();
        serviceCollection.AddSingleton<StreamManager>();
        serviceCollection.AddSingleton<ILiveTvService, LiveTvService>();
    }
}
