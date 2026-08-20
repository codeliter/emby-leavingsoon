using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.LeavingSoon.Services;

public class MediaRemover : IMediaRemover
{
    private readonly RadarrClient _radarr;
    private readonly SonarrClient _sonarr;

    public MediaRemover(MediaBrowser.Model.Logging.ILogger logger)
    {
        _radarr = new RadarrClient(logger);
        _sonarr = new SonarrClient(logger);
    }

    public Task<int?> FindMovieIdByTmdbAsync(int tmdbId, CancellationToken cancellationToken) =>
        _radarr.FindMovieIdByTmdbAsync(tmdbId, cancellationToken);

    public Task DeleteMovieAsync(int movieId, bool deleteFiles, CancellationToken cancellationToken) =>
        _radarr.DeleteMovieAsync(movieId, deleteFiles, cancellationToken);

    public Task<int?> FindSeriesIdByTvdbAsync(int tvdbId, CancellationToken cancellationToken) =>
        _sonarr.FindSeriesIdByTvdbAsync(tvdbId, cancellationToken);

    public Task DeleteSeriesAsync(int seriesId, bool deleteFiles, CancellationToken cancellationToken) =>
        _sonarr.DeleteSeriesAsync(seriesId, deleteFiles, cancellationToken);
}
