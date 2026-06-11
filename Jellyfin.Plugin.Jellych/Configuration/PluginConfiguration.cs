using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellych.WebhookPlugin.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the jellych server base URL.
    /// </summary>
    public string TargetUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Gets or sets the shared secret header value.
    /// </summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 3;

    // No channel mapping required; plugin sends the playback source directly as the channel.
}
