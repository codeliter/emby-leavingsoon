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
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.LeavingSoon.Services;

public class LeavingSoonScanner
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ILogger _logger;
    private readonly IMediaRemover _remover;
    private readonly PluginConfiguration? _configOverride;

    public LeavingSoonScanner(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ICollectionManager collectionManager,
        ILogger logger)
        : this(libraryManager, userManager, userDataManager, collectionManager, logger, new MediaRemover(logger), null)
    {
    }

    public LeavingSoonScanner(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ICollectionManager collectionManager,
        ILogger logger,
        IMediaRemover remover,
        PluginConfiguration? configOverride)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _collectionManager = collectionManager;
        _logger = logger;
        _remover = remover;
        _configOverride = configOverride;
    }

    private PluginConfiguration Config => _configOverride ?? Plugin.Instance.Configuration;

    private void SaveConfig()
    {
        if (_configOverride == null)
        {
            Plugin.Instance.SaveConfiguration();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var config = Config;
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
        stale.AddRange(FindStaleSeries(cutoff, minAge, excludedTags, config));

        _logger.Info($"[LeavingSoon] Scan found {stale.Count} stale items");

        progress.Report(40);

        await SyncCollectionAsync(stale.Select(s => s.Item).ToList()).ConfigureAwait(false);

        progress.Report(60);

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

            if (!CandidateEvaluator.IsDueForRemoval(entry.AddedToCollectionUtc, entry.Approved, config.GracePeriodDays, config.Mode, now))
            {
                continue;
            }

            await RemoveAsync(entry, config, cancellationToken).ConfigureAwait(false);
        }

        SaveConfig();
        progress.Report(100);
    }

    private class TrackedCandidate
    {
        public BaseItem Item { get; set; }
        public string ItemId { get; set; }
        public string MediaType { get; set; }
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

    private IEnumerable<TrackedCandidate> FindStaleSeries(DateTime cutoff, DateTime minAge, HashSet<string> excludedTags, PluginConfiguration config)
    {
        if (!config.EnableSeries)
        {
            yield break;
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { nameof(Series) },
            Recursive = true
        };

        var allSeries = _libraryManager.GetItemList(query).OfType<Series>();

        foreach (var series in allSeries)
        {
            if (IsExcluded(series, minAge, excludedTags, config))
            {
                continue;
            }

            var episodeQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                AncestorIds = new[] { series.InternalId },
                Recursive = true
            };

            var episodes = _libraryManager.GetItemList(episodeQuery).OfType<Episode>().ToList();
            if (episodes.Count == 0 || episodes.Any(e => e.DateCreated.UtcDateTime > minAge))
            {
                continue;
            }

            if (episodes.All(e => IsStale(e, cutoff)))
            {
                yield return new TrackedCandidate { Item = series, ItemId = KeyOf(series), MediaType = "Series" };
            }
        }
    }

    private bool IsExcluded(BaseItem item, DateTime minAge, HashSet<string> excludedTags, PluginConfiguration config)
    {
        var anyFavorite = false;
        if (config.ExcludeFavorites)
        {
            foreach (var user in _userManager.Users)
            {
                if (_userDataManager.GetUserData(user, item).IsFavorite)
                {
                    anyFavorite = true;
                    break;
                }
            }
        }

        return CandidateEvaluator.IsExcluded(item.DateCreated.UtcDateTime, minAge, item.Tags, excludedTags, anyFavorite);
    }

    private bool IsStale(BaseItem item, DateTime cutoff)
    {
        var states = new List<PlayState>();
        foreach (var user in _userManager.Users)
        {
            var data = _userDataManager.GetUserData(user, item);
            states.Add(new PlayState
            {
                Played = data.Played,
                LastPlayedDateUtc = data.LastPlayedDate.HasValue ? data.LastPlayedDate.Value.UtcDateTime : (DateTime?)null
            });
        }

        return CandidateEvaluator.IsStale(states, cutoff);
    }

    private async Task SyncCollectionAsync(List<BaseItem> staleItems)
    {
        var config = Config;
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

            var created = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(BoxSet) },
                Name = config.CollectionName
            }).OfType<BoxSet>().FirstOrDefault();
            if (created != null)
            {
                ApplyPresentation(created, staleItems.Count, config);
            }

            return;
        }

        var current = new HashSet<long>(_libraryManager.GetItemList(new InternalItemsQuery
        {
            Parent = existing,
            Recursive = true
        }).Select(c => c.InternalId));

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

        ApplyPresentation(existing, staleIds.Count, config);
    }

    private void ApplyPresentation(BoxSet collection, int itemCount, PluginConfiguration config)
    {
        var overview = "These movies and series haven't been watched in a while and will be removed from the library soon. Watch them now, or remove them from this collection to keep them.";
        var tagline = itemCount == 1 ? "1 item leaving soon" : $"{itemCount} items leaving soon";

        if (collection.Overview != overview || collection.Tagline != tagline)
        {
            collection.Overview = overview;
            collection.Tagline = tagline;
            _libraryManager.UpdateItem(collection, collection.GetParent(), ItemUpdateType.MetadataEdit);
        }
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
            Log(config, "dry-run", $"Would remove {entry.MediaType}: {entry.Name}");
            return;
        }

        try
        {
            if (entry.MediaType == "Movie")
            {
                var tmdb = item.ProviderIds.TryGetValue("Tmdb", out var tmdbValue) ? tmdbValue : null;
                if (int.TryParse(tmdb, out var tmdbId))
                {
                    var movieId = await _remover.FindMovieIdByTmdbAsync(tmdbId, cancellationToken).ConfigureAwait(false);
                    if (movieId.HasValue)
                    {
                        await _remover.DeleteMovieAsync(movieId.Value, config.DeleteFiles, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (entry.MediaType == "Series")
            {
                var tvdb = item.ProviderIds.TryGetValue("Tvdb", out var tvdbValue) ? tvdbValue : null;
                if (int.TryParse(tvdb, out var tvdbId))
                {
                    var seriesId = await _remover.FindSeriesIdByTvdbAsync(tvdbId, cancellationToken).ConfigureAwait(false);
                    if (seriesId.HasValue)
                    {
                        await _remover.DeleteSeriesAsync(seriesId.Value, config.DeleteFiles, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            entry.RemovedUtc = DateTime.UtcNow;
            Log(config, "removed", $"{entry.MediaType}: {entry.Name}");
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
