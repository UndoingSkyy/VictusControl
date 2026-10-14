using System.Text.Json;
using VictusControl.Core.Games;
using VictusControl.Core.Monitoring;

var directory = Path.Combine(Path.GetTempPath(), "VictusChecks-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
int checks = 0;
void Check(bool condition, string message) {
    if (!condition) throw new Exception(message);
    checks++; Console.WriteLine("PASS " + message);
}
var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
TelemetrySample Sample(double? gpu = 70, bool? throttle = false) => new(now, 20, gpu, 65, 60, 2400, 2200, throttle);
var processes = new Dictionary<int, GameProcessInfo> { [42] = new("testgame", null, true, now.AddMinutes(-1)) };
var store = Path.Combine(directory, "games.json");
GameTracker Tracker(string path) => new(path, pid => processes.GetValueOrDefault(pid), () => now);
var tracker = Tracker(store);
void Detect() { tracker.Observe(new() { [42] = 35 }); }
try {
    Detect(); Check(tracker.RecentlyPlayed().Count == 0, "Requires sustained 3D activity");
    Detect(); Check(tracker.RecentlyPlayed().Single().IsRunning, "Starts a confirmed game session");
    tracker.RecordTelemetry(Sample(throttle: true));
    now = now.AddSeconds(5); tracker.RecordTelemetry(Sample(null, null));
    now = now.AddSeconds(5); tracker.RecordTelemetry(Sample(90, true));
    now = now.AddSeconds(5); tracker.RecordTelemetry(Sample(50, false));
    now = now.AddSeconds(5); tracker.RecordTelemetry(Sample(70, true));
    var summary = tracker.RecentlyPlayed().Single().RecentSessions.Single();
    Check(summary.GpuUsage.Count == 4 && summary.GpuUsage.Average == 70, "Missing GPU samples do not lower averages");
    Check(summary.ThrottlingEvents == 2 && summary.ThrottlingSamples == 4, "Counts throttling transitions, ignoring unsampled polls");
    Check(summary.DurationSeconds == 20, "Accumulates measured session duration");
    tracker.RecordTelemetry(Sample());
    Check(tracker.RecentlyPlayed().Single().RecentSessions.Single().CpuUsage.Count == 5, "Duplicate timestamps do not double-count");
    summary.CpuUsage.Sum = 99999;
    Check(tracker.RecentlyPlayed().Single().RecentSessions.Single().CpuUsage.Average == 20, "UI snapshots cannot mutate stored statistics");
    now = now.AddMinutes(10); tracker.RecordTelemetry(Sample());
    Check(tracker.RecentlyPlayed().Single().RecentSessions.Single().DurationSeconds == 20, "Suspend gaps do not inflate playtime");
    var recovered = Tracker(store).RecentlyPlayed().Single();
    Check(!recovered.IsRunning && recovered.RecentSessions.Single().EndReason == "Recording interrupted", "Checkpoints recover safely after interruption");
    processes.Remove(42); tracker.Observe(new());
    Check(!tracker.RecentlyPlayed().Single().IsRunning && tracker.RecentlyPlayed().Single().RecentSessions.Single().EndedUtc != null, "Process exit finalizes session");
    for (int i = 0; i < 22; i++) {
        now = now.AddSeconds(5); processes[42] = new("testgame", null, true, now);
        Detect(); Detect(); tracker.RecordTelemetry(Sample());
        processes.Remove(42); tracker.Observe(new());
    }
    var bounded = tracker.RecentlyPlayed().Single();
    Check(bounded.RecentSessions.Count == 20 && bounded.SessionCount == 23, "Bounds detailed history while retaining lifetime session count");
    now = now.AddSeconds(5); processes[42] = new("testgame", null, true, now);
    Detect(); Detect();
    processes[42] = processes[42] with { StartedUtc = now.AddSeconds(1) };
    Detect(); Check(!tracker.RecentlyPlayed().Single().IsRunning, "PID reuse closes old session and reconfirms detection");
    Detect(); tracker.Forget("testgame"); Detect(); Detect();
    Check(tracker.RecentlyPlayed().Count == 0, "Excluded running games stay excluded");
    tracker.ClearAll(); Detect(); Detect(); tracker.ClearAll(); tracker.RecordTelemetry(Sample());
    Check(tracker.RecentlyPlayed().Count == 0 && Tracker(store).RecentlyPlayed().Count == 0, "Clear history also removes active recording state");
    processes[42] = new("msedgewebview2", null, true, now); Detect(); Detect();
    Check(tracker.RecentlyPlayed().Count == 0, "Animated background cannot be detected as a game");
    processes[42] = new("testgame", null, true, now); Detect();
    processes[42] = processes[42] with { HasWindow = false }; Detect();
    processes[42] = processes[42] with { HasWindow = true }; Detect();
    Check(tracker.RecentlyPlayed().Count == 0, "Ineligible observations reset candidate confirmation");
    Detect(); tracker.CompleteSessions();
    Check(Tracker(store).RecentlyPlayed().Single().RecentSessions.Single().EndReason == "App closed", "Orderly exit persists completed sessions");
    var legacy = Path.Combine(directory, "legacy.json");
    File.WriteAllText(legacy, "[{\"Key\":\"legacy\",\"Name\":\"Legacy game\",\"TotalMinutes\":123,\"SessionCount\":5,\"IsRunning\":true}]");
    var migrated = Tracker(legacy).RecentlyPlayed().Single();
    Check(migrated.TotalMinutes == 123 && migrated.SessionCount == 5 && migrated.RecentSessions.Count == 0 && !migrated.IsRunning, "Older game history migrates without losing totals");
    var history = new TelemetryHistory();
    for (int i = 0; i <= 2400; i++) { now = now.AddSeconds(1); history.Add(Sample()); }
    var samples = history.Snapshot();
    Check(samples.Count == 1801 && samples[^1].TimestampUtc - samples[0].TimestampUtc == TimeSpan.FromMinutes(30), "Rolling history retains exactly the last 30 minutes");
    history.Add(Sample() with { TimestampUtc = now.AddHours(-1) });
    Check(history.Snapshot().Count == 1801, "Rejects out-of-order history samples");
    var stats = new SampleStatistics(); stats.Add(null); stats.Add(double.NaN); stats.Add(-1); stats.Add(0); stats.Add(10);
    Check(stats.Count == 2 && stats.Average == 5 && stats.Peak == 10, "Statistics reject invalid values and preserve real zero");
    Console.WriteLine($"{checks} checks passed.");
} finally { Directory.Delete(directory, true); }
