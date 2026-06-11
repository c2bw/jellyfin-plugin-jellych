using System;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellych.WebhookPlugin;

/// <summary>
/// Playback start event consumer.
/// </summary>
public class PlaybackStartEventConsumer : IEventConsumer<PlaybackStartEventArgs>
{
    private readonly ILogger<PlaybackStartEventConsumer>? _logger;
    private readonly ILogger<PlaybackWebhookForwarder>? _forwarderLogger;

    public PlaybackStartEventConsumer(
        ILogger<PlaybackStartEventConsumer>? logger = null,
        ILogger<PlaybackWebhookForwarder>? forwarderLogger = null)
    {
        _logger = logger;
        _forwarderLogger = forwarderLogger;
    }

    /// <inheritdoc />
    public async Task OnEvent(PlaybackStartEventArgs @event)
    {
        if (@event == null)
        {
            return;
        }

        try
        {
            var sourceName = GetSourceName(@event);
            var sessionId = PlaybackEventConsumerHelpers.GetStableSessionId(@event, sourceName);

            _logger?.LogInformation("Playback start: {SourceName} (session: {SessionId})", sourceName, sessionId);
            JellychPluginLog.Write($"Playback start: {sourceName} (session: {sessionId}, playSessionId: {@event.PlaySessionId ?? string.Empty})");
            await ForwardWebhookAsync("start", sourceName, sessionId, _forwarderLogger);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in playback start");
            JellychPluginLog.Write($"Error in playback start: {ex}");
        }
    }

    private static async Task ForwardWebhookAsync(
        string action,
        string sourceName,
        string sessionId,
        ILogger<PlaybackWebhookForwarder>? logger)
    {
        await PlaybackWebhookDispatcher.ForwardAsync(action, sourceName, sessionId, logger).ConfigureAwait(false);
    }

    private static string GetSourceName(PlaybackProgressEventArgs @event)
    {
        return @event.Item?.Name
            ?? @event.MediaInfo?.Name
            ?? @event.MediaSourceId
            ?? "unknown";
    }
}

/// <summary>
/// Playback stop event consumer.
/// </summary>
public class PlaybackStopEventConsumer : IEventConsumer<PlaybackStopEventArgs>
{
    private readonly ILogger<PlaybackStopEventConsumer>? _logger;
    private readonly ILogger<PlaybackWebhookForwarder>? _forwarderLogger;

    public PlaybackStopEventConsumer(
        ILogger<PlaybackStopEventConsumer>? logger = null,
        ILogger<PlaybackWebhookForwarder>? forwarderLogger = null)
    {
        _logger = logger;
        _forwarderLogger = forwarderLogger;
    }

    /// <inheritdoc />
    public async Task OnEvent(PlaybackStopEventArgs @event)
    {
        if (@event == null)
        {
            return;
        }

        try
        {
            var sourceName = GetSourceName(@event);
            var sessionId = PlaybackEventConsumerHelpers.GetStableSessionId(@event, sourceName);

            _logger?.LogInformation("Playback stop: {SourceName} (session: {SessionId})", sourceName, sessionId);
            JellychPluginLog.Write($"Playback stop: {sourceName} (session: {sessionId}, playSessionId: {@event.PlaySessionId ?? string.Empty})");
            await ForwardWebhookAsync("stop", sourceName, sessionId, _forwarderLogger);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in playback stop");
            JellychPluginLog.Write($"Error in playback stop: {ex}");
        }
    }

    private static async Task ForwardWebhookAsync(
        string action,
        string sourceName,
        string sessionId,
        ILogger<PlaybackWebhookForwarder>? logger)
    {
        await PlaybackWebhookDispatcher.ForwardAsync(action, sourceName, sessionId, logger).ConfigureAwait(false);
    }

    private static string GetSourceName(PlaybackProgressEventArgs @event)
    {
        return @event.Item?.Name
            ?? @event.MediaInfo?.Name
            ?? @event.MediaSourceId
            ?? "unknown";
    }

}

internal static class PlaybackEventConsumerHelpers
{
    public static string GetStableSessionId(PlaybackProgressEventArgs @event, string sourceName)
    {
        return GetNestedString(@event, "Session", "Id")
            ?? GetString(@event, nameof(@event.PlaySessionId))
            ?? GetString(@event, "DeviceId")
            ?? BuildFallbackSessionId(@event, sourceName);
    }

    private static string BuildFallbackSessionId(PlaybackProgressEventArgs @event, string sourceName)
    {
        var userId = GetString(@event, "UserId") ?? "unknown-user";
        var itemId = @event.Item?.Id.ToString("N") ?? GetString(@event, "ItemId") ?? "unknown-item";
        return $"{userId}:{itemId}:{sourceName.Trim().ToLowerInvariant()}";
    }

    private static string? GetNestedString(object value, string propertyName, string nestedPropertyName)
    {
        var nested = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        return nested == null ? null : GetString(nested, nestedPropertyName);
    }

    private static string? GetString(object value, string propertyName)
    {
        var propertyValue = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        var stringValue = propertyValue?.ToString();
        return string.IsNullOrWhiteSpace(stringValue) ? null : stringValue;
    }
}

internal static class PlaybackWebhookDispatcher
{
    public static async Task ForwardAsync(
        string action,
        string sourceName,
        string sessionId,
        ILogger<PlaybackWebhookForwarder>? logger)
    {
        try
        {
            var settings = Plugin.Instance?.Configuration ?? new Jellych.WebhookPlugin.Configuration.PluginConfiguration();
            using var forwarder = new PlaybackWebhookForwarder(settings, logger);

            if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase))
            {
                await forwarder.OnPlaybackStartAsync(sourceName, sessionId).ConfigureAwait(false);
            }
            else if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase))
            {
                await forwarder.OnPlaybackStopAsync(sourceName, sessionId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to forward webhook");
            JellychPluginLog.Write($"Failed to forward webhook: {ex}");
        }
    }
}
