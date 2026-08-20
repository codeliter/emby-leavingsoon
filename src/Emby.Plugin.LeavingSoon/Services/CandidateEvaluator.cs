using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugin.LeavingSoon.Services;

public class PlayState
{
    public bool Played { get; set; }
    public DateTime? LastPlayedDateUtc { get; set; }
}

public static class CandidateEvaluator
{
    public static bool IsStale(IEnumerable<PlayState> playStates, DateTime cutoffUtc)
    {
        DateTime? latestPlay = null;
        var anyPlayed = false;

        foreach (var state in playStates)
        {
            if (!state.Played)
            {
                continue;
            }

            anyPlayed = true;
            if (state.LastPlayedDateUtc.HasValue && (latestPlay == null || state.LastPlayedDateUtc > latestPlay))
            {
                latestPlay = state.LastPlayedDateUtc;
            }
        }

        if (!anyPlayed)
        {
            return true;
        }

        return latestPlay.HasValue && latestPlay.Value < cutoffUtc;
    }

    public static bool IsExcluded(DateTime dateCreatedUtc, DateTime minAgeUtc, IReadOnlyCollection<string> itemTags, ISet<string> excludedTags, bool anyUserFavorite)
    {
        if (dateCreatedUtc > minAgeUtc)
        {
            return true;
        }

        if (itemTags.Any(excludedTags.Contains))
        {
            return true;
        }

        return anyUserFavorite;
    }

    public static bool IsDueForRemoval(DateTime addedToCollectionUtc, bool approved, int gracePeriodDays, Configuration.RemovalMode mode, DateTime nowUtc)
    {
        var due = addedToCollectionUtc <= nowUtc.AddDays(-gracePeriodDays);
        if (!due)
        {
            return false;
        }

        return mode == Configuration.RemovalMode.Automatic || approved;
    }
}
