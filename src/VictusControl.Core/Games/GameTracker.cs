using System.Diagnostics;
using System.Text.Json;
using VictusControl.Core.Monitoring;

namespace VictusControl.Core.Games;

public sealed class GameRecord {
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ExecutablePath { get; set; }
    public DateTime LastPlayedUtc { get; set; }
    public double TotalMinutes { get; set; }
    public int SessionCount { get; set; }
    public bool Hidden { get; set; }
    public bool IsRunning { get; set; }
    public List<GameSessionSummary> RecentSessions { get; set; } = new();
}

public sealed record GameProcessInfo(string Name, string? Path, bool HasWindow, DateTime StartedUtc);

/// <summary>Detect sustained 3D activity and checkpoint bounded, local session summaries.</summary>
public sealed class GameTracker {
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase) {
        "dwm", "explorer", "shellexperiencehost", "searchhost", "startmenuexperiencehost",
        "applicationframehost", "textinputhost", "systemsettings", "lockapp",
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "browser",
        "discord", "slack", "teams", "zoom", "whatsapp", "telegram", "spotify",
        "code", "devenv", "rider64", "idea64", "pycharm64", "sublime_text", "notepad++",
        "obs64", "obs32", "streamlabs obs", "nvidia share", "nvcontainer", "nvidia overlay",
        "photoshop", "illustrator", "afterfx", "premiere", "blender", "unity", "unrealeditor",
        "steam", "steamwebhelper", "epicgameslauncher", "galaxyclient", "battle.net",
        "eadesktop", "ubisoftconnect", "riotclientux", "origin",
        "msedgewebview2", "codex", "victuscontrol.app", "claude", "cursor", "windowsterminal", "powershell", "cmd",
        "taskmgr", "perfmon", "hwinfo64", "msiafterburner", "rtss", "omen gaming hub",
        "widgets", "phoneexperiencehost", "gamebar", "gamebarftserver", "xboxgamebar"
    };
    private readonly object _gate = new();
    private readonly string _storePath;
    private readonly Func<int, GameProcessInfo?> _processInfo;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, GameRecord> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (DateTime start, int hits)> _candidateHits = new();
    private readonly Dictionary<int, (string key, DateTime processStartUtc, DateTime lastSeenUtc, GameSessionSummary summary)> _sessions = new();
    private DateTime _lastSaveUtc;
    private DateTime? _lastTelemetryUtc;
    private bool _stopped;

    public GameTracker(string? storePath = null, Func<int, GameProcessInfo?>? processInfo = null, Func<DateTime>? clock = null) {
        _processInfo = processInfo ?? ReadProcess;
        _clock = clock ?? (() => DateTime.UtcNow);
        _storePath = storePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VictusControl", "games.json");
        Load();
    }

    private static GameProcessInfo? ReadProcess(int pid) {
        try {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return null;
            string? path = null;
            try { path = process.MainModule?.FileName; } catch { }
            return new(process.ProcessName, path, process.MainWindowHandle != IntPtr.Zero, process.StartTime.ToUniversalTime());
        } catch { return null; }
    }

    public bool Observe(Dictionary<int, double> gpuByPid) {
        lock (_gate) {
            if (_stopped) return false;
            var now = _clock();
            bool changed = false;
            var active = new HashSet<int>();
            var eligible = new HashSet<int>();
            foreach (var (pid, usage) in gpuByPid) {
                if (usage < 8 || !double.IsFinite(usage)) continue;
                var process = _processInfo(pid);
                if (process == null || NotGames.Contains(process.Name)) continue;
                string key = (process.Path ?? process.Name).ToLowerInvariant();
                if (_games.TryGetValue(key, out var excluded) && excluded.Hidden) continue;
                if (_sessions.TryGetValue(pid, out var prior) && prior.processStartUtc != process.StartedUtc) {
                    EndSession(pid, "Process ended"); changed = true;
                }
                if (!process.HasWindow && !_sessions.ContainsKey(pid)) continue;
                eligible.Add(pid);
                if (!_sessions.ContainsKey(pid)) {
                    var candidate = _candidateHits.GetValueOrDefault(pid);
                    int hits = candidate.start == process.StartedUtc ? candidate.hits + 1 : 1;
                    _candidateHits[pid] = (process.StartedUtc, hits);
                    if (hits < 2) continue;
                }
                active.Add(pid);
                if (!_sessions.TryGetValue(pid, out var session)) {
                    var summary = new GameSessionSummary { StartedUtc = now, LastObservedUtc = now };
                    _sessions[pid] = (key, process.StartedUtc, now, summary);
                    if (!_games.TryGetValue(key, out var game)) {
                        game = new GameRecord { Key = key, Name = FriendlyName(process.Path, process.Name), ExecutablePath = process.Path };
                        _games[key] = game;
                    }
                    game.RecentSessions.Insert(0, summary);
                    if (game.RecentSessions.Count > 20) game.RecentSessions.RemoveRange(20, game.RecentSessions.Count - 20);
                    game.SessionCount++; game.IsRunning = true; game.LastPlayedUtc = now;
                    changed = true;
                } else {
                    _sessions[pid] = (session.key, session.processStartUtc, now, session.summary);
                }
            }
            foreach (var pid in _sessions.Keys.ToArray()) {
                if (active.Contains(pid)) continue;
                var alive = _processInfo(pid);
                bool stillAlive = alive != null && alive.StartedUtc == _sessions[pid].processStartUtc;
                if (stillAlive && now - _sessions[pid].lastSeenUtc < TimeSpan.FromSeconds(60)) continue;
                EndSession(pid, stillAlive ? "Rendering stopped" : "Process ended");
                changed = true;
            }
            foreach (var pid in _candidateHits.Keys.ToArray())
                if (!eligible.Contains(pid)) _candidateHits.Remove(pid);
            if (changed) Save();
            return changed;
        }
    }

    public void RecordTelemetry(TelemetrySample sample) {
        lock (_gate) {
            if (_stopped || sample.TimestampUtc <= _lastTelemetryUtc) return;
            var elapsed = _lastTelemetryUtc.HasValue ? (sample.TimestampUtc - _lastTelemetryUtc.Value).TotalSeconds : 0;
            _lastTelemetryUtc = sample.TimestampUtc;
            // Suspend and sampling outages must not inflate measured playtime.
            if (elapsed > 20) elapsed = 0;
            foreach (var group in _sessions.Values.GroupBy(s => s.key)) {
                foreach (var session in group) {
                    session.summary.Record(sample);
                    session.summary.DurationSeconds += Math.Min(elapsed, Math.Max(0, (sample.TimestampUtc - session.summary.StartedUtc).TotalSeconds));
                    session.summary.LastObservedUtc = sample.TimestampUtc;
                }
                if (_games.TryGetValue(group.Key, out var game)) {
                    game.TotalMinutes += Math.Min(elapsed, Math.Max(0, (sample.TimestampUtc - group.Min(s => s.summary.StartedUtc)).TotalSeconds)) / 60;
                    game.LastPlayedUtc = sample.TimestampUtc;
                }
            }
            if (_sessions.Count > 0 && sample.TimestampUtc - _lastSaveUtc >= TimeSpan.FromSeconds(30)) Save();
        }
    }

    private void EndSession(int pid, string reason) {
        var session = _sessions[pid];
        session.summary.EndedUtc = session.summary.LastObservedUtc;
        session.summary.EndReason = reason;
        _sessions.Remove(pid); _candidateHits.Remove(pid);
        if (_games.TryGetValue(session.key, out var game)) game.IsRunning = _sessions.Values.Any(s => s.key == session.key);
    }

    public IReadOnlyList<GameRecord> RecentlyPlayed(int max = 20) {
        lock (_gate) return _games.Values.Where(g => !g.Hidden).OrderByDescending(g => g.IsRunning)
            .ThenByDescending(g => g.LastPlayedUtc).Take(max).Select(g => new GameRecord {
                Key = g.Key, Name = g.Name, ExecutablePath = g.ExecutablePath, LastPlayedUtc = g.LastPlayedUtc,
                TotalMinutes = g.TotalMinutes, SessionCount = g.SessionCount, IsRunning = g.IsRunning,
                RecentSessions = g.RecentSessions.Select(s => s.Copy()).ToList()
            }).ToList();
    }

    public void CompleteSessions() {
        lock (_gate) {
            _stopped = true;
            foreach (var pid in _sessions.Keys.ToArray()) EndSession(pid, "App closed");
            Save();
        }
    }

    public void Forget(string key) {
        lock (_gate) {
            foreach (var pid in _sessions.Where(s => s.Value.key == key).Select(s => s.Key).ToArray()) EndSession(pid, "Excluded");
            if (_games.TryGetValue(key, out var game)) { game.Hidden = true; Save(); }
        }
    }

    public void ClearAll() {
        lock (_gate) { _sessions.Clear(); _candidateHits.Clear(); _games.Clear(); Save(); }
    }

    private void Load() {
        try {
            if (!File.Exists(_storePath)) return;
            var list = JsonSerializer.Deserialize<List<GameRecord>>(File.ReadAllText(_storePath));
            if (list == null) return;
            foreach (var game in list) {
                game.IsRunning = false;
                game.RecentSessions ??= new();
                foreach (var session in game.RecentSessions.Where(s => s.EndedUtc == null)) {
                    session.EndedUtc = session.LastObservedUtc; session.EndReason = "Recording interrupted";
                }
                _games[game.Key] = game;
            }
        } catch { /* Bad history must not stop hardware controls. */ }
    }

    private void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_storePath))!);
            File.WriteAllText(_storePath + ".tmp", JsonSerializer.Serialize(_games.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_storePath + ".tmp", _storePath, true);
            _lastSaveUtc = _clock();
        } catch { /* History must not interrupt monitoring. */ }
    }

    private static string FriendlyName(string? path, string processName) {
        if (path != null) {
            try {
                var info = FileVersionInfo.GetVersionInfo(path);
                foreach (var candidate in new[] { info.ProductName, info.FileDescription }) {
                    if (!string.IsNullOrWhiteSpace(candidate) && candidate.Trim().Length > 1)
                        return candidate.Trim();
                }
            } catch {
                // fall through to the process name
            }
        }
        // "witcher3" -> "Witcher3"; better than nothing when metadata is missing
        return processName.Length > 1
            ? char.ToUpperInvariant(processName[0]) + processName.Substring(1)
            : processName;
    }


}
