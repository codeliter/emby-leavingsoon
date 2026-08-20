# Leaving Soon

An Emby plugin that surfaces movies and TV seasons nobody has watched in a while in a **Leaving Soon** collection, then coordinates removal through **Sonarr** and **Radarr**.

Think [Maintainerr](https://github.com/jorenn92/Maintainerr), but native to Emby.

## How it works

1. A scheduled task (daily by default) scans play data across all users.
2. Movies and series with no play in **X days** (default 180) are added to the *Leaving Soon* collection.
3. Items sit in the collection for a **grace period** (default 14 days) — anyone can watch or rescue them.
4. After the grace period:
   - **Manual mode** (default): items wait for approval via the plugin API.
   - **Automatic mode**: items are removed immediately.
5. Removal is delegated to the *arr stack:
   - **Movies** → Radarr `DELETE /api/v3/movie/{id}` (matched by TMDB ID)
   - **Series** → Sonarr `DELETE /api/v3/series/{id}` (matched by TVDB ID). A series is only a candidate when *no episode* has been watched within the threshold.
6. Emby's next library scan drops the deleted files from the library.

## Safety rails

- **Dry run is ON by default.** The plugin logs what it *would* remove and never deletes anything until you disable it.
- Favorites are excluded by default.
- Items newer than a configurable minimum age are never considered.
- Tag-based exclusion list (e.g. `keep`, `kids`).
- Watching an item removes it from the candidate list on the next scan.
- Every action is written to an audit log (last 500 entries).

## Installation

### Via plugin catalog (recommended)

1. Emby Dashboard → **Plugins** → **Catalog** → ⚙ → **Add**
2. Repository URL:
   ```
   https://raw.githubusercontent.com/codeliter/emby-leavingsoon/main/manifest/manifest.json
   ```
3. Install **Leaving Soon** from the catalog and restart Emby.

### Manual

1. Download `Emby.Plugin.LeavingSoon.dll` from the [latest release](../../releases/latest).
2. Copy it to `<emby-config>/plugins/`.
3. Restart Emby.

## Configuration

Dashboard → **Plugins** → **Leaving Soon**:

| Setting | Default | Notes |
|---|---|---|
| Unwatched days threshold | 180 | No play in this many days → candidate |
| Grace period | 14 | Days in the collection before removal |
| Minimum library age | 30 | Never touch recent additions |
| Removal mode | Manual | Manual approval or automatic |
| Dry run | **on** | Log only, no deletion |
| Delete files | on | *arr deletes files vs unmonitor only |
| Include movies / series | on | A series is stale only when no episode has been watched within the threshold |
| Exclude favorites | on | |
| Excluded tags | — | Comma-separated |
| Collection name | Leaving Soon | |
| Radarr / Sonarr URL + API key | — | Required for removal |

## API

Authenticated endpoints (usable from the dashboard or scripts):

```
GET  /LeavingSoon/Candidates       # items currently tracked
POST /LeavingSoon/Approve/{itemId} # approve removal (manual mode)
POST /LeavingSoon/Rescue/{itemId}  # stop tracking without deleting
GET  /LeavingSoon/Audit            # removal audit log
```

## Building

Requires the .NET 8 SDK (build only; the plugin itself targets netstandard2.0):

```bash
dotnet publish src/Emby.Plugin.LeavingSoon/Emby.Plugin.LeavingSoon.csproj -c Release -o out
```

Or with Docker:

```bash
docker build -o out .
```

## Releasing

Every merge to `main` creates a new release automatically. GitVersion computes the next version from commit messages:

- `feat:` / `add` in a commit message → minor bump
- `fix:` / `chore:` / `docs:` etc → patch bump
- `breaking` / `major` → major bump

CI builds the DLL, tags the release, attaches the DLL, and updates the catalog manifest with version, checksum, and download URL — no manual steps.

## Requirements

- Emby Server 4.9.x
- Radarr v3+ and/or Sonarr v3+ for removal (scanning/collection works without them)

## License

MIT
