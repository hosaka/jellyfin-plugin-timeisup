using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TimeIsUp;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<TimeKeeper>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<TimeKeeper>());
        serviceCollection.Configure<MvcOptions>(options => options.Filters.Add<PlaybackGuard>());
    }
}
