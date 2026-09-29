using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;

namespace Horizon.Stealth.Services;

public class ThroughputSample
{
    public DateTime TimeUtc { get; set; }
    public double Bps { get; set; }
}

public static class VpnThroughputHistoryService
{
    private static readonly string _dir  = Path.Combine(LogService.UserDataRoot, "vpn_history");
    private static readonly string _path = Path.Combine(_dir, "throughput.jsonl");

    private const int SampleIntervalSeconds = 30;
    private const int MaxStoredSamples = 5000;
    private const int MaxReturnedSamples = 500;

    private static readonly object _lock = new();
    private static DispatcherTimer? _timer;
    private static long _lastBytes;
    private static DateTime _lastSampleUtc;
    private static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            Directory.CreateDirectory(_dir);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnThroughputHistoryService.Start");
            return;
        }

        _lastBytes = VpnRelayService.TotalBytes;
        _lastSampleUtc = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SampleIntervalSeconds) };
        _timer.Tick += (s, e) => Sample();
        _timer.Start();
    }

    private static void Sample()
    {
        try
        {
            var now = DateTime.UtcNow;
            var nowBytes = VpnRelayService.TotalBytes;
            var elapsed = (now - _lastSampleUtc).TotalSeconds;
            if (elapsed <= 0) return;

            var connected = VpnRelayService.State == VpnRelayState.Connected;
            var bps = connected ? (nowBytes - _lastBytes) / elapsed : 0.0;

            _lastBytes = nowBytes;
            _lastSampleUtc = now;

            var line = JsonSerializer.Serialize(new ThroughputSample { TimeUtc = now, Bps = Math.Max(0, bps) });

            lock (_lock)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
                TrimIfNeeded();
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnThroughputHistoryService.Sample");
        }
    }

    private static void TrimIfNeeded()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var lines = File.ReadAllLines(_path);
            if (lines.Length <= MaxStoredSamples) return;

            var trimmed = lines.Skip(lines.Length - MaxStoredSamples);
            File.WriteAllLines(_path, trimmed);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnThroughputHistoryService.Trim");
        }
    }

    public static List<ThroughputSample> GetRecentSamples(int max = MaxReturnedSamples)
    {
        var result = new List<ThroughputSample>();
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return result;

                foreach (var line in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var sample = JsonSerializer.Deserialize<ThroughputSample>(line);
                    if (sample != null) result.Add(sample);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnThroughputHistoryService.GetRecentSamples");
        }

        return result.Count > max ? result.Skip(result.Count - max).ToList() : result;
    }
}