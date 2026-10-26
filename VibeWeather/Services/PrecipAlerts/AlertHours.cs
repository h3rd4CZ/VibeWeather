namespace VibeWeather.Services.PrecipAlerts;

/// <summary>
/// When precipitation checks run: from–to on weekdays (Monday–Friday) and on weekends, in device time. A range whose
/// end isn't after its start runs past midnight (22:00–01:00), and belongs to the day it starts on; equal times
/// mean all day.
/// </summary>
public sealed record AlertHours(TimeOnly WeekdayFrom, TimeOnly WeekdayTo, TimeOnly WeekendFrom, TimeOnly WeekendTo)
{
    public static readonly AlertHours Default = new(new(6, 0), new(22, 0), new(8, 0), new(22, 0));

    /// <summary><paramref name="time"/> if it falls within alert hours, else when they next start.</summary>
    public DateTime NextAllowed(DateTime time)
    {
        var today = DateOnly.FromDateTime(time);
        // From yesterday, whose range may run past midnight. Ranges start in order, one per day, so the first one
        // that hasn't ended either contains the time or is the next to start.
        for (var date = today.AddDays(-1); date <= today.AddDays(1); date = date.AddDays(1))
        {
            var (start, end) = Range(date);
            if (time < end) return time < start ? start : time;
        }
        return time;
    }

    (DateTime Start, DateTime End) Range(DateOnly date)
    {
        var (from, to) = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? (WeekendFrom, WeekendTo)
            : (WeekdayFrom, WeekdayTo);
        return (date.ToDateTime(from), (to > from ? date : date.AddDays(1)).ToDateTime(to));
    }
}
