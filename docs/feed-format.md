# Feed format

The feed is the folder the Publisher writes and a web server (or any static host or CDN) serves. Launchers only ever read it. Nothing has been released yet, so the format can still change. **After the first public release it is a stable contract.** Installed launchers depend on it, so changes after that point may only *add* fields, files or signature algorithms.

```
feed/
  files.json            the signed file list
  files.sig             signatures over files.json
  blobs/<aa>/<sha256>   file contents, named by SHA-256, sharded by the first 2 hex characters
  packages/
    manifest.json       the signed package manifest
    manifest.sig        signatures over manifest.json
    {role}-{version}.{rid}.zip
```

A launcher requests each file at its path relative to the feed root (for example `https://updates.example.com/blobs/9f/9f86…`). The only other endpoint the bundled server has is `/health`, which isn't part of the feed.

## files.json

```json
{
  "version": 1791035515,
  "files": [
    { "name": "Data/map0.mul", "sha256": "9817b74f…", "size": 4 },
    { "name": "art.mul", "sha256": "5891b5b5…", "size": 6 }
  ],
  "removed": [
    { "name": "old.mul", "removedIn": 1790000000 }
  ]
}
```

| Field | Meaning |
|---|---|
| `version` | UTC Unix time (seconds) of the publish. It only ever increases: if the existing feed already has a version at or above the clock, the Publisher uses existing + 1. A launcher refuses a list with a lower version than the last one it accepted from that server (rollback protection). |
| `files[].name` | Path relative to the install folder, with `/` between folders. Names are unique, ignoring case. A launcher writes a name only if it passes the containment check (it must resolve strictly inside the install folder). |
| `files[].sha256` | Lowercase hex SHA-256 of the contents. It is also the blob's name. |
| `files[].size` | Size in bytes. |
| `removed[].name` | A name that an earlier publish had and a later one dropped. Launchers delete exactly these names, after the containment check, and never files that a keep-local pattern or the player's ignore list matches. They never delete "files that aren't in the list", because players keep their own files in the install folder. |
| `removed[].removedIn` | The `version` of the publish that dropped it. `--forget-removed-before <date>` trims old entries. |

The JSON uses camelCase. All of these properties are required. Readers must ignore properties they don't know.

## Blobs

`blobs/<first two hex chars>/<sha256>` holds the exact bytes of every file with that hash. Blobs never change. The security comes from the signed `files.json` pinning each hash, not from the blob name. Hash names give deduplication, stable names across publishes (unchanged files are never uploaded again) and safe long-term caching.

The Publisher keeps the blobs of the current list and of the previous one, so a launcher still working from the previous list can finish. Older blobs are pruned.

## packages/manifest.json

```json
{
  "generated": "2026-10-03T13:51:55+00:00",
  "packages": [
    { "role": "launcher", "version": "1.1.0", "rid": "win-x64", "file": "launcher-1.1.0.win-x64.zip", "sha256": "…", "size": 123456 }
  ]
}
```

- `role` is `launcher` (the launcher's own self-update) or `client` (the game client the launcher starts, whatever it is: TazUO launcher, TazUO, ClassicUO).
- `rid` is a .NET runtime identifier such as `win-x64`, `linux-x64`, `osx-x64` or `osx-arm64`.
- `file` follows `{role}-{version}.{rid}.zip`, with a 2–4 part numeric version. The manifest lists only the newest version per role and platform.
- A launcher accepts a package only if its version is strictly higher than the one installed and than the highest version it has seen for that role, so validly signed older manifests can't downgrade it.
- A package file name is published once. The Publisher refuses to publish different bytes under a name that's already in the feed.

## Signatures (files.sig, manifest.sig)

One signature per line, each tagged with its algorithm:

```
p256:<base64 signature>
p256:<base64 signature from a second key>
```

- The signature covers the **exact bytes** of `files.json` or `manifest.json`.
- **Every line must be tagged.** An untagged line makes the whole file invalid.
- A line whose algorithm the launcher doesn't know or whose platform doesn't support it is skipped. This is how new algorithms (e.g. `mldsa65`) can be added later without changing the format.
- The file is **valid** if any line verifies with a trusted key of the same algorithm.
- Results: **missing** (no `.sig` file), **invalid** (present, but malformed or not verified by any trusted key) or **valid**. A missing signature is accepted only by launchers built with `AllowUnsignedFeed`, and only from their default server. An invalid one is never accepted.

`p256` is ECDSA over NIST P-256 with SHA-256. The signature is the 64-byte IEEE P1363 form (r‖s), base64-encoded.

Trusted keys live in the launcher's `launcher.json`, each with its algorithm:

```json
"TrustedPublicKeys": [ { "alg": "p256", "key": "<base64 SubjectPublicKeyInfo>" } ]
```

## Unsigned feeds

`publisher publish … --unsigned` writes the same feed without `files.sig` and `manifest.sig` (and deletes stale ones). Only launchers built with `AllowUnsignedFeed: true` accept it.

## Uploading safely

Upload tools don't follow the Publisher's write order, so use one of these:

1. **Stop the web server while uploading**, then start it again. This is the simplest option.
2. **Upload in three passes:**
   1. `blobs/` and `packages/` **without deleting** anything on the server.
   2. `packages/manifest.sig`, then `packages/manifest.json`, then `files.sig`, then `files.json`.
   3. Delete what the Publisher pruned (sync with delete).

Use an upload tool that writes to a temporary name and renames the file when it's complete (rclone and rsync do by default), so a blob is never visible half-written.

Launchers tolerate a feed caught mid-upload. A missing blob, or a list and signature that still don't match after one refetch, is reported as "the server is updating, try again shortly", not as untrusted.
