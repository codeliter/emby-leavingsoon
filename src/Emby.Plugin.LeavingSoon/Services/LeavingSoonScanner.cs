using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.LeavingSoon.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.LeavingSoon.Services;

public class LeavingSoonScanner
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ILogger _logger;
    private readonly RadarrClient _radarr;
    private readonly SonarrClient _sonarr;

    public LeavingSoonScanner(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ICollectionManager collectionManager,
        ILogger logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _collectionManager = collectionManager;
        _logger = logger;
        _radarr = new RadarrClient(logger);
        _sonarr = new SonarrClient(logger);
    }

    public async Task RunAsync(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var config = Plugin.Instance.Configuration;
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-config.UnwatchedDaysThreshold);
        var minAge = now.AddDays(-config.MinimumLibraryAgeDays);
        var excludedTags = new HashSet<string>(
            config.ExcludedTags
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var stale = new List<TrackedCandidate>();
        stale.AddRange(FindStaleMovies(cutoff, minAge, excludedTags, config));
        stale.AddRange(FindStaleSeasons(cutoff, minAge, excludedTags, config));

        _logger.Info($"[LeavingSoon] Scan found {stale.Count} stale items");

        progress.Report(40);

        await SyncCollectionAsync(stale.Select(s => s.Item).ToList()).ConfigureAwait(false);

        progress.Report(60);

        var graceCutoff = now.AddDays(-config.GracePeriodDays);
        var tracked = config.Tracked;
        var trackedIds = new HashSet<string>(tracked.Select(t => t.ItemId));

        foreach (var candidate in stale)
        {
            if (!trackedIds.Contains(candidate.ItemId))
            {
                tracked.Add(new TrackedItem
                {
                    ItemId = candidate.ItemId,
                    Name = candidate.Item.Name,
                    MediaType = candidate.MediaType,
                    SeasonNumber = candidate.SeasonNumber,
                    AddedToCollectionUtc = now
                });
            }
        }

        var staleIds = new HashSet<string>(stale.Select(s => s.ItemId));
        foreach (var entry in tracked.Where(t => t.RemovedUtc == null && !staleIds.Contains(t.ItemId)).ToList())
        {
            tracked.Remove(entry);
            Log(config, "unstale", $"{entry.Name} watched or removed from candidates");
        }

        progress.Report(75);

        foreach (var entry in tracked.Where(t => t.RemovedUtc == null).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var due = entry.AddedToCollectionUtc <= graceCutoff;
            var approved = config.Mode == RemovalMode.Automatic || entry.Approved;
            if (!due || !approved)
            {
                continue;
            }

            await RemoveAsync(entry, config, cancellationToken).ConfigureAwait(false);
        }

        Plugin.Instance.SaveConfiguration();
        progress.Report(100);
    }

    private class TrackedCandidate
    {
        public BaseItem Item { get; set; }
        public string ItemId { get; set; }
        public string MediaType { get; set; }
        public int? SeasonNumber { get; set; }
    }

    private static string KeyOf(BaseItem item) => item.Id.ToString("N", CultureInfo.InvariantCulture);

    private IEnumerable<TrackedCandidate> FindStaleMovies(DateTime cutoff, DateTime minAge, HashSet<string> excludedTags, PluginConfiguration config)
    {
        if (!config.EnableMovies)
        {
            yield break;
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { nameof(Movie) },
            Recursive = true
        };

        foreach (var movie in _libraryManager.GetItemList(query).OfType<Movie>())
        {
            if (IsExcluded(movie, minAge, excludedTags, config))
            {
                continue;
            }

            if (IsStale(movie, cutoff))
            {
                yield return new TrackedCandidate { Item = movie, ItemId = KeyOf(movie), MediaType = "Movie" };
            }
        }
    }

    private IEnumerable<TrackedCandidate> FindStaleSeasons(DateTime cutoff, DateTime minAge, HashSet<string> excludedTags, PluginConfiguration config)
    {
        if (!config.EnableSeries)
        {
            yield break;
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { nameof(Season) },
            Recursive = true
        };

        var seasons = _libraryManager.GetItemList(query).OfType<Season>();

        foreach (var season in seasons)
        {
            if (season.IndexNumber == null || season.IndexNumber == 0 || season.Series == null)
            {
                continue;
            }

            if (IsExcluded(season.Series, minAge, excludedTags, config))
            {
                continue;
            }

            var episodes = season.GetEpisodes().Items;
            if (episodes.Length == 0 || episodes.Any(e => e.DateCreated > minAge))
            {
                continue;
            }

            if (episodes.All(e => IsStale(e, cutoff)))
            {
                yield return new TrackedCandidate { Item = season, ItemId = KeyOf(season), MediaType = "Season", SeasonNumber = season.IndexNumber };
            }
        }
    }

    private bool IsExcluded(BaseItem item, DateTime minAge, HashSet<string> excludedTags, PluginConfiguration config)
    {
        if (item.DateCreated > minAge)
        {
            return true;
        }

        if (item.Tags.Any(t => excludedTags.Contains(t)))
        {
            return true;
        }

        if (config.ExcludeFavorites)
        {
            foreach (var user in _userManager.Users)
            {
                if (_userDataManager.GetUserData(user, item).IsFavorite)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsStale(BaseItem item, DateTime cutoff)
    {
        DateTime? latestPlay = null;
        var anyPlayed = false;

        foreach (var user in _userManager.Users)
        {
            var data = _userDataManager.GetUserData(user, item);
            if (data.Played)
            {
                anyPlayed = true;
                if (data.LastPlayedDate.HasValue && (latestPlay == null || data.LastPlayedDate > latestPlay))
                {
                    latestPlay = data.LastPlayedDate.Value.UtcDateTime;
                }
            }
        }

        if (!anyPlayed)
        {
            return true;
        }

        return latestPlay.HasValue && latestPlay.Value < cutoff;
    }

    private async Task SyncCollectionAsync(List<BaseItem> staleItems)
    {
        var config = Plugin.Instance.Configuration;
        var staleIds = staleItems.Select(i => i.InternalId).ToList();

        var existing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { nameof(BoxSet) },
            Name = config.CollectionName
        }).OfType<BoxSet>().FirstOrDefault();

        if (existing == null)
        {
            if (staleItems.Count == 0)
            {
                return;
            }

            await _collectionManager.CreateCollection(new CollectionCreationOptions
            {
                Name = config.CollectionName,
                ItemIdList = staleIds.ToArray()
            }).ConfigureAwait(false);
            _logger.Info($"[LeavingSoon] Created collection '{config.CollectionName}' with {staleIds.Count} items");
            return;
        }

        var current = new HashSet<long>(existing.GetRecursiveChildren().Select(c => c.InternalId));

        var toAdd = staleIds.Where(id => !current.Contains(id)).ToArray();
        if (toAdd.Length > 0)
        {
            await _collectionManager.AddToCollection(existing.InternalId, toAdd).ConfigureAwait(false);
        }

        var toRemove = current.Where(id => !staleIds.Contains(id)).ToArray();
        if (toRemove.Length > 0)
        {
            _collectionManager.RemoveFromCollection(existing, toRemove);
        }

        _logger.Info($"[LeavingSoon] Collection sync: +{toAdd.Length} -{toRemove.Length}");
    }

    private async Task RemoveAsync(TrackedItem entry, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(new Guid(entry.ItemId));
        if (item == null)
        {
            entry.RemovedUtc = DateTime.UtcNow;
            Log(config, "missing", $"{entry.Name} no longer in library");
            return;
        }

        if (config.DryRun)
        {
            Log(config, "dry-run", $"Would remove {entry.MediaType}: {entry.Name}{(entry.SeasonNumber.HasValue ? $" S{entry.SeasonNumber:D2}" : string.Empty)}");
            return;
        }

        try
        {
            if (entry.MediaType == "Movie")
            {
                var tmdb = item.ProviderIds.TryGetValue("Tmdb", out var tmdbValue) ? tmdbValue : null;
                if (int.TryParse(tmdb, out var tmdbId))
                {
                    var movieId = await _radarr.FindMovieIdByTmdbAsync(tmdbId, cancellationToken).ConfigureAwait(false);
                    if (movieId.HasValue)
                    {
                        await _radarr.DeleteMovieAsync(movieId.Value, config.DeleteFiles, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (entry.MediaType == "Season" && entry.SeasonNumber.HasValue)
            {
                var series = ((Season)item).Series;
                var tvdb = series != null && series.ProviderIds.TryGetValue("Tvdb", out var tvdbValue) ? tvdbValue : null;
                if (int.TryParse(tvdb, out var tvdbId))
                {
                    var seriesId = await _sonarr.FindSeriesIdByTvdbAsync(tvdbId, cancellationToken).ConfigureAwait(false);
                    if (seriesId.HasValue)
                    {
                        await _sonarr.DeleteSeasonFilesAsync(seriesId.Value, entry.SeasonNumber.Value, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            entry.RemovedUtc = DateTime.UtcNow;
            Log(config, "removed", $"{entry.MediaType}: {entry.Name}{(entry.SeasonNumber.HasValue ? $" S{entry.SeasonNumber:D2}" : string.Empty)}");
        }
        catch (Exception ex)
        {
            _logger.Error($"[LeavingSoon] Failed to remove {entry.Name}: {ex.Message}");
            Log(config, "error", $"{entry.Name}: {ex.Message}");
        }
    }

    private void Log(PluginConfiguration config, string action, string detail)
    {
        config.AuditLog.Add(new AuditEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Action = action,
            Detail = detail
        });

        if (config.AuditLog.Count > 500)
        {
            config.AuditLog.RemoveRange(0, config.AuditLog.Count - 500);
        }
    }
}
