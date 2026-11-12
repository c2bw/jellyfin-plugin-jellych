# Jellyfin Plugin: Jellych Webhook

Forward Jellyfin playback events to [jellych](https://github.com/c2bw/jellych) server for automatic stream tracking and management.

## Features

- **Playback Tracking** — Automatically reports when playback starts/stops
- **Secure** — Shared-secret authentication (`X-Jellych-Secret` header)

## Compatibility

- **Jellyfin**: 12.0+ (10.11.8+ supported via release 1.0.0.1 in repository manifest)
- **.NET**: 10.0+
- **OS**: Windows, Linux, macOS (plugin runs on Jellyfin server)

## Installation

### Install via Jellyfin Repository URL (**recommended**)

If you do not want to manually copy plugin files, use a custom repository URL in Jellyfin (**recommended**).

Repository manifest URL:

- `https://raw.githubusercontent.com/c2bw/jellyfin-plugin-jellych/main/manifest.json`

Then in Jellyfin admin, open **Dashboard -> Plugins -> Repositories**, add that URL, save, and install **Jellych Webhook** from Catalog.

#### Steps

1. Go to Dashboard -> Plugins -> Repositories
2. Add repository URL:
   - `https://raw.githubusercontent.com/c2bw/jellyfin-plugin-jellych/main/manifest.json`
3. Save
4. Go to Catalog, find **Jellych Webhook**, and install/update
5. Restart Jellyfin when prompted
6. Configure plugin (Plugins -> Jellych Webhook -> Configuration)

### Configuration Fields

| Field                   | Description                                                                           |
| ----------------------- | ------------------------------------------------------------------------------------- |
| `targetUrl`             | Jellych server base URL, including optional port (e.g., `http://jellych-server:8080`) |
| `sharedSecret`          | Shared secret — must match `JELLYFIN_WEBHOOK_SECRET` on jellych server                |
| `requestTimeoutSeconds` | HTTP timeout for webhook requests (default 3s)                                        |
 
## How It Works

1. **Playback Start** → Plugin detects event → Sends webhook with the playback source name
2. **Webhook sent** → `POST /api/jellyfin/webhook` with `X-Jellych-Secret` header
3. **jellych server** → Records active session → Tracks viewer idle time
4. **Playback Stop** → Plugin detects → Sends stop webhook with the same source name → jellych removes session

## Playback startup and proxy mode

Webhook requests are ordered per Jellyfin session. A slow request for one viewer
no longer blocks events for other viewers. Channel changes and late stop events
retain their session ordering.

With a matching updated Jellych server, you can try the tuner URL
`http://your-jellych-server:8080/api/twitch.m3u?playback=proxy` and refresh the guide.
The plugin recognizes `/api/live-proxy/<channel>/index.m3u8` and includes
`playbackMode: "proxy"` in its start webhook. Jellych records the viewer without
starting its own FFmpeg remux. The default `/api/twitch.m3u` tuner URL retains
local remux playback; change back to it and refresh the guide to revert.
Update the plugin as well as Jellych before trying proxy mode.

## Troubleshooting

### Webhook not sent
- Check jellych server is reachable and webhook endpoint is accessible
- Verify `sharedSecret` matches jellych's `JELLYFIN_WEBHOOK_SECRET`

### 403 Unauthorized
- Ensure `sharedSecret` in config **exactly** matches jellych server's secret
- Restart Jellyfin and jellych after changing secrets

## Development

### Build from Source

**Prerequisites**: .NET 10 SDK

```bash
git clone https://github.com/c2bw/jellyfin-plugin-jellych.git
cd jellyfin-plugin-jellych

# Windows
build.bat

# Linux/macOS
./build.sh
```

Output: `Jellyfin.Plugin.Jellych/bin/Release/net10.0/Jellych.WebhookPlugin.dll`

Run the session ordering and playback mode regression tests with:

```sh
dotnet test Jellyfin.Plugin.Jellych.sln -c Release
```

For repository publishing automation (build + zip + checksum + manifest update):

```powershell
.\release.ps1
```

## License

[MIT License](LICENSE) - See LICENSE file

## Contributing

Contributions welcome! Please open issues or PRs.

## References

- [jellych server](https://github.com/c2bw/jellych)
- [Jellyfin plugin docs](https://jellyfin.org/docs/general/server/plugins/)
- [Jellyfin 10.11.8 release](https://github.com/jellyfin/jellyfin/releases/tag/v10.11.8)
