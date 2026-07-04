using Liquidcast.Api.Models;

namespace Liquidcast.Api.Services;

/// <summary>Expands recurring schedule slots into concrete occurrences around a moment in time.</summary>
public static class SlotOccurrence
{
    /// <summary>
    /// Returns the occurrence start for <paramref name="slot"/> active at <paramref name="nowUtc"/>,
    /// or null if the slot is not active then. Handles None/Daily/Weekly recurrence including
    /// occurrences that started "yesterday" and run past midnight.
    /// </summary>
    public static DateTime? ActiveStart(ScheduleSlot slot, DateTime nowUtc)
    {
        var duration = slot.EndUtc - slot.StartUtc;
        if (duration <= TimeSpan.Zero) return null;

        switch (slot.Recurrence)
        {
            case Recurrence.None:
                return (slot.StartUtc <= nowUtc && nowUtc < slot.EndUtc) ? slot.StartUtc : null;

            case Recurrence.Daily:
                // Check an occurrence anchored to today and one anchored to yesterday (for spans past midnight).
                foreach (var dayOffset in new[] { 0, -1 })
                {
                    var candidate = nowUtc.Date.AddDays(dayOffset) + slot.StartUtc.TimeOfDay;
                    if (candidate < slot.StartUtc.Date) continue; // before the series began
                    if (candidate <= nowUtc && nowUtc < candidate + duration) return candidate;
                }
                return null;

            case Recurrence.Weekly:
                foreach (var dayOffset in new[] { 0, -1, -7 })
                {
                    var candidate = nowUtc.Date.AddDays(dayOffset) + slot.StartUtc.TimeOfDay;
                    if (candidate.DayOfWeek != slot.StartUtc.DayOfWeek) continue;
                    if (candidate < slot.StartUtc.Date) continue;
                    if (candidate <= nowUtc && nowUtc < candidate + duration) return candidate;
                }
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the next future occurrence start for <paramref name="slot"/> strictly after
    /// <paramref name="nowUtc"/>, or null if it never occurs again. Mirrors
    /// <see cref="ActiveStart"/> but looks forward instead of back.
    /// </summary>
    public static DateTime? NextStart(ScheduleSlot slot, DateTime nowUtc)
    {
        var duration = slot.EndUtc - slot.StartUtc;
        if (duration <= TimeSpan.Zero) return null;

        switch (slot.Recurrence)
        {
            case Recurrence.None:
                return slot.StartUtc > nowUtc ? slot.StartUtc : null;

            case Recurrence.Daily:
                for (var dayOffset = 0; dayOffset <= 1; dayOffset++)
                {
                    var candidate = nowUtc.Date.AddDays(dayOffset) + slot.StartUtc.TimeOfDay;
                    if (candidate < slot.StartUtc.Date) continue;
                    if (candidate > nowUtc) return candidate;
                }
                return null;

            case Recurrence.Weekly:
                for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
                {
                    var candidate = nowUtc.Date.AddDays(dayOffset) + slot.StartUtc.TimeOfDay;
                    if (candidate.DayOfWeek != slot.StartUtc.DayOfWeek) continue;
                    if (candidate < slot.StartUtc.Date) continue;
                    if (candidate > nowUtc) return candidate;
                }
                return null;

            default:
                return null;
        }
    }
}
