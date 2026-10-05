# OpenShardLauncher

A game launcher for Ultima Online shards, with its own update feed. Players get one small window that downloads and verifies the shard's game files and then starts the game client (the TazUO launcher by default, or any pre-configured TazUO/ClassicUO exe). Operators publish updates from their own machine with a command-line tool and upload the result to any static web host.

It's a rewrite of [Memento-FileUpdater](https://github.com/Jascen/Memento-FileUpdater) (.NET 10, Avalonia 12) and is meant to be forked: a shard changes a few configuration files and art, never C# code.

| Part | What it does |
|---|---|
| **Launcher** (`src/OpenShardLauncher.Client`) | Compares the install folder with the signed file list, downloads what differs (resuming, hash-checked), deletes what the feed removed, installs/updates the game client and updates itself. Windows, Linux and macOS. |
| **Publisher** (`tools/OpenShardLauncher.Publisher`) | Builds and signs the whole feed locally: `files.json`, content-addressed blobs, package zips and their manifest. [README](tools/OpenShardLauncher.Publisher/README.md) |
| **Feed server** (`src/OpenShardLauncher.Server`) | A plain static host for the feed folder. Optional: nginx, a CDN or object storage work just as well. [README](src/OpenShardLauncher.Server/README.md) |

**Security model.** The feed is signed with an ECDSA P-256 key that stays on the operator's machine. The launcher only trusts the public keys compiled into it, so neither the web server, a mirror nor someone on the network can change game files or push a launcher update. Details: [feed format](docs/feed-format.md), [self-update and stable contracts](docs/self-update.md), [ignore rules](docs/ignore-rules.md).

Licence: [MIT](LICENSE).

---

## Contents

- [Build and run locally](#build-and-run-locally)
- [Making it your shard's launcher](#making-it-your-shards-launcher)
- [Publishing updates](#publishing-updates)
- [Hosting the feed](#hosting-the-feed)
- [Releasing the launcher](#releasing-the-launcher)
- [Unsigned binaries: SmartScreen, Smart App Control, Gatekeeper](#unsigned-binaries-smartscreen-smart-app-control-gatekeeper)
- [Template: instructions for your players](#template-instructions-for-your-players)
- [Troubleshooting](#troubleshooting)

---

## Build and run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`).

```bash
dotnet build
dotnet test
```

A full local loop is Publisher → feed → server → launcher. From the repository root:

**1. Create a test key** (any folder outside the repository; the Publisher refuses keys inside `--source`):

```bash
dotnet run --project tools/OpenShardLauncher.Publisher -- keygen --out ../dev-keys
```

It prints a line like `{"alg":"p256","key":"MFkw..."}` and asks for a password (or set `PUBLISHER_KEY_PASSWORD`).

**2. Give a Debug launcher that key.** Create `src/OpenShardLauncher.Client/launcher.local.json` (gitignored, never commit it):

```json
{
  "TrustedPublicKeys": [ { "alg": "p256", "key": "MFkw..." } ]
}
```

Its objects are merged into `launcher.json`; any other value (such as the `TrustedPublicKeys` list) replaces the one there. Instead of a key you can use `{ "AllowUnsignedFeed": true }` and publish with `--unsigned`. **Only Debug builds read `launcher.local.json`**; a Release build always uses the embedded `launcher.json` alone.

**3. Publish a feed** from a folder of game files (and optionally a folder of package zips):

```bash
dotnet run --project tools/OpenShardLauncher.Publisher -- publish --source ../dev-game --packages ../dev-packages --out ../dev-feed --key ../dev-keys/feed-signing.key
```

**4. Serve it.** The server's default binding is `0.0.0.0:8080`, which makes Windows Firewall ask for permission. For local testing bind to loopback instead. `FeedDirectory` is relative to the server's own folder (`bin/...`), so pass an absolute path:

```bash
dotnet run --project src/OpenShardLauncher.Server -- --Kestrel:Endpoints:Http:Url=http://127.0.0.1:8080 --Server:FeedDirectory=C:/path/to/dev-feed
```

**5. Run the launcher.** The upstream `UpdateUrl` is already `http://127.0.0.1:8080/`.

```bash
dotnet run --project src/OpenShardLauncher.Client
```

The launcher installs into a `Game/` folder next to its exe and keeps its own data in `.openshardlauncher/` there (settings, feed state, logs).

---

## Making it your shard's launcher

A fork changes **only these files**. Everything named `OpenShardLauncher.*` (projects, namespaces, the repository) stays as it is, so pulling upstream fixes stays easy.

| File | What you change |
|---|---|
| `src/OpenShardLauncher.Client/Resources/launcher.json` | `Title`, `Subtitle`, `UpdateUrl`, `TrustedPublicKeys`, `AllowUnsignedFeed`, nav `Links`, `KeepLocalPatterns`, `AppDataFolderName`, `DefaultInstallFolder`, `Client` (`Enabled`, `InstallFolder`, `ExecutableName`, `Arguments`, `TazUOProfiles`), `PackageCheckInterval` |
| `Directory.Build.props` | `<LauncherExeName>` (the exe players run), optionally `<LauncherIcon>` |
| `src/OpenShardLauncher.Client/Themes/Branding.axaml` | Colors, brushes, fonts |
| `src/OpenShardLauncher.Client/Assets/` | `background.png`, `play-button.png`, `progress-*.png` (keep each image's size) and `icon.ico` (a real .ico) |
| `src/OpenShardLauncher.Server/appsettings.json` | Feed folder, port, certificates, optional rate limiting. Not needed with another static host |

### launcher.json

Change only what you need: **a property you leave out keeps its default** (the upstream values below). Comments and trailing commas are allowed.

```jsonc
{
  "Title": "My Shard",                         // shown big in the window; a long title shrinks to fit
  "Subtitle": "Stay up to date with the latest files",
  "UpdateUrl": "https://updates.example.com/", // the feed's root; players can override it in Settings (mirrors)
  "TrustedPublicKeys": [                       // from `publisher keygen`: your main key and an offline backup key
    { "alg": "p256", "key": "MFkw..." },
    { "alg": "p256", "key": "MFkw..." }
  ],
  "AllowUnsignedFeed": false,
  "Links": [
    { "Text": "Website", "Url": "https://example.com/" },
    { "Text": "Verify", "Url": "verify" }      // "verify" re-checks every file instead of opening a page
  ],
  "KeepLocalPatterns": [ "*.cfg" ],            // files players change: downloaded only when missing, never replaced
  "AppDataFolderName": "MyShard",              // settings folder under AppData when the launcher folder isn't writable
  "DefaultInstallFolder": "Game",              // next to the exe, until the player picks another
  "Client": {                                 // what Play starts
    "Enabled": true,
    "InstallFolder": "TazUO",                  // subfolder of the install folder; a client-*.zip package unpacks here
    "ExecutableName": "TazUOLauncher",         // in InstallFolder; ".exe" is added on Windows
    "Arguments": [],                           // passed to it; "{GameFolder}" and "{ClientFolder}" are replaced
    "TazUOProfiles": [                         // TazUO launcher only: created when missing; players' own changes are kept
      { "Id": "myshard", "Name": "My Shard", "Ip": "play.example.com", "Port": 2593, "ClientVersion": "7.0.15.1" }
    ]
  },
  "PackageCheckInterval": "04:00:00"           // how often a running launcher looks for a launcher update
}
```

Upstream defaults: title `OpenShardLauncher`, exe `OpenShardLauncher`, `UpdateUrl` `http://127.0.0.1:8080/`, **no keys**, `AllowUnsignedFeed` false, AppData folder `OpenShardLauncher`, the TazUO launcher in `Game/TazUO` with one neutral profile `openshard-local` at `127.0.0.1:2593`. A fresh clone builds and starts, and then shows an error that it has no keys until you add one.

**Choose these once.** After players have your launcher, keep `<LauncherExeName>`, `AppDataFolderName` and each `TazUOProfiles` `Id`: self-update looks for an exe of the same name, and settings and profiles are found by those names. See [stable contracts](docs/self-update.md).

### Using another client (ClassicUO, TazUO without its launcher)

The launcher doesn't need the TazUO launcher. Play starts `Client.ExecutableName` in `Client.InstallFolder`, wherever it came from:

- **In the game files.** Put the client folder (e.g. `ClassicUO/` with a pre-configured `settings.json`) into `--source`. It updates like every other file. Add the client's own settings file to `KeepLocalPatterns` if players change it.
- **As a package.** Publish it as `client-{version}.{rid}.zip` (one per platform). It is unpacked into `Client.InstallFolder` and updated when a newer version is published.

Then set `ExecutableName`, pass connection settings with `Arguments` if the client isn't configured by its own files, and leave `TazUOProfiles` empty:

```jsonc
"Client": {
  "InstallFolder": "ClassicUO",
  "ExecutableName": "ClassicUO",
  "Arguments": [ "-uopath", "{GameFolder}", "-ip", "play.example.com", "-port", "2593" ],
  "TazUOProfiles": []
}
```

With `Arguments` the client is started directly; without them it is started like a double-click (on macOS through `open`).

### Release-build warnings

A Release build of the launcher warns (and still succeeds) when:

| Code | Meaning |
|---|---|
| `OSL0001` | `TrustedPublicKeys` is empty and `AllowUnsignedFeed` is false. Such a launcher can never update: it shows an error at startup. |
| `OSL0002` | `<LauncherExeName>` or the `launcher.json` title is still the upstream `OpenShardLauncher`. |
| `OSL0003` | `launcher.json` couldn't be read, so it wasn't checked. |

Upstream builds always show `OSL0001` and `OSL0002`; a configured fork shows none.

### Signing keys

- `publisher keygen` writes a password-protected private key (`feed-signing.key`) and the public key. Keep the private key **offline and out of the repository, the server and CI**. It can't be revoked and doesn't expire.
- Generate a **backup key** at the same time, store it separately and trust it from the first release, so losing the main key doesn't strand your players.
- To change keys later: release a launcher that trusts both keys (signed with the old one), wait a long time, then sign with the new key (`--key` can be given twice to sign with both during the overlap). See section "Signatures" in [feed format](docs/feed-format.md#signatures-filessig-manifestsig).
- **Unsigned feeds** (`AllowUnsignedFeed: true` + `publisher publish --unsigned`) are for operators who don't want to manage a key. The launcher then accepts a feed without signatures, but only from its default `UpdateUrl` and only over https (or loopback), and it shows a permanent notice to players. A signature that is present but wrong is always rejected.

---

## Publishing updates

On the machine that holds the key:

```bash
publisher publish --source <game files> --packages <zips> --out <feed> --key <feed-signing.key> [--key <backup.key>]
```

(`publisher` is `OpenShardLauncher.Publisher` from the release's `publisher-*.zip`, or `dotnet run --project tools/OpenShardLauncher.Publisher --`.)

- `--source` is the folder players should end up with. Only changed files are hashed and copied. Removed files go into a signed `removed` list, so launchers delete exactly those (never the player's own files).
- `--packages` holds `launcher-{version}.{rid}.zip` and `client-{version}.{rid}.zip` (the game client: TazUO launcher, ClassicUO, ...). The newest per role and platform is published. Launcher zips come from the [release](#releasing-the-launcher) as-is.
- The summary lists every **new** file name. Read it before uploading, so a private file (accounts, configs) doesn't slip out. `.publishignore` and default exclusions are in [ignore rules](docs/ignore-rules.md).
- `publisher verify --feed <feed> --pub <feed-signing.pub.json>` checks a feed the way a launcher does.

Full reference: [Publisher README](tools/OpenShardLauncher.Publisher/README.md).

### Uploading a feed

Upload the output folder to your host. Blobs never change once written, so only new ones transfer.

- **Use a tool that writes to a temporary name and renames when done** (rclone and rsync do by default), so a blob is never visible half-written.
- **Upload order:** either stop the web server while uploading, or upload in three passes: (1) `blobs/` and `packages/` without deleting, (2) `packages/manifest.sig`, `packages/manifest.json`, `files.sig`, `files.json`, (3) delete pruned files. See [uploading safely](docs/feed-format.md#uploading-safely).
- A launcher that meets a feed mid-upload (a missing or still-growing blob, a list whose signature doesn't match yet) shows "the server is updating, try again shortly", keeps what it already downloaded and resumes later.

---

## Hosting the feed

Any static host with HTTP Range support works: the included server, nginx, Caddy, S3/R2 behind a CDN. Required headers and nginx/rclone examples are in the [server README](src/OpenShardLauncher.Server/README.md#using-another-static-host-instead).

**The included server** (`server-{version}.{rid}.zip`, Windows and Linux): unzip, edit `appsettings.json` (`Server:FeedDirectory`, the `Kestrel` endpoint and certificate), run `OpenShardLauncher.Server`. `/health` reports status only. `/latest/launcher/<rid>` (e.g. `https://updates.example.com/latest/launcher/win-x64`) always downloads the newest launcher in the feed, so you can put it on your website. Use https for anything public.

**Rate limiting.** Launchers pause *all* their requests to the server when they get a **429 or 503**, and honour `Retry-After` (seconds or a date, up to 2 minutes; 5 seconds without it). A server or CDN that rate-limits should send `Retry-After`. The included server has an optional per-client concurrency limit (`Server:RateLimiting`, off by default).

**Behind a proxy or CDN.** Only matters when the included server's rate limiting is on: without forwarded headers every player appears to come from the proxy's IP and they share one bucket. Turn on `Server:ForwardedHeaders` and list only your proxies (`KnownProxies`/`KnownNetworks`), never trust `X-Forwarded-For` from everyone. nginx and Cloudflare examples: [server README](src/OpenShardLauncher.Server/README.md#forwarded-headers-optional-off-by-default).

---

## Releasing the launcher

Releases are built by `.github/workflows/release.yml`; feeds are signed only on your machine. The private key never exists in CI.

1. **Tag:** `git tag v1.2.0 && git push origin v1.2.0`. The version must be numeric (2–4 parts): a launcher whose version isn't, such as `1.2.0-beta`, is never offered a self-update, so the workflow refuses such tags. (A manual run of the workflow builds a version you type in and only uploads workflow artifacts.)
2. **CI** runs the tests and publishes single-file, self-contained builds into a **draft** GitHub release, with `SHA256SUMS.txt`:
   - `launcher-{version}.{rid}.zip` for `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`: the exe (`<LauncherExeName>`) at the zip root with its native libraries. These are also the self-update packages.
   - `publisher-{version}.{rid}.zip` for the same platforms; `server-{version}.{rid}.zip` for `win-x64` and `linux-x64`.
   - macOS builds run on a macOS runner so the SDK ad-hoc signs them (Apple Silicon won't run unsigned code); CI checks them with `codesign --verify`.
3. **Scan** the zips (VirusTotal, and Windows Defender on a clean VM) and record the results in [docs/releasing.md](docs/releasing.md). Builds are plain .NET single-file publishes (no compression, packers or obfuscators) to keep false positives rare.
4. **Publish the feed:** copy the launcher zips into your `--packages` folder and run `publisher publish ... --key ...` locally (or `--unsigned` for a launcher built with `AllowUnsignedFeed`). Upload. Running launchers offer the update within `PackageCheckInterval` or on their next start.
5. **Publish the GitHub release** so new players can download the launcher.

The full checklist, including the manual self-update test matrix, is in [docs/releasing.md](docs/releasing.md).

---

## Unsigned binaries: SmartScreen, Smart App Control, Gatekeeper

Releases are **not** code-signed (no Authenticode, no Apple notarization). Players see a warning the first time, and in one case Windows blocks the launcher outright. Self-update doesn't trigger new prompts: the update is unpacked by the launcher itself, so it carries no "downloaded from the internet" mark.

**Windows SmartScreen** ("Windows protected your PC"): click **More info → Run anyway**. Shown once per downloaded file.

**Windows 11 Smart App Control.** When Smart App Control is **on**, Windows blocks unsigned apps that it doesn't know, with no "run anyway" button and no per-app exception. The only way to run the launcher is to turn Smart App Control off: **Settings → Privacy & security → Windows Security → App & browser control → Smart App Control settings → Off**. (Since the April 2026 update it can be turned back on later without reinstalling Windows.) Players who don't want that can't use an unsigned launcher; tell them so up front.

**Microsoft Defender false positive.** If Defender (or another antivirus) flags a release, submit the file as a false positive at <https://www.microsoft.com/wdsi/filesubmission> ("Software developer", incorrectly detected), and let players know it's being reviewed. Don't tell players to turn their antivirus off; at most, to allow the launcher's folder.

**macOS Gatekeeper.** The launcher is ad-hoc signed but not notarized. On first start macOS says it "can't be opened" / can't verify the developer. Either open **System Settings → Privacy & Security** and click **Open Anyway** after the first attempt, or remove the quarantine flag in Terminal:

```bash
xattr -dr com.apple.quarantine ~/Games/MyShard
```

**macOS App Translocation:** a quarantined launcher started from `Downloads` runs from a hidden read-only copy and can't update itself (the launcher says so). Move the unzipped folder somewhere else (e.g. `~/Games/`) before starting it.

**Linux:** unzip and run the exe; if the executable bit got lost, `chmod +x MyShard`.

---

## Template: instructions for your players

Copy, replace the names and links, and put it on your website or Discord.

> ### Installing the My Shard launcher
>
> 1. Download the launcher for your system from **<link to your release page>**:
>    Windows: `launcher-X.Y.Z.win-x64.zip` · Linux: `launcher-X.Y.Z.linux-x64.zip` · Mac (Apple Silicon): `launcher-X.Y.Z.osx-arm64.zip` · Mac (Intel): `launcher-X.Y.Z.osx-x64.zip`
> 2. Unzip it into a folder **you own**, such as `Documents\MyShard` or `~/Games/MyShard`. Not `Program Files` and not your `Downloads` folder: the launcher updates itself and needs to write there.
> 3. Start `MyShard`. The game is downloaded into the `Game` folder next to it. Press **Play** when it's done.
>
> **"Windows protected your PC"?** Click *More info → Run anyway*. The launcher isn't code-signed yet; this appears only once.
> **Blocked by Smart App Control?** Windows 11's Smart App Control blocks unsigned apps entirely. You'd have to turn it off (Windows Security → App & browser control → Smart App Control). If you'd rather not, ask us for help on <Discord>.
> **Mac says it can't be opened?** Open *System Settings → Privacy & Security* and click *Open Anyway*.
> **Antivirus warning?** It's a false positive we've reported. Our files' checksums are listed on the release page.
>
> The launcher keeps itself and the game up to date and checks every file before you play. Your own settings files are kept. To keep the launcher from touching certain files, add them to the ignore list in *Settings*.
> **Something wrong?** Send us the newest file from the `.openshardlauncher/logs/` folder next to the launcher.

---

## Troubleshooting

- **Launcher log:** `.openshardlauncher/logs/launcher-*.log` next to the exe (or under `%AppData%/<AppDataFolderName>/logs/` when the launcher folder isn't writable). It records the version, the update server, how many trusted keys the build has, "Feed signatures are not required" when `AllowUnsignedFeed` is on, and the no-keys error.
- **"The server is updating" for a long time:** the feed is mid-upload, or the feed is signed with a key this launcher doesn't trust (a mirror signed by someone else looks the same). Check `TrustedPublicKeys` against `publisher verify`.
- **Server log:** `logs/server-*.log` next to the server exe.
