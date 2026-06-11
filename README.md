# Jellyfin Plugin: Jellych Webhook

Forward Jellyfin playback events to [jellych](https://github.com/c2bw/jellych) server for automatic stream tracking and management.

## Features

- **Playback Tracking** — Automatically reports when playback starts/stops
- **Secure** — Shared-secret authentication (`X-Jellych-Secret` header)

## Compatibility

- **Jellyfin**: 10.11.8+ (tested on 10.11.8)
- **.NET**: 9.0+
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

## Troubleshooting

### Webhook not sent
- Check jellych server is reachable and webhook endpoint is accessible
- Verify `sharedSecret` matches jellych's `JELLYFIN_WEBHOOK_SECRET`

### 403 Unauthorized
- Ensure `sharedSecret` in config **exactly** matches jellych server's secret
- Restart Jellyfin and jellych after changing secrets

## Development

### Build from Source

**Prerequisites**: .NET 9 SDK

```bash
git clone https://github.com/c2bw/jellyfin-plugin-jellych.git
cd jellyfin-plugin-jellych

# Windows
build.bat

# Linux/macOS
./build.sh
```

Output: `bin/Release/net9.0/Jellych.WebhookPlugin.dll`

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
