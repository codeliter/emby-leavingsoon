using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.LeavingSoon.Services;

public interface IMediaRemover
{
    Task<int?> FindMovieIdByTmdbAsync(int tmdbId, CancellationToken cancellationToken);
    Task DeleteMovieAsync(int movieId, bool deleteFiles, CancellationToken cancellationToken);
    Task<int?> FindSeriesIdByTvdbAsync(int tvdbId, CancellationToken cancellationToken);
    Task DeleteSeriesAsync(int seriesId, bool deleteFiles, CancellationToken cancellationToken);
}
