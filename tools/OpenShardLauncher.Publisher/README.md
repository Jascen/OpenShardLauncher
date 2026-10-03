# OpenShardLauncher Publisher

Builds and signs the whole update feed ([feed format](../../docs/feed-format.md)) on the machine that holds the private key. Afterwards, upload the output folder to any static host. The private key never goes on the server, in CI or in the repository.

```
publisher keygen [--out <dir>]
publisher publish --source <game files> --packages <zips> --out <feed> --key <key> [--key <key2>]
                  [--state <dir>] [--forget-removed-before <date>]
publisher publish --source <game files> --packages <zips> --out <feed> --unsigned
publisher verify  --feed <feed> [--pub <key or file>]...
```

Key passwords come from the `PUBLISHER_KEY_PASSWORD` environment variable. If it isn't set, the Publisher asks for them.

## keygen

Writes `feed-signing.key` (password-encrypted PKCS#8 PEM) and `feed-signing.pub.json` (the public key in `launcher.json` format), and prints the line to add to `TrustedPublicKeys`. It never overwrites an existing key. Generate a **backup key** too, keep it offline separately and trust it from the first release.

## publish

- Hashes `--source` incrementally. A hash cache (path, size, modified time → SHA-256) lives in `--state`, by default `<out>/../.publisher-state`, never inside the feed or the source. It's the only thing there: deleting it only means everything is hashed again.
- Copies new content to `blobs/<aa>/<sha256>` and the newest zip per role and platform from `--packages` to `packages/`, then writes and signs `packages/manifest.json` and `files.json`. Each `--key` adds one signature line.
- `version` is the current UTC Unix time. If the existing feed's version is the same or newer (a clock behind, or two publishes in one second), the Publisher uses existing + 1 and warns.
- Names that the previous `files.json` had but this publish doesn't are added to the signed `removed` list, so launchers delete them. A name that comes back is dropped from the list. `--forget-removed-before 2026-01-31` trims old entries.
- Keeps the blobs and zips of this and the previous publish, and prunes older ones.
- **Writes nothing until every check has passed:**
  - `--out` and `--source` must not be inside each other, and the state folder must not be inside either of them.
  - A `--key` file inside `--source`, or **any PEM private key** in `--source` (whatever its name), is refused.
  - **Collision guard:** an existing blob must have the same size and, for files hashed in this run, the same bytes.
  - A package zip already in the feed under the same name must have the same bytes. A rebuilt package needs a higher version.
  - Every name must pass the containment check, and names must be unique ignoring case.
- Default exclusions and `.publishignore`: see [ignore rules](../../docs/ignore-rules.md). Symlinks in `--source` are skipped.
- **Write order:** blobs → package zips → `manifest.sig` → `manifest.json` → `files.sig` → `files.json` → pruning. Each file is written to a temp file and then moved into place.
- The summary lists **every new file name**, so you notice an accidental `Accounts.xml` before uploading. It also shows the bytes to upload.

`--unsigned` builds the same feed without `files.sig` and `manifest.sig` (and deletes stale ones). Only launchers built with `AllowUnsignedFeed: true` accept such a feed, and only from their default server. It can't be combined with `--key`.

### Uploading

Upload tools don't follow the write order. Either **stop the web server while uploading**, or upload in three passes:

1. `blobs/` and `packages/` without deleting anything (e.g. `rclone copy`, `rsync` without `--delete`).
2. `packages/manifest.sig`, `packages/manifest.json`, `files.sig`, `files.json`, in that order.
3. Delete pruned files (e.g. `rclone sync`, `rsync --delete`).

Blobs never change, so only new ones are transferred.

## verify

Checks a feed the way a launcher would: the signatures of `files.json` and `manifest.json` against the given public keys (a `feed-signing.pub.json` file, a `{ "alg", "key" }` JSON or a bare base64 P-256 key), and that every blob and package exists with the right size and SHA-256. Without `--pub`, the feed must be unsigned.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Refused or failed; the message says why. A refused publish leaves the feed unchanged. |
| 2 | Usage error: unknown command or option, or a missing value |
| 3 | `verify` found problems |
