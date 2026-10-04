# Releasing

The maintainer's checklist for a launcher release. The overview is in the [README](../README.md#releasing-the-launcher).

## Checklist

1. `main` is green in CI.
2. Tag a **numeric** version and push it: `git tag v1.2.0 && git push origin v1.2.0`. `release.yml` refuses anything else (e.g. `v1.2.0-beta`), because launchers only self-update between plain numeric versions.
3. Wait for the **Release** workflow. It runs the tests and creates a **draft** release with:
   - `launcher-{version}.{rid}.zip` (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`): exe at the zip root, named `<LauncherExeName>`, plus its native libraries,
   - `publisher-{version}.{rid}.zip` (same platforms), `server-{version}.{rid}.zip` (`win-x64`, `linux-x64`),
   - `SHA256SUMS.txt`.
4. Scan the zips and record the results below (VirusTotal, and Windows Defender on a clean, up-to-date VM). On a detection: submit a false positive (<https://www.microsoft.com/wdsi/filesubmission>, or the vendor's own form) and change the packaging only if detections persist.
5. On your own machine: download the launcher zips into the `--packages` folder, run `publisher publish ... --key <key> [--key <backup>]` and `publisher verify`, then upload the feed ([uploading safely](feed-format.md#uploading-safely)).
6. Smoke-test: an installed launcher of the previous version offers the update and comes back as "Updated to {version}".
7. Publish the draft release.

**Never in CI:** the private key, `publisher publish --key`, a `launcher.json` with test keys. Upstream `launcher.json` stays without keys (the Release build then warns `OSL0001`, by design).

## Manual self-update matrix

Self-update can only be tested by hand. Done in phase 7 on **win-x64** with Debug, framework-dependent builds: success, non-writable folder, corrupted package, applier timeout, Mark of the Web. Still to run **with Release builds from this workflow**:

| Case | Status |
|---|---|
| win-x64: single-file Release build updates to another single-file Release build | open |
| win-x64: SmartScreen on a browser-downloaded unsigned Release exe: first-run prompt expected, **none during the update** | open |
| linux-x64: update succeeds, the new exe keeps its executable bit | open |
| osx-x64: update succeeds, the new binary passes `codesign --verify` and starts | open |
| osx-arm64: same as osx-x64 (Apple Silicon refuses unsigned code) | open |
| macOS App Translocation: launcher started from `Downloads` with the quarantine flag shows the "move it out of Downloads" banner and doesn't attempt the update | open |

How to run one: Release builds ignore `launcher.local.json`, so build **two** launchers for the test only, with a test key (or `AllowUnsignedFeed: true`) put into `src/OpenShardLauncher.Client/Resources/launcher.json` and never committed: e.g. run the Release workflow manually on a throwaway branch, or locally

```bash
dotnet publish src/OpenShardLauncher.Client -c Release -r <rid> --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:DebugType=embedded -p:Version=1.0.0 -o old
dotnet publish src/OpenShardLauncher.Client -c Release -r <rid> --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:DebugType=embedded -p:Version=1.0.1 -o new
```

(on macOS for osx RIDs, so the SDK signs them), delete the `*.pdb` files, zip `new/` with its files at the root as `launcher-1.0.1.<rid>.zip`, publish a feed with the test key, serve it, and start `old/`.

## Scan record

| Version | Date | Files | Scanner | Result |
|---|---|---|---|---|
| (pre-release, local build of `main`) | 2026-10-04 | win-x64 launcher, server and Publisher, single-file, built locally with the workflow's flags | Windows Defender (signatures 1.459.546.0), `MpCmdRun -Scan -ScanType 3` | no threats |
