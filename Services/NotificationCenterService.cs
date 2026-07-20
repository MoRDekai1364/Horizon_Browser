using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Horizon.Stealth.Services;

public class NotificationEntry
{
    public string Origin    { get; set; } = "";
    public string Title     { get; set; } = "";
    public string Body      { get; set; } = "";
    public DateTime Time    { get; set; } = DateTime.Now;
    public bool IsRead      { get; set; } = false;
}

public static class NotificationCenterService
{
    private const int MaxHistory = 100;

    public static ObservableCollection<NotificationEntry> History { get; } = new();

    public static event Action<NotificationEntry>? NotificationAdded;

    public static int UnreadCount
    {
        get
        {
            int count = 0;
            foreach (var n in History) if (!n.IsRead) count++;
            return count;
        }
    }

    public static void Add(string origin, string title, string body)
    {
        var entry = new NotificationEntry { Origin = origin, Title = title, Body = body };

        void DoAdd()
        {
            History.Insert(0, entry);
            while (History.Count > MaxHistory)
                History.RemoveAt(History.Count - 1);

            NotificationAdded?.Invoke(entry);
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            DoAdd();
        else
            Application.Current?.Dispatcher.Invoke(DoAdd);
    }

    public static void MarkAllRead()
    {
        foreach (var n in History) n.IsRead = true;
    }

    public static void Clear()
    {
        History.Clear();
    }
}