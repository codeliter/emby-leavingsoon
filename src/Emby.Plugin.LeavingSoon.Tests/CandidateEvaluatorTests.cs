using System;
using System.Collections.Generic;
using Emby.Plugin.LeavingSoon.Configuration;
using Emby.Plugin.LeavingSoon.Services;
using Xunit;

namespace Emby.Plugin.LeavingSoon.Tests;

public class CandidateEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    public class IsStale
    {
        [Fact]
        public void NeverPlayed_IsStale()
        {
            var states = new[] { new PlayState { Played = false } };
            Assert.True(CandidateEvaluator.IsStale(states, Now.AddDays(-180)));
        }

        [Fact]
        public void PlayedRecently_NotStale()
        {
            var states = new[] { new PlayState { Played = true, LastPlayedDateUtc = Now.AddDays(-10) } };
            Assert.False(CandidateEvaluator.IsStale(states, Now.AddDays(-180)));
        }

        [Fact]
        public void PlayedLongAgo_IsStale()
        {
            var states = new[] { new PlayState { Played = true, LastPlayedDateUtc = Now.AddDays(-200) } };
            Assert.True(CandidateEvaluator.IsStale(states, Now.AddDays(-180)));
        }

        [Fact]
        public void OneUserPlayedRecently_OthersStale_NotStale()
        {
            var states = new[]
            {
                new PlayState { Played = true, LastPlayedDateUtc = Now.AddDays(-300) },
                new PlayState { Played = true, LastPlayedDateUtc = Now.AddDays(-5) },
                new PlayState { Played = false }
            };
            Assert.False(CandidateEvaluator.IsStale(states, Now.AddDays(-180)));
        }

        [Fact]
        public void AllUsersStale_IsStale()
        {
            var states = new[]
            {
                new PlayState { Played = true, LastPlayedDateUtc = Now.AddDays(-300) },
                new PlayState { Played = true, LastPlayedDateUtc = Now.AddDays(-250) }
            };
            Assert.True(CandidateEvaluator.IsStale(states, Now.AddDays(-180)));
        }

        [Fact]
        public void PlayedExactlyAtCutoff_NotStale()
        {
            var cutoff = Now.AddDays(-180);
            var states = new[] { new PlayState { Played = true, LastPlayedDateUtc = cutoff } };
            Assert.False(CandidateEvaluator.IsStale(states, cutoff));
        }

        [Fact]
        public void PlayedWithoutDate_NotStale()
        {
            // Watched but no timestamp — we cannot prove it's old, so keep it.
            var states = new[] { new PlayState { Played = true, LastPlayedDateUtc = null } };
            Assert.False(CandidateEvaluator.IsStale(states, Now.AddDays(-180)));
        }

        [Fact]
        public void NoUsers_IsStale()
        {
            Assert.True(CandidateEvaluator.IsStale(new List<PlayState>(), Now.AddDays(-180)));
        }
    }

    public class IsExcluded
    {
        private static readonly HashSet<string> ExcludedTags = new(StringComparer.OrdinalIgnoreCase) { "keep" };

        [Fact]
        public void TooNew_IsExcluded()
        {
            var created = Now.AddDays(-10);
            Assert.True(CandidateEvaluator.IsExcluded(created, Now.AddDays(-30), Array.Empty<string>(), ExcludedTags, false));
        }

        [Fact]
        public void HasExcludedTag_IsExcluded()
        {
            var created = Now.AddDays(-100);
            Assert.True(CandidateEvaluator.IsExcluded(created, Now.AddDays(-30), new[] { "Keep" }, ExcludedTags, false));
        }

        [Fact]
        public void AnyUserFavorite_IsExcluded()
        {
            var created = Now.AddDays(-100);
            Assert.True(CandidateEvaluator.IsExcluded(created, Now.AddDays(-30), Array.Empty<string>(), ExcludedTags, true));
        }

        [Fact]
        public void OldUnfavoritedUntagged_NotExcluded()
        {
            var created = Now.AddDays(-100);
            Assert.False(CandidateEvaluator.IsExcluded(created, Now.AddDays(-30), new[] { "other" }, ExcludedTags, false));
        }

        [Fact]
        public void EmptyExclusionSet_TagsNeverMatch()
        {
            var created = Now.AddDays(-100);
            Assert.False(CandidateEvaluator.IsExcluded(created, Now.AddDays(-30), new[] { "keep" }, new HashSet<string>(StringComparer.OrdinalIgnoreCase), false));
        }
    }

    public class IsDueForRemoval
    {
        [Fact]
        public void WithinGracePeriod_NotDue()
        {
            var added = Now.AddDays(-5);
            Assert.False(CandidateEvaluator.IsDueForRemoval(added, approved: true, 14, RemovalMode.Automatic, Now));
        }

        [Fact]
        public void PastGrace_AutomaticMode_Due()
        {
            var added = Now.AddDays(-20);
            Assert.True(CandidateEvaluator.IsDueForRemoval(added, approved: false, 14, RemovalMode.Automatic, Now));
        }

        [Fact]
        public void PastGrace_ManualModeUnapproved_NotDue()
        {
            var added = Now.AddDays(-20);
            Assert.False(CandidateEvaluator.IsDueForRemoval(added, approved: false, 14, RemovalMode.Manual, Now));
        }

        [Fact]
        public void PastGrace_ManualModeApproved_Due()
        {
            var added = Now.AddDays(-20);
            Assert.True(CandidateEvaluator.IsDueForRemoval(added, approved: true, 14, RemovalMode.Manual, Now));
        }

        [Fact]
        public void ExactlyAtGraceBoundary_Due()
        {
            var added = Now.AddDays(-14);
            Assert.True(CandidateEvaluator.IsDueForRemoval(added, approved: false, 14, RemovalMode.Automatic, Now));
        }
    }
}
