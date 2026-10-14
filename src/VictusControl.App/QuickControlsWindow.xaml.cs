using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using VictusControl.Core.Bios;

namespace VictusControl.App;

public partial class QuickControlsWindow : Window {
    public event Action<HpBiosData.FanMode>? FanModeRequested;
    public event Action? MaxFanRequested;
    public event Action<bool>? ThemeRequested;
    public event Action<bool>? DashboardRequested;
    public QuickControlsWindow() {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
    }

    public void ShowNearTray() {
        if (IsVisible) { Hide(); return; }
        LightSwitch.IsChecked = ThemeManager.Current == AppTheme.Light;
        // Place in physical screen coordinates first; convert through this window's
        // current transform after moving to the cursor's monitor (mixed DPI setups).
        var cursor = System.Windows.Forms.Cursor.Position;
        var work = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        var transform = source.CompositionTarget.TransformFromDevice;
        var corner = transform.Transform(new Point(work.Right, work.Bottom));
        Left = corner.X - Width - 12; Top = corner.Y - 600;
        Show(); UpdateLayout();
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(work.Left, work.Top));
        corner = transform.Transform(new Point(work.Right, work.Bottom));
        MaxHeight = Math.Max(200, corner.Y - topLeft.Y - 24);
        Left = Math.Max(topLeft.X + 12, corner.X - ActualWidth - 12);
        Top = Math.Max(topLeft.Y + 12, corner.Y - ActualHeight - 12);
        Activate();
    }

    public void UpdateTelemetry(double? cpu, double? gpu, string cpuFan, string gpuFan, string session) {
        CpuReading.Text = cpu.HasValue ? $"{cpu:0}°C" : "—";
        GpuReading.Text = gpu.HasValue ? $"{gpu:0}°C" : "—";
        CpuDetail.Text = cpuFan; GpuDetail.Text = gpuFan; SessionText.Text = session;
    }
    public void UpdateControls(bool available, bool busy, bool? maxFan) {
        FanModes.IsEnabled = available && !busy;
        MaxFanAction.IsEnabled = available && !busy && maxFan.HasValue;
        MaxFanAction.Content = maxFan.HasValue ? (maxFan.Value ? "Max fans · ON — turn off" : "Max fans · OFF — turn on") : "Max fans · unavailable";
        LightSwitch.IsChecked = ThemeManager.Current == AppTheme.Light;
        if (!available) CommandStatus.Text = "Hardware controls unavailable. Administrator access is required.";
    }
    public void SetStatus(string text) => CommandStatus.Text = text;
    private void OnDismiss(object sender, RoutedEventArgs e) => Hide();
    private void OnFanMode(object sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string value } && Enum.TryParse<HpBiosData.FanMode>(value, out var mode)) FanModeRequested?.Invoke(mode);
    }
    private void OnMaxFan(object sender, RoutedEventArgs e) => MaxFanRequested?.Invoke();
    private void OnTheme(object sender, RoutedEventArgs e) => ThemeRequested?.Invoke(LightSwitch.IsChecked == true);
    private void OnDashboard(object sender, RoutedEventArgs e) { Hide(); DashboardRequested?.Invoke(false); }
    private void OnHistory(object sender, RoutedEventArgs e) { Hide(); DashboardRequested?.Invoke(true); }
}
