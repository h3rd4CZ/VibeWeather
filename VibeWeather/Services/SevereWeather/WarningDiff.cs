namespace VibeWeather.Services.SevereWeather;

/// <summary>What a check changes: the warnings now active, those to notify about, and ids whose notifications go.</summary>
public sealed record WarningChanges(IReadOnlyList<SevereWarning> Active, IReadOnlyList<SevereWarning> Notify, IReadOnlyList<string> Remove);

/// <summary>Compares a check's warnings with those stored from the previous check of the same place.</summary>
public static class WarningDiff
{
    static readonly string[] Ended = ["Cancel", "Expire"];
    static readonly string[] Changed = ["Update", "Upgrade", "Extend", "Correct"];

    /// <summary>
    /// A warning not seen before notifies; a known one notifies again only when its status says it changed and its
    /// status, end or name differ from what was stored. Cancelled, expired, ended or missing warnings are removed.
    /// </summary>
    public static WarningChanges Compare(IReadOnlyList<SevereWarning> stored, IReadOnlyList<SevereWarning> current, DateTimeOffset now)
    {
        var before = stored.DistinctBy(w => w.Id).ToDictionary(w => w.Id);
        var active = current
            .DistinctBy(w => w.Id)
            .Where(w => !Ended.Contains(w.Status) && w.End > now)
            .ToList();
        var notify = active
            .Where(w => !before.TryGetValue(w.Id, out var old)
                || (Changed.Contains(w.Status) && (w.Status != old.Status || w.End != old.End || w.Name != old.Name)))
            .ToList();
        var activeIds = active.Select(w => w.Id).ToHashSet();
        var remove = before.Keys.Where(id => !activeIds.Contains(id)).ToList();
        return new WarningChanges(active, notify, remove);
    }
}
