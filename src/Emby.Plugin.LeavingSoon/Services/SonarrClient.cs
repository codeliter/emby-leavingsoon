using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using static Emby.Plugin.LeavingSoon.Services.RadarrClient;

namespace Emby.Plugin.LeavingSoon.Services;

public class SonarrClient
{
    private readonly ILogger _logger;

    public SonarrClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<int?> FindSeriesIdByTvdbAsync(int tvdbId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        if (string.IsNullOrWhiteSpace(config.SonarrUrl) || string.IsNullOrWhiteSpace(config.SonarrApiKey))
        {
            return null;
        }

        var url = $"{config.SonarrUrl.TrimEnd('/')}/api/v3/series?tvdbId={tvdbId}";
        using var doc = JsonDocument.Parse(await SendAsync(HttpMethod.Get, url, config.SonarrApiKey, null, cancellationToken).ConfigureAwait(false) ?? "[]");
        foreach (var series in doc.RootElement.EnumerateArray())
        {
            if (series.TryGetProperty("id", out var id))
            {
                return id.GetInt32();
            }
        }

        return null;
    }

    public async Task DeleteSeriesAsync(int seriesId, bool deleteFiles, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        var url = $"{config.SonarrUrl.TrimEnd('/')}/api/v3/series/{seriesId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}&addImportExclusion=false";
        await SendAsync(HttpMethod.Delete, url, config.SonarrApiKey, null, cancellationToken).ConfigureAwait(false);
        _logger.Info($"[LeavingSoon] Sonarr deleted series id={seriesId} deleteFiles={deleteFiles}");
    }
}
