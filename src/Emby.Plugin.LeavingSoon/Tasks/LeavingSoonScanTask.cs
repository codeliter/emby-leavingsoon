using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.LeavingSoon.Services;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugin.LeavingSoon.Tasks;

public class LeavingSoonScanTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ILogger _logger;

    public LeavingSoonScanTask(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ICollectionManager collectionManager,
        ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _collectionManager = collectionManager;
        _logger = logManager.GetLogger("LeavingSoon");
    }

    public string Name => "Leaving Soon Scan";

    public string Key => "LeavingSoonScan";

    public string Description => "Finds stale movies and series, maintains the Leaving Soon collection, and removes items past their grace period via Sonarr/Radarr.";

    public string Category => "Leaving Soon";

    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var scanner = new LeavingSoonScanner(_libraryManager, _userManager, _userDataManager, _collectionManager, _logger);
        await scanner.RunAsync(cancellationToken, progress).ConfigureAwait(false);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        };
    }
}
