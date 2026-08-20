using System.Collections.Generic;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.LeavingSoon.Api;

[Route("/LeavingSoon/Candidates", "GET", Summary = "Lists items currently tracked in the Leaving Soon collection")]
public class GetCandidates : IReturn<List<CandidateDto>>
{
}

[Route("/LeavingSoon/Approve/{ItemId}", "POST", Summary = "Approves removal of a tracked item")]
public class ApproveItem : IReturnVoid
{
    public string ItemId { get; set; } = string.Empty;
}

[Route("/LeavingSoon/Rescue/{ItemId}", "POST", Summary = "Removes an item from tracking without deleting it")]
public class RescueItem : IReturnVoid
{
    public string ItemId { get; set; } = string.Empty;
}

[Route("/LeavingSoon/Audit", "GET", Summary = "Returns the removal audit log")]
public class GetAudit : IReturn<List<Configuration.AuditEntry>>
{
}

public class CandidateDto
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int? SeasonNumber { get; set; }
    public string AddedToCollectionUtc { get; set; } = string.Empty;
    public bool Approved { get; set; }
}

[Authenticated(Roles = "Admin")]
public class LeavingSoonApiService : IService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

    public LeavingSoonApiService(ILibraryManager libraryManager, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _logger = logManager.GetLogger("LeavingSoon");
    }

    public List<CandidateDto> Get(GetCandidates request)
    {
        var result = new List<CandidateDto>();
        foreach (var t in Plugin.Instance.Configuration.Tracked)
        {
            if (t.RemovedUtc != null)
            {
                continue;
            }

            result.Add(new CandidateDto
            {
                ItemId = t.ItemId,
                Name = t.Name,
                MediaType = t.MediaType,
                SeasonNumber = t.SeasonNumber,
                AddedToCollectionUtc = t.AddedToCollectionUtc.ToString("o"),
                Approved = t.Approved
            });
        }

        return result;
    }

    public void Post(ApproveItem request)
    {
        var entry = Plugin.Instance.Configuration.Tracked.Find(t => t.ItemId == request.ItemId);
        if (entry == null)
        {
            return;
        }

        entry.Approved = true;
        Plugin.Instance.SaveConfiguration();
        _logger.Info($"[LeavingSoon] Approved removal of {entry.Name}");
    }

    public void Post(RescueItem request)
    {
        var entry = Plugin.Instance.Configuration.Tracked.Find(t => t.ItemId == request.ItemId);
        if (entry == null)
        {
            return;
        }

        Plugin.Instance.Configuration.Tracked.Remove(entry);
        Plugin.Instance.SaveConfiguration();
        _logger.Info($"[LeavingSoon] Rescued {entry.Name} from tracking");
    }

    public List<Configuration.AuditEntry> Get(GetAudit request)
    {
        return Plugin.Instance.Configuration.AuditLog;
    }
}
