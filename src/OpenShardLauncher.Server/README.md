# OpenShardLauncher feed server

A plain static host for a feed folder produced by the [Publisher](../../tools/OpenShardLauncher.Publisher/README.md). It builds, hashes and signs nothing. Any static host or CDN can serve the same folder instead (see below).

- Serves only the feed layout: `/files.json`, `/files.sig`, `/blobs/**` and `/packages/**`. Anything else in the folder returns 404. Paths are case-sensitive.
- Uses ASP.NET's static files: Range/`If-Range`, ETag/`Last-Modified`/304, HEAD, no directory listing, no paths outside the folder. Hidden and system files are never served.
- `Cache-Control: public, max-age=31536000, immutable` on blobs, and `no-cache` on everything else.
- `/health` returns only the status.
- `/latest/{role}/{rid}` is a link that never changes, for your website or Discord. For example, `/latest/launcher/win-x64` redirects (302, `no-cache`) to the newest `launcher-*.win-x64.zip` in `packages/manifest.json`, and `/latest/tazuo/win-x64` does the same for TazUO. A role or platform the manifest doesn't list returns 404. The manifest is reread on every request, so a new publish shows up without a restart.
- No CORS (the launcher isn't a browser client).
- **Symlinks inside the feed folder are followed.** The folder is the operator's own; don't link to anything you don't want served.

## Configuration (`appsettings.json`)

```json
{
  "Server": {
    "FeedDirectory": "feed",
    "Compression": false,
    "RateLimiting": { "Enabled": false, "ConcurrentRequestsPerClient": 8, "QueueLimit": 32 },
    "ForwardedHeaders": { "Enabled": false, "KnownProxies": [], "KnownNetworks": [] }
  },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:8080" } } }
}
```

- `appsettings.json` is read from the server's own folder. Relative paths (`FeedDirectory`, log files) resolve against that folder, wherever the server is started from.
- **Unknown keys under `Server` stop the server at startup**, so a typo can't silently leave a setting at its default. A missing feed folder stops it too.
- Endpoints and HTTPS certificates use ASP.NET's standard [`Kestrel` section](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints).
- `Compression` gzips/brotlis only `files.json` and `manifest.json`. Blobs and zips are never compressed.
- Logging is Serilog (console + daily rolling file in `logs/`), configured in the `Serilog` section.

### Rate limiting (optional, off by default)

`RateLimiting:Enabled` limits how many requests one client (an IPv4 address, or an IPv6 /64) can have in progress at once. Extra requests wait in a queue of `QueueLimit` and then get 429. CDNs and reverse proxies usually rate-limit already, so it's off by default.

### Forwarded headers (optional, off by default)

You only need this when **rate limiting is on and the server sits behind a proxy**. Without it, every player appears to come from the proxy's IP and they all share one bucket. Only proxies you list are trusted. Trusting `X-Forwarded-For` from anyone would let clients pick their own IP and bypass the limit. Loopback is always trusted.

**nginx on the same machine:**

```json
"ForwardedHeaders": { "Enabled": true }
```

```nginx
location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_set_header X-Forwarded-For $remote_addr;   # replace, don't append, so clients can't inject an address
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

**Cloudflare in front of the server:** list Cloudflare's published ranges (https://www.cloudflare.com/ips/) and make sure only Cloudflare can reach the server (firewall).

```json
"ForwardedHeaders": {
  "Enabled": true,
  "KnownNetworks": [ "173.245.48.0/20", "103.21.244.0/22", "2400:cb00::/32" ]
}
```

With Cloudflare *and* nginx, use nginx's `real_ip` module (`set_real_ip_from <Cloudflare ranges>; real_ip_header CF-Connecting-IP;`) and pass `$remote_addr` on as above. The server then only needs to trust nginx.

## Uploading a new feed

Either stop the server while uploading, or upload in three passes: (1) `blobs/` and `packages/` without deleting, (2) `packages/manifest.sig`, `packages/manifest.json`, `files.sig`, `files.json`, (3) delete pruned files. See [feed format](../../docs/feed-format.md#uploading-safely).

## Using another static host instead

Any host that serves files with Range support works. Upload the feed folder as-is and set:

| Path | Content-Type | Cache-Control |
|---|---|---|
| `/blobs/**` | `application/octet-stream` | `public, max-age=31536000, immutable` |
| `/files.json`, `/packages/manifest.json` | `application/json` | `no-cache` |
| `/files.sig`, `/packages/manifest.sig`, `/packages/*.zip` | `application/octet-stream` | `no-cache` |

**nginx:**

```nginx
server {
    root /srv/feed;
    autoindex off;
    location ~ ^/blobs/[0-9a-f]{2}/[0-9a-f]{64}$ {
        default_type application/octet-stream;
        add_header Cache-Control "public, max-age=31536000, immutable";
    }
    location ~ ^/(files\.(json|sig)|packages/[^/]+)$ {
        types { application/json json; }
        default_type application/octet-stream;
        add_header Cache-Control "no-cache";
    }
    location / { return 404; }
}
```

**S3 / Cloudflare R2 / other object storage:** blobs have no file extension, so set metadata on upload. For example, with rclone:

```
rclone copy feed/blobs remote:bucket/blobs --header-upload "Cache-Control: public, max-age=31536000, immutable" --header-upload "Content-Type: application/octet-stream"
rclone copy feed/packages remote:bucket/packages --header-upload "Cache-Control: no-cache"
rclone copy feed remote:bucket --include "/files.*" --header-upload "Cache-Control: no-cache"
```

`/latest/...` links need the included server. On another host, link to your GitHub release instead, or add a redirect rule there (e.g. nginx `location = /latest/launcher/win-x64 { return 302 /packages/launcher-1.2.0.win-x64.zip; }`) and update it with each release.

Keep the same upload order (blobs and packages, then the manifest, then `files.sig`/`files.json`, then deletes). Make sure the CDN doesn't cache `files.json` or the signatures for long, or players see new versions late.
