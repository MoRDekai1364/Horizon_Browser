using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Stealth.Services;

public static class CalendarBridge
{
    private static List<CalendarEvent> _events = new();

    public static event Action? Updated;

    public static void Publish(IEnumerable<CalendarEvent> events)
    {
        _events = events.ToList();
        Updated?.Invoke();
    }

    public static CalendarEvent? GetPreviousEvent()
    {
        var now = DateTime.Now;
        return _events.Where(e => e.Start <= now).OrderByDescending(e => e.Start).FirstOrDefault();
    }

    public static CalendarEvent? GetNextEvent()
    {
        var now = DateTime.Now;
        return _events.Where(e => e.Start > now).OrderBy(e => e.Start).FirstOrDefault();
    }
}