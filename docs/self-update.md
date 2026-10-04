# Self-update and other stable contracts

Nothing has been released yet, so everything here can still change. **After the first public release these are stable contracts:** installed launchers depend on them, and the *old* launcher starts the *new* version's exe, so two different versions meet on every update. Changes after that point must stay compatible: add fields, files, protocol versions or signature algorithms; never rename or remove them.

| Contract | Where it's defined |
|---|---|
| Exe name | `<LauncherExeName>` in `Directory.Build.props` |
| Feed layout, `files.json` (including `removed`) | [feed-format.md](feed-format.md) |
| `manifest.json`, role strings, platform IDs, package file names | [feed-format.md](feed-format.md#packagesmanifestjson) |
| `files.sig` / `manifest.sig` | [feed-format.md](feed-format.md#signatures-filessig-manifestsig) |
| `--apply-update` arguments | below |
| Launcher data folder and `update-result.json` | below |

## Exe name

The client's `AssemblyName` is `<LauncherExeName>` (default `OpenShardLauncher`), so the exe is `OpenShardLauncher.exe` on Windows and `OpenShardLauncher` on Linux and macOS. A launcher package must have an exe of the **current** name at the **root of the zip**; a package without one is rejected before anything is changed. A fork that already has players keeps its exe name for good.

## Launcher packages

`packages/launcher-{version}.{rid}.zip`, listed in the signed manifest with role `launcher`. The zip holds the published launcher folder as-is: the exe at the root, plus anything that has to sit next to it (native libraries). A launcher is offered a package only when:

- the manifest verifies (or is accepted unsigned under `AllowUnsignedFeed`, from the default server over https or loopback),
- the package's `rid` is this platform's (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`),
- its version is strictly higher than the running launcher's and not lower than the highest `launcher` version a manifest has offered before (downgrade protection, kept in `feed-state.json`),
- the running launcher has a numeric version (dev builds such as `1.2.0-dev` are never offered one).

Package zips are unpacked with the same safety checks as TazUO packages: every entry must stay inside the target folder, no symlink entries, at most 20,000 entries and 4 GB unpacked.

## The update, step by step

1. The banner appears. The launcher probes its own folder; if it can't write there (Program Files, a read-only macOS App Translocation copy) the banner says so and nothing is attempted. There is no elevation prompt.
2. **Update** (after cancelling a running game update, if the player agrees) downloads the zip into `.temp/` next to the exe. It resumes a partial download (`<zip>.part`) and checks size and SHA-256.
3. Immediately before the hand-off the manifest is fetched and verified again: it must still list exactly this package, and the zip's hash is computed again.
4. The zip is unpacked to `.temp/staging/` (never `%TEMP%`).
5. `update-result.json` is written with the expected version.
6. The launcher starts `.temp/staging/<exe>` with the arguments below and exits. The running exe is never copied: on NTFS a copy would carry a browser's Mark of the Web and could trigger SmartScreen mid-update. Files unpacked from the zip carry no mark.
7. The applier (the new version) waits up to 10 seconds for the old launcher to exit, copies `staging/` over the launcher folder **with the exe last** and starts the launcher. Files the package doesn't have are left alone (players may keep their own files there).
8. The next start compares its own version with `update-result.json`, shows "Updated to X" or "Launcher update failed: reason", deletes the marker and cleans up `.temp/` (best effort: anything still locked is retried on the next start).

## `--apply-update` protocol

```
<staging>/<exe> --apply-update <protocol> <pid> <appDir> <exeName>
```

| Argument | Meaning |
|---|---|
| `--apply-update` | Always the first argument. The launcher then runs as the applier, with no window. |
| `<protocol>` | `1`. Newer launchers must keep accepting every older protocol, because the old launcher chooses it. |
| `<pid>` | The old launcher's process id, to wait for. |
| `<appDir>` | The launcher folder to update: fully qualified, no trailing separator. |
| `<exeName>` | The exe's file name in `<appDir>` (and in the staging folder), e.g. `OpenShardLauncher.exe`. |

Arguments are passed as separate arguments (`ProcessStartInfo.ArgumentList`), not as one quoted string. The applier runs from the staging folder: the folder of its own exe is what it copies.

**Errors.** On an unknown protocol, invalid arguments, an applier that isn't running from a staged copy, or an old launcher that is still running after 10 seconds, the applier changes nothing, records the error in `update-result.json` and starts `<appDir>/<exeName>` again. If copying fails partway, it records the error and starts whatever exe is in place (the old one, since the exe is copied last).

## Launcher data folder

```
<launcher folder>/
  <exe>
  .openshardlauncher/       the player's data: don't delete
    settings.json           the player's settings (install folder, server override, ...)
    feed-state.json         last files.json version per server; per role the installed and highest offered version
    update-result.json      only between a self-update hand-off and the next start
    logs/launcher-*.log     daily, 7 kept; the applier logs here too
  .temp/                    a self-update in progress; removed at startup
  Game/                     the default install folder (LauncherOptions.DefaultInstallFolder)
```

The data folder is portable first: `.openshardlauncher/` next to the exe. Only when that folder isn't writable does the launcher use the per-user folder `<AppData>/<AppDataFolderName>/` (`%AppData%` on Windows, `~/Library/Application Support` on macOS, `~/.config` on Linux; `AppDataFolderName` defaults to `OpenShardLauncher` and is set in `launcher.json`). A launcher using the AppData fallback can't update itself.

The install folder has its own cache, `.openshardlauncher-cache/` (hash cache, partial downloads, TazUO packages being unpacked), which is safe to delete.

### update-result.json

Written by the old launcher, changed by the applier (the new version) and read by whichever version starts next, so its property names are part of the contract. Unknown properties are kept.

```json
{
  "expectedVersion": "1.2.0",
  "previousVersion": "1.1.0",
  "error": null
}
```

| Property | Meaning |
|---|---|
| `expectedVersion` | The version the launcher handed off to. |
| `previousVersion` | The version that handed off. |
| `error` | `null`, or why the applier gave up (plain text, for the log and the status line). |

A start whose version equals `expectedVersion` with no `error` reports success; anything else (including a marker the applier never touched, e.g. after its 10-second timeout) reports a failure.

## Mark of the Web

On Windows the launcher logs at startup when its own exe has a `Zone.Identifier` stream (it was downloaded with a browser). It never strips the mark and shows no warning; the self-update design above simply never carries it into the new files.
