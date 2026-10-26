namespace VibeWeather.Services.SevereWeather;

/// <summary>
/// An official warning for the selected place, as stored between checks. Times keep the place's UTC offset, so their
/// clock is the place's. Status is the source's English keyword: New, Continue, Update, Correct, Extend, Upgrade,
/// Cancel or Expire. Id combines the source and its alert number.
/// </summary>
public sealed record SevereWarning(
    string Id, string Name, string Status, string Area, DateTimeOffset Start, DateTimeOffset End,
    string Source, string Details, string? Disclaimer, int Priority)
{
    /// <summary>Priority 1 is the most important; the scale is the provider's, so only the top band shows as severe (red).</summary>
    public bool IsSevere => Priority <= 10;
}
