# Ignore rules

The same syntax is used for:

- **`.openshardignore`** in the player's install folder: files and folders the launcher never downloads, overwrites or deletes.
- **`.publishignore`** in the Publisher's `--source` folder: files that are never published.

It reads like a `.gitignore`, but uses globs only (no regular expressions, no `[abc]` sets).

| Pattern | Matches |
|---|---|
| `# text` | A comment. Blank lines are skipped too, and spaces around a pattern are trimmed. |
| `*.log` | A pattern without `/` matches a file or folder **name at any depth**: `debug.log`, `Logs/old/debug.log`. |
| `Music` | A folder named `Music` anywhere, with everything inside it (and a file named `Music`). |
| `Music/` | A trailing `/` matches **folders only**. |
| `/art.mul` | A leading `/` **anchors** the pattern to the top folder: `art.mul`, but not `Data/art.mul`. |
| `Data/*.mul` | A `/` in the middle anchors too: `Data/art.mul`, but not `Other/Data/art.mul`. |
| `*`, `?` | Any characters / one character, **within one name** (`Data/*.mul` doesn't match `Data/sub/art.mul`). |
| `**` | As a whole segment, any number of folders, including none: `**/Music/*.mp3`, `Data/**/*.mp3`, `Data/**`. |
| `!pattern` | Brings back something an earlier line ignored. The **last matching line wins**. |
| `\#name`, `\!name` | A name that really starts with `#` or `!`. |

- Matching **ignores case**.
- `\` is accepted as a folder separator, so `Data\*.mul` is the same as `Data/*.mul`.
- As in git, a file **inside an ignored folder can't be brought back** with `!`. Ignore the folder's contents (`Music/*`) instead of the folder if you need exceptions.

## Publisher defaults

The Publisher always applies these before `.publishignore`, so a `!` line there can bring one back (e.g. `!Data/appsettings.json`):

```
.git/
.env*
*.pem
*.key
appsettings*.json
/.publishignore
/.openshardignore
/.openshardlauncher-cache/
```

Separately, the Publisher refuses to publish at all if any file in `--source` (outside `.git/`) is a PEM private key, whatever its name and whether or not it's ignored.
