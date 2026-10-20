using System;
using System.Globalization;
using System.IO;

namespace Jellych.WebhookPlugin;

internal static class JellychPluginLog
{
    private static readonly object SyncLock = new();

    public static void Write(string message)
    {
        try
        {
            var logDirectory = Plugin.LogDirectoryPath;
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                return;
            }

            lock (SyncLock)
            {
                Directory.CreateDirectory(logDirectory);
                var logPath = Path.Combine(logDirectory, "jellych-plugin.log");
                var timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
                File.AppendAllText(logPath, $"{timestamp} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break playback event handling.
        }
    }
}
