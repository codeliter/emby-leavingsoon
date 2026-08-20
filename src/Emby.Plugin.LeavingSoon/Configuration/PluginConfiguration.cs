using System;
using System.Collections.Generic;

namespace Emby.Plugin.LeavingSoon.Configuration;

public enum RemovalMode
{
    Manual = 0,
    Automatic = 1
}

public class PluginConfiguration : MediaBrowser.Model.Plugins.BasePluginConfiguration
{
    public int UnwatchedDaysThreshold { get; set; } = 180;
    public int GracePeriodDays { get; set; } = 14;
    public int MinimumLibraryAgeDays { get; set; } = 30;
    public RemovalMode Mode { get; set; } = RemovalMode.Manual;
    public bool DryRun { get; set; } = true;
    public bool DeleteFiles { get; set; } = true;
    public bool EnableMovies { get; set; } = true;
    public bool EnableSeries { get; set; } = true;
    public bool ExcludeFavorites { get; set; } = true;
    public string ExcludedTags { get; set; } = string.Empty;
    public string CollectionName { get; set; } = "Leaving Soon";

    public string RadarrUrl { get; set; } = string.Empty;
    public string RadarrApiKey { get; set; } = string.Empty;
    public string SonarrUrl { get; set; } = string.Empty;
    public string SonarrApiKey { get; set; } = string.Empty;

    public List<TrackedItem> Tracked { get; set; } = new();
    public List<AuditEntry> AuditLog { get; set; } = new();
}

public class TrackedItem
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int? SeasonNumber { get; set; }
    public DateTime AddedToCollectionUtc { get; set; }
    public DateTime? RemovedUtc { get; set; }
    public bool Approved { get; set; }
}

public class AuditEntry
{
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
