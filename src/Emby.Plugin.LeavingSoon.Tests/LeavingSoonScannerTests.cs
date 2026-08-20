using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.LeavingSoon.Configuration;
using Emby.Plugin.LeavingSoon.Services;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using Moq;
using Xunit;

namespace Emby.Plugin.LeavingSoon.Tests;

public class LeavingSoonScannerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Mock<ILibraryManager> _library = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IUserDataManager> _userData = new();
    private readonly Mock<ICollectionManager> _collections = new();
    private readonly Mock<IMediaRemover> _remover = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly User _user = new();
    private readonly List<UserItemData> _playStates = new();

    public LeavingSoonScannerTests()
    {
        _users.Setup(u => u.Users).Returns(new[] { _user });
        _userData
            .Setup(d => d.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns((User _, BaseItem item) =>
            {
                var state = _playStates.Find(s => s.Key == item.Id.ToString("N"));
                return state ?? new UserItemData { Played = false };
            });
    }

    private Movie StaleMovie(string name = "Old Movie")
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            DateCreated = Now.AddDays(-400),
            Tags = Array.Empty<string>(),
            ProviderIds = new ProviderIdDictionary { { "Tmdb", "12345" } }
        };
        _playStates.Add(new UserItemData { Key = movie.Id.ToString("N"), Played = true, LastPlayedDate = Now.AddDays(-300) });
        return movie;
    }

    private PluginConfiguration Config(Action<PluginConfiguration>? tweak = null)
    {
        var config = new PluginConfiguration
        {
            UnwatchedDaysThreshold = 180,
            GracePeriodDays = 14,
            MinimumLibraryAgeDays = 30,
            Mode = RemovalMode.Automatic,
            DryRun = false,
            EnableMovies = true,
            EnableSeries = false,
            RadarrUrl = "http://radarr:7878",
            RadarrApiKey = "key"
        };
        tweak?.Invoke(config);
        return config;
    }

    private LeavingSoonScanner BuildScanner(BaseItem[] libraryItems, PluginConfiguration config)
    {
        _library
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => FilterByType(libraryItems, q.IncludeItemTypes));
        _library
            .Setup(l => l.GetItemById(It.IsAny<Guid>()))
            .Returns((Guid id) => Array.Find(libraryItems, i => i.Id == id));
        return new LeavingSoonScanner(_library.Object, _users.Object, _userData.Object, _collections.Object, _logger.Object, _remover.Object, config);
    }

    private static BaseItem[] FilterByType(BaseItem[] items, string[] types)
    {
        var result = new List<BaseItem>();
        foreach (var item in items)
        {
            foreach (var t in types)
            {
                if (t == "BoxSet" && item is BoxSet)
                {
                    result.Add(item);
                }
                else if (t == "Movie" && item is Movie)
                {
                    result.Add(item);
                }
                else if (t == "Series" && item is MediaBrowser.Controller.Entities.TV.Series)
                {
                    result.Add(item);
                }
                else if (t == "Episode" && item is MediaBrowser.Controller.Entities.TV.Episode)
                {
                    result.Add(item);
                }
            }
        }

        return result.ToArray();
    }

    [Fact]
    public async Task NewCollection_GetsPresentation()
    {
        var movie = StaleMovie();
        var config = Config(c => c.Mode = RemovalMode.Manual);

        BoxSet? created = null;
        _library
            .SetupSequence(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(FilterByType(new BaseItem[] { movie }, new[] { "Movie" }))
            .Returns(Array.Empty<BaseItem>())
            .Returns(Array.Empty<BaseItem>())
            .Returns(new BaseItem[] { new BoxSet { Name = "Leaving Soon" } });

        var scanner = new LeavingSoonScanner(_library.Object, _users.Object, _userData.Object, _collections.Object, _logger.Object, _remover.Object, config);
        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        _collections.Verify(c => c.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "Leaving Soon")), Times.Once);
    }

    [Fact]
    public async Task ExistingCollection_SyncsMembershipAndTagline()
    {
        var movie = StaleMovie();
        var collection = new BoxSet { Id = Guid.NewGuid(), Name = "Leaving Soon" };
        var config = Config(c => c.Mode = RemovalMode.Manual);

        _library
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
            {
                if (q.IncludeItemTypes.Contains("BoxSet"))
                {
                    return q.Name == "Leaving Soon" ? new BaseItem[] { collection } : Array.Empty<BaseItem>();
                }

                if (q.Parent == collection)
                {
                    return Array.Empty<BaseItem>();
                }

                return FilterByType(new BaseItem[] { movie }, q.IncludeItemTypes);
            });
        _library.Setup(l => l.GetItemById(It.IsAny<Guid>())).Returns((Guid id) => id == movie.Id ? movie : null);

        var scanner = new LeavingSoonScanner(_library.Object, _users.Object, _userData.Object, _collections.Object, _logger.Object, _remover.Object, config);
        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        _collections.Verify(c => c.AddToCollection(collection.InternalId, It.Is<long[]>(ids => ids.Contains(movie.InternalId))), Times.Once);
        Assert.Equal("1 item leaving soon", collection.Tagline);
        Assert.Contains("removed from the library soon", collection.Overview);
    }

    [Fact]
    public async Task StaleMovie_IsTrackedAndAddedToCollection()
    {
        var movie = StaleMovie();
        var config = Config(c => c.Mode = RemovalMode.Manual);
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        var tracked = Assert.Single(config.Tracked);
        Assert.Equal(movie.Id.ToString("N"), tracked.ItemId);
        Assert.Equal("Movie", tracked.MediaType);
        _collections.Verify(c => c.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "Leaving Soon")), Times.Once);
        _remover.Verify(r => r.DeleteMovieAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecentlyPlayedMovie_IsIgnored()
    {
        var movie = StaleMovie();
        _playStates[0].LastPlayedDate = Now.AddDays(-5);
        var config = Config();
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        Assert.Empty(config.Tracked);
        _collections.Verify(c => c.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
    }

    [Fact]
    public async Task FavoriteMovie_IsExcluded()
    {
        var movie = StaleMovie();
        _playStates[0].IsFavorite = true;
        var config = Config(c => c.ExcludeFavorites = true);
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        Assert.Empty(config.Tracked);
    }

    [Fact]
    public async Task TaggedMovie_IsExcluded()
    {
        var movie = StaleMovie();
        movie.Tags = new[] { "keep" };
        var config = Config(c => c.ExcludedTags = "keep");
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        Assert.Empty(config.Tracked);
    }

    [Fact]
    public async Task RecentlyAddedMovie_IsExcluded()
    {
        var movie = StaleMovie();
        movie.DateCreated = Now.AddDays(-5);
        var config = Config();
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        Assert.Empty(config.Tracked);
    }

    [Fact]
    public async Task AutomaticMode_PastGrace_DeletesViaRadarr()
    {
        var movie = StaleMovie();
        var config = Config();
        config.Tracked.Add(new TrackedItem
        {
            ItemId = movie.Id.ToString("N"),
            Name = movie.Name,
            MediaType = "Movie",
            AddedToCollectionUtc = Now.AddDays(-30)
        });
        _remover.Setup(r => r.FindMovieIdByTmdbAsync(12345, It.IsAny<CancellationToken>())).ReturnsAsync(77);
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        _remover.Verify(r => r.DeleteMovieAsync(77, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(config.Tracked[0].RemovedUtc);
        Assert.Contains(config.AuditLog, a => a.Action == "removed");
    }

    [Fact]
    public async Task DryRun_LogsInsteadOfDeleting()
    {
        var movie = StaleMovie();
        var config = Config(c => c.DryRun = true);
        config.Tracked.Add(new TrackedItem
        {
            ItemId = movie.Id.ToString("N"),
            Name = movie.Name,
            MediaType = "Movie",
            AddedToCollectionUtc = Now.AddDays(-30)
        });
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        _remover.Verify(r => r.DeleteMovieAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(config.AuditLog, a => a.Action == "dry-run");
        Assert.Null(config.Tracked[0].RemovedUtc);
    }

    [Fact]
    public async Task ManualMode_Unapproved_NothingDeleted()
    {
        var movie = StaleMovie();
        var config = Config(c => c.Mode = RemovalMode.Manual);
        config.Tracked.Add(new TrackedItem
        {
            ItemId = movie.Id.ToString("N"),
            Name = movie.Name,
            MediaType = "Movie",
            AddedToCollectionUtc = Now.AddDays(-30)
        });
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        _remover.Verify(r => r.DeleteMovieAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(config.Tracked[0].RemovedUtc);
    }

    [Fact]
    public async Task WatchedAgain_IsRemovedFromTracking()
    {
        var movie = StaleMovie();
        _playStates[0].LastPlayedDate = Now.AddDays(-2);
        var config = Config();
        config.Tracked.Add(new TrackedItem
        {
            ItemId = movie.Id.ToString("N"),
            Name = movie.Name,
            MediaType = "Movie",
            AddedToCollectionUtc = Now.AddDays(-10)
        });
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        Assert.Empty(config.Tracked);
        Assert.Contains(config.AuditLog, a => a.Action == "unstale");
    }

    [Fact]
    public async Task MoviesDisabled_SkipsMovies()
    {
        var movie = StaleMovie();
        var config = Config(c => c.EnableMovies = false);
        var scanner = BuildScanner(new BaseItem[] { movie }, config);

        await scanner.RunAsync(CancellationToken.None, new Progress<double>());

        Assert.Empty(config.Tracked);
    }
}
