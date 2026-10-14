using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VictusControl.Core.Bios;
using VictusControl.Core.Games;
using VictusControl.Core.Monitoring;

namespace VictusControl.App;

public partial class MainWindow {
    private readonly TelemetryHistory _history = new();
    private readonly QuickControlsWindow _quickControls = new();
    private readonly HashSet<string> _expandedGames = new();
    private int _historyMinutes = 5;
    private DateTime _gamesRefreshUtc;
    private bool _hardwareBusy;
    private bool? _maxFanReadback;
    private int _commandRevision;

    private void InitializeInsights() {
        _tray.QuickControlsRequested += () => {
            _quickControls.UpdateControls(_biosAvailable, _hardwareBusy, _maxFanReadback);
            _quickControls.ShowNearTray();
        };
        _quickControls.FanModeRequested += mode => Send(() => _bios.SetFanMode(mode), $"Fan mode set to {mode}.");
        _quickControls.MaxFanRequested += () => {
            if (_maxFanReadback is not bool state) return;
            Send(() => _bios.SetMaxFan(!state), state ? "Max fan released." : "Max fan engaged.");
        };
        _quickControls.ThemeRequested += light => ThemeSwitch.IsChecked = light;
        _quickControls.DashboardRequested += history => {
            if (history) NavHistory.IsChecked = true;
            RestoreFromTray();
        };
        IsVisibleChanged += (_, _) => _windowVisible = IsVisible && WindowState != WindowState.Minimized;
        StateChanged += (_, _) => _windowVisible = IsVisible && WindowState != WindowState.Minimized;
        _quickControls.UpdateControls(_biosAvailable, false, null);
    }

    private void UpdateInsights(PollResult result) {
        if (result.Telemetry is { } sample) _history.Add(sample);
        if (NavHistory.IsChecked == true && _windowVisible) RefreshHistory();
        if (NavGames.IsChecked == true && DateTime.UtcNow - _gamesRefreshUtc > TimeSpan.FromSeconds(10)) {
            _gamesRefreshUtc = DateTime.UtcNow;
            BuildGames();
        }
        var running = _games.RecentlyPlayed().FirstOrDefault(g => g.IsRunning);
        var gpu = result.Snapshot.Gpu ?? _lastGpu;
        _quickControls.UpdateTelemetry(result.Snapshot.CpuTempC, gpu is { Available: true } ? gpu.TemperatureC : null,
            CpuFanText.Text, GpuFanText.Text, running == null ? "No game session active" : $"Recording · {running.Name}");
        _quickControls.UpdateControls(_biosAvailable, _hardwareBusy, _maxFanReadback);
    }

    private void OnHistoryRangeChanged(object sender, RoutedEventArgs e) {
        if (sender is RadioButton { Tag: string text } && int.TryParse(text, out int minutes)) _historyMinutes = minutes;
        if (TemperatureHistory != null) RefreshHistory();
    }
    private void RefreshHistory() {
        var samples = _history.Snapshot();
        var now = DateTime.UtcNow;
        TemperatureHistory.SetSamples(samples, now, _historyMinutes);
        UsageHistory.SetSamples(samples, now, _historyMinutes);
        FanHistory.SetSamples(samples, now, _historyMinutes);
    }

    private UIElement SessionDetails(GameRecord game) {
        var stack = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        if (game.RecentSessions.Count == 0) {
            stack.Children.Add(new TextBlock { Text = "Session summaries will appear after your next detected game session.", Style = (Style)FindResource("LabelMuted") });
            return stack;
        }
        stack.Children.Add(SessionSummary(game.RecentSessions[0]));
        if (game.RecentSessions.Count > 1) {
            var previous = new StackPanel();
            foreach (var summary in game.RecentSessions.Skip(1)) previous.Children.Add(SessionSummary(summary));
            var expander = new Expander {
                Header = $"Previous sessions ({game.RecentSessions.Count - 1})", Content = previous,
                IsExpanded = _expandedGames.Contains(game.Key), Margin = new Thickness(0, 12, 0, 0)
            };
            expander.SetResourceReference(ForegroundProperty, "TextSecondary");
            expander.Expanded += (_, _) => _expandedGames.Add(game.Key);
            expander.Collapsed += (_, _) => _expandedGames.Remove(game.Key);
            stack.Children.Add(expander);
        }
        return stack;
    }

    private UIElement SessionSummary(GameSessionSummary session) {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        string state = session.EndedUtc.HasValue ? session.EndReason : "LIVE";
        stack.Children.Add(new TextBlock {
            Text = $"{session.StartedUtc.ToLocalTime():dd MMM · HH:mm}  /  {PlaytimeText(session.DurationSeconds / 60)} recorded  /  {state}",
            Style = (Style)FindResource("LabelMuted"), Margin = new Thickness(0, 0, 0, 10)
        });
        var stats = new WrapPanel();
        void Stat(string label, double? value, string suffix) {
            var cell = new StackPanel { Width = 142, Margin = new Thickness(0, 0, 12, 12) };
            var figure = new TextBlock { Text = value.HasValue ? $"{value:0.#}{suffix}" : "—", Style = (Style)FindResource("StatBig"), FontSize = 20 };
            cell.Children.Add(figure);
            cell.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("LabelMuted") });
            stats.Children.Add(cell);
        }
        Stat("CPU average usage", session.CpuUsage.Average, "%");
        Stat("GPU average usage", session.GpuUsage.Average, "%");
        Stat("CPU peak temperature", session.CpuTemperature.Peak, "°C");
        Stat("GPU peak temperature", session.GpuTemperature.Peak, "°C");
        Stat("CPU peak fan", session.CpuFan.Peak, " rpm");
        Stat("GPU peak fan", session.GpuFan.Peak, " rpm");
        Stat("Throttling events", session.ThrottlingSamples > 0 ? session.ThrottlingEvents : null, "");
        stack.Children.Add(stats);
        stack.Children.Add(new TextBlock {
            Text = $"System-wide readings during this session · sample averages · CPU {session.CpuUsage.Count} / GPU {session.GpuUsage.Count} samples",
            Style = (Style)FindResource("LabelMuted")
        });
        return stack;
    }
}
