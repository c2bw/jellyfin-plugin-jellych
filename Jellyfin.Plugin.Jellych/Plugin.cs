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
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        LogDirectoryPath = applicationPaths.LogDirectoryPath;
    }

    public static Plugin? Instance { get; private set; }

    public static string? LogDirectoryPath { get; private set; }

    public override string Name => "Jellych Webhook";

    public override Guid Id => new("a1f2b3c4-d5e6-4f7a-8b9c-1d2e3f4a5b6c");

    public override string Description => "Forwards playback start/stop events to jellych server";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
        },
    };
}
