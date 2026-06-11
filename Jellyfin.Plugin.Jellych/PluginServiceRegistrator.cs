using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellych.WebhookPlugin;

/// <summary>
/// Registers Jellych plugin services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<JellychStartupPingService>();
        serviceCollection.AddSingleton<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartEventConsumer>();
        serviceCollection.AddSingleton<IEventConsumer<PlaybackStopEventArgs>, PlaybackStopEventConsumer>();
    }
}
