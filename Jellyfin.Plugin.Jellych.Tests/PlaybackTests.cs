using System.Collections.Concurrent;
using System.Text.Json;
using Jellych.WebhookPlugin;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Jellych.Tests;

public class PlaybackTests
{
    private static PlaybackStartEventArgs Start(string viewer, string channel, string playId = "play-1", bool proxy = false)
        => new()
        {
            Session = new SessionInfo(null!, NullLogger.Instance) { Id = viewer },
            PlaySessionId = playId,
            MediaInfo = new BaseItemDto
            {
                Type = BaseItemKind.LiveTvChannel,
                Name = channel,
                Path = $"https://jellych.test/{(proxy ? "api/live-proxy" : "live")}/{channel}/index.m3u8",
            },
        };

    private static PlaybackStopEventArgs Stop(string viewer, string channel, string playId = "play-1")
        => new()
        {
            Session = new SessionInfo(null!, NullLogger.Instance) { Id = viewer },
            PlaySessionId = playId,
            MediaInfo = new BaseItemDto { Type = BaseItemKind.LiveTvChannel, Name = channel },
        };

    [Fact]
    public async Task SlowViewerDoesNotBlockOthersAndItsStopStaysOrdered()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new ConcurrentQueue<string>();
        var tracker = new PlaybackSessionTracker(async (action, channel, viewer, logger, ct, mode) =>
        {
            calls.Enqueue($"{viewer}:{action}");
            if (viewer == "slow" && action == "start")
            {
                entered.SetResult();
                await release.Task;
            }
        });
        var slow = tracker.HandlePlaybackStartAsync(Start("slow", "alpha"), null, null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var stop = tracker.HandlePlaybackStopAsync(Stop("slow", "alpha"), null, null);
            await tracker.HandlePlaybackStartAsync(Start("fast", "bravo"), null, null).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(stop.IsCompleted);
            Assert.Contains("fast:start", calls);
            release.SetResult();
            await Task.WhenAll(slow, stop).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "slow:start", "slow:stop" }, calls.Where(x => x.StartsWith("slow:")));
        }
        finally
        {
            release.TrySetResult();
            await slow;
        }
    }

    [Fact]
    public async Task SwitchingChannelsIgnoresLateStopsAndReportsProxyMode()
    {
        var calls = new List<string>();
        var tracker = new PlaybackSessionTracker((action, channel, viewer, logger, ct, mode) =>
        {
            calls.Add($"{action}:{channel}:{mode}");
            return Task.CompletedTask;
        });
        await tracker.HandlePlaybackStartAsync(Start("viewer", "alpha"), null, null);
        await tracker.HandlePlaybackStartAsync(Start("viewer", "bravo", "play-2", proxy: true), null, null);
        await tracker.HandlePlaybackStopAsync(Stop("viewer", "alpha"), null, null);
        await tracker.HandlePlaybackStopAsync(Stop("viewer", "bravo", "play-1"), null, null);
        await tracker.HandlePlaybackStopAsync(Stop("viewer", "bravo", "play-2"), null, null);
        Assert.Equal(new[] { "start:alpha:remux", "stop:alpha:", "start:bravo:proxy", "stop:bravo:" }, calls);
    }

    [Fact]
    public async Task ShutdownWaitsForPendingStartBeforeSendingStop()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new ConcurrentQueue<string>();
        var tracker = new PlaybackSessionTracker(async (action, channel, viewer, logger, ct, mode) =>
        {
            calls.Enqueue(action);
            if (action == "start")
            {
                entered.SetResult();
                await release.Task;
            }
        });
        var start = tracker.HandlePlaybackStartAsync(Start("viewer", "alpha"), null, null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var shutdown = tracker.StopAllActiveSessionsAsync(null);
            Assert.False(shutdown.IsCompleted);
            release.SetResult();
            await Task.WhenAll(start, shutdown).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "start", "stop" }, calls);
        }
        finally
        {
            release.TrySetResult();
            await start;
        }
    }

    [Theory]
    [InlineData("https://jellych.test/live/Alpha/index.m3u8", "alpha", false)]
    [InlineData("https://jellych.test/api/live-proxy/Bravo/index.m3u8?x=1", "bravo", true)]
    [InlineData("https://jellych.test/live/alpha/index.m3u8?next=/api/live-proxy/", "alpha", false)]
    [InlineData("/API/LIVE-PROXY/Bravo/index.m3u8", "bravo", true)]
    public void ResolvesBothPlaybackPaths(string path, string expected, bool proxy)
    {
        Assert.True(PlaybackWebhookForwarder.TryExtractChannelFromLivePath(path, out var channel));
        Assert.Equal(expected, channel);
        Assert.Equal(proxy, PlaybackWebhookForwarder.IsProxyLivePath(path));
    }

    [Fact]
    public void LegacyWebhookPayloadOmitsNewFields()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var legacy = JsonSerializer.Serialize(new WebhookPayload { Action = "start", Channel = "alpha", SessionId = "viewer" }, options);
        Assert.DoesNotContain("playbackMode", legacy);
        var proxy = JsonSerializer.Serialize(new WebhookPayload { PlaybackMode = "proxy" }, options);
        Assert.Contains("\"playbackMode\":\"proxy\"", proxy);
    }

    [Fact]
    public async Task CancelledSessionWaiterDoesNotReplaceAnActiveLock()
    {
        var gate = new SessionGate();
        using var holder = await gate.AcquireAsync("viewer");
        using var cancelled = new CancellationTokenSource();
        var waiter = gate.AcquireAsync("viewer", cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        var next = gate.AcquireAsync("viewer");
        Assert.False(next.IsCompleted);
        using var other = await gate.AcquireAsync("other").WaitAsync(TimeSpan.FromSeconds(2));
        holder.Dispose();
        using var acquired = await next.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
