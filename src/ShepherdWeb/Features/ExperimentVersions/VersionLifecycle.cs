using ShepherdWeb.Infrastructure;
using ShepherdWeb.Infrastructure.Api.Generated;
using ShepherdWeb.Infrastructure.Time;

namespace ShepherdWeb.Features.ExperimentVersions;

/// <summary>
/// What the UI offers for a version, derived from its lifecycle state (docs/ARCHITECTURE.md §3, AD-7).
/// ShepherdAPI decides; this only keeps the UI from offering actions the API would reject.
/// Lock state comes from LockedUTC, never from the status name.
/// </summary>
public static class VersionLifecycle
{
    /// <summary>Configuration is frozen once LockedUTC is set.</summary>
    public static bool IsLocked(ExperimentVersion version) => version.LockedUTC is not null;

    /// <summary>
    /// Statuses the status control may offer. Locked is offered only for a locked version and Draft
    /// only for an unlocked one, so ShepherdWeb can never make StatusID contradict LockedUTC. Locking
    /// itself goes through the Lock action, which sets both together (ShepherdAPI §5.3).
    /// </summary>
    public static IReadOnlyList<Status> OfferableStatuses(
        ExperimentVersion version, IEnumerable<Status> statuses, ConventionsOptions conventions)
    {
        var excluded = IsLocked(version) ? conventions.DraftStatusName : conventions.LockedStatusName;
        return statuses.Where(s => !string.Equals(s.Name, excluded, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// A warning when the status name and LockedUTC disagree, which ShepherdWeb cannot cause but
    /// older data can contain; null when they agree.
    /// </summary>
    public static string? StatusMismatch(ExperimentVersion version, string statusName, ConventionsOptions conventions)
    {
        var saysLocked = string.Equals(statusName, conventions.LockedStatusName, StringComparison.OrdinalIgnoreCase);
        var saysDraft = string.Equals(statusName, conventions.DraftStatusName, StringComparison.OrdinalIgnoreCase);

        if (saysLocked && !IsLocked(version))
        {
            return $"Status is “{statusName}”, but this version has no lock timestamp, so ShepherdAPI does not treat its " +
                   "configuration as locked. Review the configuration, then use Lock to lock it.";
        }

        if (saysDraft && IsLocked(version))
        {
            return $"This version was locked at {Utc.Format(version.LockedUTC!.Value)}, but its status is “{statusName}”. " +
                   "Its configuration is locked. Change the status to reflect that.";
        }

        return null;
    }
}
