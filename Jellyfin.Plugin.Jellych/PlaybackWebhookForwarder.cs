using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    private readonly HttpClient _http;
    private readonly ILogger<PlaybackWebhookForwarder>? _logger;
    private readonly PluginConfiguration _settings;

    public PlaybackWebhookForwarder(PluginConfiguration settings, ILogger<PlaybackWebhookForwarder>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        var timeoutSeconds = settings.RequestTimeoutSeconds > 0 ? settings.RequestTimeoutSeconds : 3;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
    }

    public Task OnPlaybackStartAsync(string sourceName, string sessionId, CancellationToken cancellationToken = default)
        => SendAsync("start", sourceName, sessionId, pingBeforeSend: true, cancellationToken);

    public Task OnPlaybackStopAsync(string sourceName, string sessionId, CancellationToken cancellationToken = default)
        => SendAsync("stop", sourceName, sessionId, pingBeforeSend: false, cancellationToken);

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        var pingUri = BuildApiUri(_settings.TargetUrl, PingPath);
        using var req = new HttpRequestMessage(HttpMethod.Get, pingUri);

        if (!string.IsNullOrEmpty(_settings.SharedSecret))
        {
            req.Headers.Add("X-Jellych-Secret", _settings.SharedSecret);
        }

        _logger?.LogInformation("Pinging jellych API: GET {PingUri}", pingUri);
        JellychPluginLog.Write($"Pinging jellych API: GET {pingUri}");

        try
        {
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var responseBody = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task SendAsync(string action, string sourceName, string sessionId, bool pingBeforeSend, CancellationToken cancellationToken)
    {
        if (!TryMapChannel(sourceName, out var channel))
        {
            _logger?.LogWarning("Skipping {Action} webhook because playback source name is empty", action);
            JellychPluginLog.Write($"Skipping {action} webhook because playback source name is empty");
            return;
        }

        if (pingBeforeSend)
        {
            await PingAsync(cancellationToken).ConfigureAwait(false);
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
        req.Headers.Add("X-Jellych-Secret", _settings.SharedSecret);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger?.LogInformation(
            "Sending webhook: POST {WebhookUri} action={Action} channel={Channel} session={SessionId} body={PayloadJson}",
            webhookUri,
            action,
            channel,
            sessionId,
            payloadJson);
        JellychPluginLog.Write($"Sending webhook: POST {webhookUri} action={action} channel={channel} session={sessionId} body={payloadJson}");

        try
        {
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var responseBody = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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

    private bool TryMapChannel(string sourceName, out string channel)
    {
        channel = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return false;
        }

        // Use the playback source name directly as the jellych channel.
        channel = sourceName.Trim().ToLowerInvariant();
        return true;
    }

    private static string BuildApiUri(string serverUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return path;
        }

        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var baseUri))
        {
            var builder = new UriBuilder(baseUri)
            {
                Path = path,
            };

            return builder.Uri.ToString();
        }

        return $"{serverUrl.TrimEnd('/')}{path}";
    }

    public void Dispose() => _http.Dispose();
}

public sealed class WebhookPayload
{
    public string Action { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}
