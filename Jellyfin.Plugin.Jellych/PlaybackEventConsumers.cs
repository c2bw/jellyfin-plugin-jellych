using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellych.WebhookPlugin;

/// <summary>
/// Playback start event consumer.
/// </summary>
public class PlaybackStartEventConsumer : IEventConsumer<PlaybackStartEventArgs>
{
    private readonly ILogger<PlaybackStartEventConsumer>? _logger;
    private readonly ILogger<PlaybackWebhookForwarder>? _forwarderLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStartEventConsumer"/> class.
    /// </summary>
    /// <param name="logger">Consumer logger.</param>
    /// <param name="forwarderLogger">Webhook forwarder logger.</param>
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
            await PlaybackSessionTracker.Instance.HandlePlaybackStartAsync(@event, _logger, _forwarderLogger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in playback start");
            JellychPluginLog.Write($"Error in playback start: {ex}");
        }
    }
}

/// <summary>
/// Playback stop event consumer.
/// </summary>
public class PlaybackStopEventConsumer : IEventConsumer<PlaybackStopEventArgs>
{
    private readonly ILogger<PlaybackStopEventConsumer>? _logger;
    private readonly ILogger<PlaybackWebhookForwarder>? _forwarderLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStopEventConsumer"/> class.
    /// </summary>
    /// <param name="logger">Consumer logger.</param>
    /// <param name="forwarderLogger">Webhook forwarder logger.</param>
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
            await PlaybackSessionTracker.Instance.HandlePlaybackStopAsync(@event, _logger, _forwarderLogger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in playback stop");
            JellychPluginLog.Write($"Error in playback stop: {ex}");
        }
    }
}

/// <summary>
/// Session ended event consumer that stops any active jellych stream when a client disconnects or closes without stopping playback.
/// </summary>
public class SessionEndedEventConsumer : IEventConsumer<SessionEndedEventArgs>
{
    private readonly ILogger<SessionEndedEventConsumer>? _logger;
    private readonly ILogger<PlaybackWebhookForwarder>? _forwarderLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEndedEventConsumer"/> class.
    /// </summary>
    /// <param name="logger">Consumer logger.</param>
    /// <param name="forwarderLogger">Webhook forwarder logger.</param>
    public SessionEndedEventConsumer(
        ILogger<SessionEndedEventConsumer>? logger = null,
        ILogger<PlaybackWebhookForwarder>? forwarderLogger = null)
    {
        _logger = logger;
        _forwarderLogger = forwarderLogger;
    }

    /// <inheritdoc />
    public async Task OnEvent(SessionEndedEventArgs @event)
    {
        if (@event?.Argument == null)
        {
            return;
        }

        try
        {
            await PlaybackSessionTracker.Instance.HandleSessionEndedAsync(@event, _logger, _forwarderLogger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in session ended consumer");
            JellychPluginLog.Write($"Error in session ended consumer: {ex}");
        }
    }
}

internal sealed class PlaybackSessionTracker
{
    public static PlaybackSessionTracker Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ActiveSessionState> _activeBySessionId = new(StringComparer.Ordinal);

    public async Task HandlePlaybackStartAsync(
        PlaybackStartEventArgs @event,
        ILogger? consumerLogger,
        ILogger<PlaybackWebhookForwarder>? forwarderLogger)
    {
        var hasChannel = PlaybackEventConsumerHelpers.TryResolveLiveChannel(@event, out var channel);
        var sessionId = PlaybackEventConsumerHelpers.GetStableSessionId(@event, hasChannel ? channel : null);
        var playSessionId = NormalizeOptionalId(@event.PlaySessionId);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!hasChannel)
            {
                // If this session switched from a Live TV channel to a non-Live-TV item (e.g., movie or VOD)
                // without first firing PlaybackStopped, stop the previous channel.
                if (_activeBySessionId.Remove(sessionId, out var previousNonLive))
                {
                    consumerLogger?.LogInformation(
                        "Stopping previous channel {PreviousChannel} because session {SessionId} started non-live playback",
                        previousNonLive.Channel,
                        sessionId);
                    JellychPluginLog.Write(
                        $"Stopping previous channel {previousNonLive.Channel} because session {sessionId} started non-live playback");
                    await PlaybackWebhookDispatcher.ForwardAsync("stop", previousNonLive.Channel, sessionId, forwarderLogger)
                        .ConfigureAwait(false);
                }

                return;
            }

            if (_activeBySessionId.TryGetValue(sessionId, out var existing)
                && !string.Equals(existing.Channel, channel, StringComparison.OrdinalIgnoreCase))
            {
                consumerLogger?.LogInformation(
                    "Session {SessionId} switched channel from {PreviousChannel} to {NewChannel}; stopping {PreviousChannel}",
                    sessionId,
                    existing.Channel,
                    channel,
                    existing.Channel);
                JellychPluginLog.Write(
                    $"Session {sessionId} switched channel from {existing.Channel} to {channel}; stopping {existing.Channel}");
                await PlaybackWebhookDispatcher.ForwardAsync("stop", existing.Channel, sessionId, forwarderLogger)
                    .ConfigureAwait(false);
            }

            _activeBySessionId[sessionId] = new ActiveSessionState(channel, playSessionId);

            consumerLogger?.LogInformation("Playback start: {SourceName} (session: {SessionId})", channel, sessionId);
            JellychPluginLog.Write($"Playback start: {channel} (session: {sessionId}, playSessionId: {playSessionId ?? string.Empty})");
            await PlaybackWebhookDispatcher.ForwardAsync("start", channel, sessionId, forwarderLogger).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandlePlaybackStopAsync(
        PlaybackStopEventArgs @event,
        ILogger? consumerLogger,
        ILogger<PlaybackWebhookForwarder>? forwarderLogger)
    {
        var hasEventChannel = PlaybackEventConsumerHelpers.TryResolveLiveChannel(@event, out var eventChannel);
        var sessionId = PlaybackEventConsumerHelpers.GetStableSessionId(@event, hasEventChannel ? eventChannel : null);
        var stopPlaySessionId = NormalizeOptionalId(@event.PlaySessionId);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeBySessionId.TryGetValue(sessionId, out var active))
            {
                // If this stop event targets a different channel than the one currently active on this session,
                // the previous channel was already stopped when the new channel started.
                if (hasEventChannel && !string.Equals(active.Channel, eventChannel, StringComparison.OrdinalIgnoreCase))
                {
                    consumerLogger?.LogDebug(
                        "Ignoring late playback stop for {EventChannel} on session {SessionId} (currently active: {ActiveChannel})",
                        eventChannel,
                        sessionId,
                        active.Channel);
                    JellychPluginLog.Write(
                        $"Ignoring late playback stop for {eventChannel} on session {sessionId} (currently active: {active.Channel})");
                    return;
                }

                // If the event is explicitly for a non-Live-TV item while a channel was tracked, only ignore if Item/MediaInfo is present and non-live.
                if (!hasEventChannel && (@event.Item is not null || @event.MediaInfo is not null))
                {
                    return;
                }

                // If both the active state and the stop event have PlaySessionIds and they differ,
                // a newer PlaySessionId (e.g., from a quality/bitrate change or stream reload) has superseded the stopped one.
                if (!string.IsNullOrEmpty(active.PlaySessionId)
                    && !string.IsNullOrEmpty(stopPlaySessionId)
                    && !string.Equals(active.PlaySessionId, stopPlaySessionId, StringComparison.Ordinal))
                {
                    consumerLogger?.LogInformation(
                        "Ignoring stale playback stop for {Channel} (session: {SessionId}, stoppedPlaySessionId: {StoppedPlaySessionId}, activePlaySessionId: {ActivePlaySessionId})",
                        active.Channel,
                        sessionId,
                        stopPlaySessionId,
                        active.PlaySessionId);
                    JellychPluginLog.Write(
                        $"Ignoring stale playback stop for {active.Channel} (session: {sessionId}, stoppedPlaySessionId: {stopPlaySessionId}, activePlaySessionId: {active.PlaySessionId})");
                    return;
                }

                _activeBySessionId.Remove(sessionId);
                var channelToStop = hasEventChannel ? eventChannel : active.Channel;

                consumerLogger?.LogInformation("Playback stop: {SourceName} (session: {SessionId})", channelToStop, sessionId);
                JellychPluginLog.Write($"Playback stop: {channelToStop} (session: {sessionId}, playSessionId: {stopPlaySessionId ?? string.Empty})");
                await PlaybackWebhookDispatcher.ForwardAsync("stop", channelToStop, sessionId, forwarderLogger).ConfigureAwait(false);
                return;
            }

            if (!hasEventChannel)
            {
                return;
            }

            consumerLogger?.LogInformation("Playback stop: {SourceName} (session: {SessionId})", eventChannel, sessionId);
            JellychPluginLog.Write($"Playback stop: {eventChannel} (session: {sessionId}, playSessionId: {stopPlaySessionId ?? string.Empty})");
            await PlaybackWebhookDispatcher.ForwardAsync("stop", eventChannel, sessionId, forwarderLogger).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleSessionEndedAsync(
        SessionEndedEventArgs @event,
        ILogger? consumerLogger,
        ILogger<PlaybackWebhookForwarder>? forwarderLogger)
    {
        var session = @event.Argument;
        if (session == null || string.IsNullOrWhiteSpace(session.Id))
        {
            return;
        }

        var sessionId = session.Id.Trim();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? channelToStop = null;
            if (_activeBySessionId.Remove(sessionId, out var active))
            {
                channelToStop = active.Channel;
            }
            else if (PlaybackEventConsumerHelpers.TryResolveLiveChannelFromDto(session.NowPlayingItem, out var dtoChannel))
            {
                channelToStop = dtoChannel;
            }

            if (string.IsNullOrEmpty(channelToStop))
            {
                return;
            }

            consumerLogger?.LogInformation("Session ended while playing {SourceName} (session: {SessionId}); sending stop webhook", channelToStop, sessionId);
            JellychPluginLog.Write($"Session ended while playing {channelToStop} (session: {sessionId}); sending stop webhook");
            await PlaybackWebhookDispatcher.ForwardAsync("stop", channelToStop, sessionId, forwarderLogger).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAllActiveSessionsAsync(
        ILogger<PlaybackWebhookForwarder>? forwarderLogger,
        CancellationToken cancellationToken = default)
    {
        List<KeyValuePair<string, ActiveSessionState>> snapshot;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeBySessionId.Count == 0)
            {
                return;
            }

            snapshot = new List<KeyValuePair<string, ActiveSessionState>>(_activeBySessionId);
            _activeBySessionId.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var entry in snapshot)
        {
            JellychPluginLog.Write($"Stopping active session on plugin shutdown: {entry.Value.Channel} (session: {entry.Key})");
            await PlaybackWebhookDispatcher.ForwardAsync("stop", entry.Value.Channel, entry.Key, forwarderLogger, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string? NormalizeOptionalId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ActiveSessionState(string Channel, string? PlaySessionId);
}

internal static class PlaybackEventConsumerHelpers
{
    public static bool TryResolveLiveChannel(PlaybackProgressEventArgs @event, out string channel)
    {
        channel = string.Empty;

        // 1. Direct match on a jellych live HLS path (/live/<channel>/index.m3u8).
        if (PlaybackWebhookForwarder.TryExtractChannelFromLivePath(@event.Item?.Path, out channel)
            || PlaybackWebhookForwarder.TryExtractChannelFromLivePath(@event.MediaInfo?.Path, out channel))
        {
            return true;
        }

        // 2. Verify that the item being played is a Live TV channel (or a synthetic event with no Item/MediaInfo).
        if (!IsLiveTvPlayback(@event))
        {
            return false;
        }

        // 3. Extract channel name from Item.Name, MediaInfo.Name, or non-GUID MediaSourceId.
        if (PlaybackWebhookForwarder.TryExtractChannelName(@event.Item?.Name, out channel)
            || PlaybackWebhookForwarder.TryExtractChannelName(@event.MediaInfo?.Name, out channel))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(@event.MediaSourceId)
            && !Guid.TryParse(@event.MediaSourceId, out _)
            && PlaybackWebhookForwarder.TryExtractChannelName(@event.MediaSourceId, out channel))
        {
            return true;
        }

        return false;
    }

    public static bool TryResolveLiveChannelFromDto(BaseItemDto? itemDto, out string channel)
    {
        channel = string.Empty;
        if (itemDto == null)
        {
            return false;
        }

        if (PlaybackWebhookForwarder.TryExtractChannelFromLivePath(itemDto.Path, out channel))
        {
            return true;
        }

        if (itemDto.Type != BaseItemKind.LiveTvChannel && itemDto.IsLive != true)
        {
            return false;
        }

        return PlaybackWebhookForwarder.TryExtractChannelName(itemDto.Name, out channel);
    }

    public static bool IsLiveTvPlayback(PlaybackProgressEventArgs @event)
    {
        if (@event.Item is LiveTvChannel)
        {
            return true;
        }

        if (@event.Item is not null && @event.Item.GetBaseItemKind() == BaseItemKind.LiveTvChannel)
        {
            return true;
        }

        if (@event.MediaInfo is not null && (@event.MediaInfo.Type == BaseItemKind.LiveTvChannel || @event.MediaInfo.IsLive == true))
        {
            return true;
        }

        if (@event.Item is null && @event.MediaInfo is null && !string.IsNullOrWhiteSpace(@event.MediaSourceId))
        {
            return !Guid.TryParse(@event.MediaSourceId, out _);
        }

        return false;
    }

    public static string GetStableSessionId(PlaybackProgressEventArgs @event, string? fallbackChannel = null)
    {
        if (!string.IsNullOrWhiteSpace(@event.Session?.Id))
        {
            return @event.Session.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(@event.PlaySessionId))
        {
            return @event.PlaySessionId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(@event.DeviceId))
        {
            return @event.DeviceId.Trim();
        }

        var sourceName = fallbackChannel
            ?? @event.Item?.Name
            ?? @event.MediaInfo?.Name
            ?? @event.MediaSourceId
            ?? "unknown";

        return BuildFallbackSessionId(@event, sourceName);
    }

    private static string BuildFallbackSessionId(PlaybackProgressEventArgs @event, string sourceName)
    {
        var userId = (@event.Users is { Count: > 0 } && @event.Users[0] is not null ? @event.Users[0].Id.ToString("N") : null)
            ?? (@event.Session is not null && @event.Session.UserId != Guid.Empty ? @event.Session.UserId.ToString("N") : null)
            ?? "unknown-user";
        var itemId = (@event.Item is not null && @event.Item.Id != Guid.Empty ? @event.Item.Id.ToString("N") : null)
            ?? (@event.MediaInfo is not null && @event.MediaInfo.Id != Guid.Empty ? @event.MediaInfo.Id.ToString("N") : null)
            ?? "unknown-item";
        return $"{userId}:{itemId}:{sourceName.Trim().ToLowerInvariant()}";
    }
}

internal static class PlaybackWebhookDispatcher
{
    public static async Task ForwardAsync(
        string action,
        string sourceName,
        string sessionId,
        ILogger<PlaybackWebhookForwarder>? logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = Plugin.Instance?.Configuration ?? new Jellych.WebhookPlugin.Configuration.PluginConfiguration();
            using var forwarder = new PlaybackWebhookForwarder(settings, logger);

            if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase))
            {
                await forwarder.OnPlaybackStartAsync(sourceName, sessionId, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase))
            {
                await forwarder.OnPlaybackStopAsync(sourceName, sessionId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to forward webhook");
            JellychPluginLog.Write($"Failed to forward webhook: {ex}");
        }
    }
}
