using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.LeavingSoon.Services;

public class RadarrClient
{
    private static readonly HttpClient Http = new();
    private readonly ILogger _logger;

    public RadarrClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<int?> FindMovieIdByTmdbAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        if (string.IsNullOrWhiteSpace(config.RadarrUrl) || string.IsNullOrWhiteSpace(config.RadarrApiKey))
        {
            return null;
        }

        var url = $"{config.RadarrUrl.TrimEnd('/')}/api/v3/movie?tmdbId={tmdbId}";
        using var doc = await GetJsonAsync(url, config.RadarrApiKey, cancellationToken).ConfigureAwait(false);
        if (doc == null)
        {
            return null;
        }

        foreach (var movie in doc.RootElement.EnumerateArray())
        {
            if (movie.TryGetProperty("id", out var id))
            {
                return id.GetInt32();
            }
        }

        return null;
    }

    public async Task DeleteMovieAsync(int movieId, bool deleteFiles, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        var url = $"{config.RadarrUrl.TrimEnd('/')}/api/v3/movie/{movieId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}&addImportExclusion=false";
        await SendAsync(HttpMethod.Delete, url, config.RadarrApiKey, null, cancellationToken).ConfigureAwait(false);
        _logger.Info($"[LeavingSoon] Radarr deleted movie id={movieId} deleteFiles={deleteFiles}");
    }

    private static async Task<JsonDocument?> GetJsonAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        var body = await SendAsync(HttpMethod.Get, url, apiKey, null, cancellationToken).ConfigureAwait(false);
        return body == null ? null : JsonDocument.Parse(body);
    }

    internal static async Task<string?> SendAsync(HttpMethod method, string url, string apiKey, string? jsonBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Api-Key", apiKey);
        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return response.IsSuccessStatusCode ? content : null;
    }
}
