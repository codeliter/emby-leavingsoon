using System.Collections.Generic;
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

    public async Task DeleteSeasonFilesAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        var baseUrl = config.SonarrUrl.TrimEnd('/');

        var filesUrl = $"{baseUrl}/api/v3/episodefile?seriesId={seriesId}&seasonNumber={seasonNumber}";
        var filesJson = await SendAsync(HttpMethod.Get, filesUrl, config.SonarrApiKey, null, cancellationToken).ConfigureAwait(false);
        if (filesJson == null)
        {
            return;
        }

        var fileIds = new List<int>();
        using (var doc = JsonDocument.Parse(filesJson))
        {
            foreach (var file in doc.RootElement.EnumerateArray())
            {
                if (file.TryGetProperty("id", out var id))
                {
                    fileIds.Add(id.GetInt32());
                }
            }
        }

        foreach (var fileId in fileIds)
        {
            if (!Plugin.Instance.Configuration.DryRun)
            {
                await SendAsync(HttpMethod.Delete, $"{baseUrl}/api/v3/episodefile/{fileId}", config.SonarrApiKey, null, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.Info($"[LeavingSoon] Sonarr deleted {fileIds.Count} episode files for series id={seriesId} season={seasonNumber} (dryRun={Plugin.Instance.Configuration.DryRun})");

        await SetSeasonMonitoredAsync(seriesId, seasonNumber, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetSeasonMonitoredAsync(int seriesId, int seasonNumber, bool monitored, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        var baseUrl = config.SonarrUrl.TrimEnd('/');

        var seriesJson = await SendAsync(HttpMethod.Get, $"{baseUrl}/api/v3/series/{seriesId}", config.SonarrApiKey, null, cancellationToken).ConfigureAwait(false);
        if (seriesJson == null)
        {
            return;
        }

        using var doc = JsonDocument.Parse(seriesJson);
        var series = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            series[prop.Name] = prop.Value.Clone();
        }

        if (!series.TryGetValue("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var newSeasons = new List<Dictionary<string, JsonElement>>();
        foreach (var season in seasons.EnumerateArray())
        {
            var s = new Dictionary<string, JsonElement>();
            foreach (var prop in season.EnumerateObject())
            {
                s[prop.Name] = prop.Value.Clone();
            }

            if (s.TryGetValue("seasonNumber", out var num) && num.GetInt32() == seasonNumber)
            {
                s["monitored"] = JsonDocument.Parse(monitored ? "true" : "false").RootElement.Clone();
            }

            newSeasons.Add(s);
        }

        var payload = new Dictionary<string, object?>();
        foreach (var kv in series)
        {
            payload[kv.Key] = kv.Value;
        }

        payload["seasons"] = newSeasons;

        if (!Plugin.Instance.Configuration.DryRun)
        {
            await SendAsync(HttpMethod.Put, $"{baseUrl}/api/v3/series/{seriesId}", config.SonarrApiKey, JsonSerializer.Serialize(payload), cancellationToken).ConfigureAwait(false);
        }
    }
}
