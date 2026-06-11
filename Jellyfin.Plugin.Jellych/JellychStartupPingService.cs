using System;
using System.Threading;
using System.Threading.Tasks;
using Jellych.WebhookPlugin.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellych.WebhookPlugin;

/// <summary>
/// Pings the jellych server when Jellyfin starts the plugin services.
/// </summary>
public sealed class JellychStartupPingService : IHostedService
{
    private readonly ILogger<JellychStartupPingService> _logger;
    private readonly ILogger<PlaybackWebhookForwarder> _forwarderLogger;

    public JellychStartupPingService(
        ILogger<JellychStartupPingService> logger,
        ILogger<PlaybackWebhookForwarder> forwarderLogger)
    {
        _logger = logger;
        _forwarderLogger = forwarderLogger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Jellych plugin startup ping beginning");
        JellychPluginLog.Write("Jellych plugin startup ping beginning");

        var settings = Plugin.Instance?.Configuration;
        if (settings == null)
        {
            _logger.LogWarning("Jellych plugin startup ping skipped because plugin configuration is not available");
            JellychPluginLog.Write("Jellych plugin startup ping skipped because plugin configuration is not available");
            return;
        }

        try
        {
            using var forwarder = new PlaybackWebhookForwarder(settings, _forwarderLogger);
            await forwarder.PingAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Jellych plugin startup ping completed");
            JellychPluginLog.Write("Jellych plugin startup ping completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellych plugin startup ping failed");
            JellychPluginLog.Write($"Jellych plugin startup ping failed: {ex}");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Jellych plugin startup ping service stopping");
        JellychPluginLog.Write("Jellych plugin startup ping service stopping");
        return Task.CompletedTask;
    }
}
