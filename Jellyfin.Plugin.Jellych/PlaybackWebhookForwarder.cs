using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellych.WebhookPlugin.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellych.WebhookPlugin;

/// <summary>
/// Forwards playback start/stop to jellych using the playback source name as the channel.
/// Hook your Jellyfin playback events into <see cref="OnPlaybackStartAsync"/> and
/// <see cref="OnPlaybackStopAsync"/>.
/// </summary>
public sealed class PlaybackWebhookForwarder : IDisposable
{
    private const string PingPath = "/api/ping/";
    private const string WebhookPath = "/api/jellyfin/webhook";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly Regex ValidChannelNameRegex = new(
        "^[a-z0-9_]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LivePathChannelRegex = new(
        @"/live/([A-Za-z0-9_]{1,64})/index\.m3u8(?:$|\?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly ILogger<PlaybackWebhookForwarder>? _logger;
    private readonly PluginConfiguration _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackWebhookForwarder"/> class.
    /// </summary>
    /// <param name="settings">Plugin configuration settings.</param>
    /// <param name="logger">Optional logger instance.</param>
    public PlaybackWebhookForwarder(PluginConfiguration settings, ILogger<PlaybackWebhookForwarder>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    /// <summary>
    /// Extracts a jellych channel name from a live HLS URL or path (e.g., <c>/live/channel/index.m3u8</c>).
    /// </summary>
    /// <param name="pathOrUrl">Stream path or URL.</param>
    /// <param name="channel">Normalized lowercase channel name if matched.</param>
    /// <returns><c>true</c> if the path matched a jellych live stream path; otherwise, <c>false</c>.</returns>
    public static bool TryExtractChannelFromLivePath(string? pathOrUrl, out string channel)
    {
        channel = string.Empty;
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return false;
        }

        var pathMatch = LivePathChannelRegex.Match(pathOrUrl);
        if (!pathMatch.Success)
        {
            return false;
        }

        channel = pathMatch.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Extracts and validates a jellych channel name from a raw source name or stream URL/path.
    /// </summary>
    /// <param name="candidate">Raw channel name or stream path.</param>
    /// <param name="channel">Normalized lowercase channel name if valid.</param>
    /// <returns><c>true</c> if a valid channel name was extracted; otherwise, <c>false</c>.</returns>
    public static bool TryExtractChannelName(string? candidate, out string channel)
    {
        if (TryExtractChannelFromLivePath(candidate, out channel))
        {
            return true;
        }

        channel = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim().ToLowerInvariant();
        if (!ValidChannelNameRegex.IsMatch(normalized))
        {
            return false;
        }

        channel = normalized;
        return true;
    }

    /// <summary>
    /// Sends a playback start webhook to jellych.
    /// </summary>
    /// <param name="sourceName">Playback source or channel name.</param>
    /// <param name="sessionId">Stable Jellyfin session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OnPlaybackStartAsync(string sourceName, string sessionId, CancellationToken cancellationToken = default)
        => SendAsync("start", sourceName, sessionId, cancellationToken);

    /// <summary>
    /// Sends a playback stop webhook to jellych.
    /// </summary>
    /// <param name="sourceName">Playback source or channel name.</param>
    /// <param name="sessionId">Stable Jellyfin session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OnPlaybackStopAsync(string sourceName, string sessionId, CancellationToken cancellationToken = default)
        => SendAsync("stop", sourceName, sessionId, cancellationToken);

    /// <summary>
    /// Pings the jellych server to verify connectivity.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        var pingUri = BuildApiUri(_settings.TargetUrl, PingPath);
        using var req = new HttpRequestMessage(HttpMethod.Get, pingUri);

        var secret = _settings.SharedSecret?.Trim();
        if (!string.IsNullOrEmpty(secret))
        {
            req.Headers.Add("X-Jellych-Secret", secret);
        }

        _logger?.LogInformation("Pinging jellych API: GET {PingUri}", pingUri);
        JellychPluginLog.Write($"Pinging jellych API: GET {pingUri}");

        using var timeoutCts = CreateTimeoutTokenSource(cancellationToken);

        try
        {
            using var resp = await SharedHttpClient.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            var responseBody = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            var responseMessage = "ok";

            if (!string.Equals(responseBody.Trim(), "pong", StringComparison.Ordinal))
            {
                responseMessage = $"unexpected response={responseBody}";
            }

            _logger?.LogInformation(
                "Jellych API ping response: {StatusCode} {ReasonPhrase} {ResponseMessage}",
                (int)resp.StatusCode,
                resp.ReasonPhrase,
                responseMessage);
            JellychPluginLog.Write($"Jellych API ping response: {(int)resp.StatusCode} {resp.ReasonPhrase} {responseMessage}");

            _ = resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Jellych API ping failed: GET {PingUri}", pingUri);
            JellychPluginLog.Write($"Jellych API ping failed: GET {pingUri} error={ex}");
            throw;
        }
    }

    private async Task SendAsync(string action, string sourceName, string sessionId, CancellationToken cancellationToken)
    {
        if (!TryExtractChannelName(sourceName, out var channel))
        {
            _logger?.LogDebug("Skipping {Action} webhook because playback source '{SourceName}' is not a valid jellych channel name", action, sourceName);
            JellychPluginLog.Write($"Skipping {action} webhook because playback source '{sourceName}' is not a valid jellych channel name");
            return;
        }

        var webhookUri = BuildApiUri(_settings.TargetUrl, WebhookPath);
        var payload = new WebhookPayload
        {
            Action = action,
            Channel = channel,
            SessionId = sessionId,
        };
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        using var req = new HttpRequestMessage(HttpMethod.Post, webhookUri)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };

        var secret = _settings.SharedSecret?.Trim();
        if (!string.IsNullOrEmpty(secret))
        {
            req.Headers.Add("X-Jellych-Secret", secret);
        }

        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger?.LogInformation(
            "Sending webhook: POST {WebhookUri} action={Action} channel={Channel} session={SessionId} body={PayloadJson}",
            webhookUri,
            action,
            channel,
            sessionId,
            payloadJson);
        JellychPluginLog.Write($"Sending webhook: POST {webhookUri} action={action} channel={channel} session={sessionId} body={payloadJson}");

        using var timeoutCts = CreateTimeoutTokenSource(cancellationToken);

        try
        {
            using var resp = await SharedHttpClient.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            var responseBody = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _logger?.LogInformation(
                    "Webhook ignored by jellych (channel not configured): action={Action} channel={Channel} session={SessionId}",
                    action,
                    channel,
                    sessionId);
                JellychPluginLog.Write($"Webhook ignored by jellych (channel not configured): action={action} channel={channel} session={sessionId}");
                return;
            }

            _logger?.LogInformation(
                "Webhook response: {StatusCode} {ReasonPhrase}",
                (int)resp.StatusCode,
                resp.ReasonPhrase);
            JellychPluginLog.Write($"Webhook response: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            _ = resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Webhook request failed: POST {WebhookUri} action={Action} channel={Channel} session={SessionId}",
                webhookUri,
                action,
                channel,
                sessionId);
            JellychPluginLog.Write($"Webhook request failed: POST {webhookUri} action={action} channel={channel} session={sessionId} error={ex}");
            throw;
        }
    }

    private CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken)
    {
        var timeoutSeconds = _settings.RequestTimeoutSeconds > 0 ? _settings.RequestTimeoutSeconds : 3;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return cts;
    }

    private static string BuildApiUri(string serverUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return path;
        }

        var trimmedServerUrl = serverUrl.Trim();
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";

        if (Uri.TryCreate(trimmedServerUrl, UriKind.Absolute, out var baseUri))
        {
            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var builder = new UriBuilder(baseUri)
            {
                Path = string.IsNullOrEmpty(basePath) ? normalizedPath : $"{basePath}{normalizedPath}",
            };

            return builder.Uri.ToString();
        }

        return $"{trimmedServerUrl.TrimEnd('/')}{normalizedPath}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // SharedHttpClient is reused across the plugin lifetime to avoid socket exhaustion.
    }
}

/// <summary>
/// Payload sent to the jellych Jellyfin webhook endpoint.
/// </summary>
public sealed class WebhookPayload
{
    /// <summary>
    /// Gets or sets the playback action ("start" or "stop").
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized Twitch channel name.
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin playback session identifier.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
}
