using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellych.WebhookPlugin.Configuration;

namespace Jellych.WebhookPlugin;

/// <summary>
/// Jellych Jellyfin plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        LogDirectoryPath = applicationPaths.LogDirectoryPath;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the Jellyfin log directory path.
    /// </summary>
    public static string? LogDirectoryPath { get; private set; }

    /// <inheritdoc />
    public override string Name => "Jellych Webhook";

    /// <inheritdoc />
    public override Guid Id => new("a1f2b3c4-d5e6-4f7a-8b9c-1d2e3f4a5b6c");

    /// <inheritdoc />
    public override string Description => "Forwards playback start/stop events to jellych server";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
        },
    };
}
