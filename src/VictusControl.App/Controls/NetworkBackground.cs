using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VictusControl.App.Controls;

/// <summary>Non-interactive composition surface, so WPF controls stay above Vanta.</summary>
public sealed class NetworkBackground : Grid, IDisposable {
    private WebView2CompositionControl? _view;
    private Window? _window;
    private bool _ready;
    private bool _disposed;
    private bool _starting;
    private long _lastPointer;

    public static readonly DependencyProperty IsLightProperty = DependencyProperty.Register(
        nameof(IsLight), typeof(bool), typeof(NetworkBackground),
        new PropertyMetadata(false, OnStateChanged));
    public static readonly DependencyProperty AnimationEnabledProperty = DependencyProperty.Register(
        nameof(AnimationEnabled), typeof(bool), typeof(NetworkBackground),
        new PropertyMetadata(true, OnStateChanged));

    public bool IsLight { get => (bool)GetValue(IsLightProperty); set => SetValue(IsLightProperty, value); }
    public bool AnimationEnabled { get => (bool)GetValue(AnimationEnabledProperty); set => SetValue(AnimationEnabledProperty, value); }

    public NetworkBackground() {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
        var background = (NetworkBackground)sender;
        background.UpdateState();
        if (background.IsLoaded) background.Initialize();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (_disposed || DesignerProperties.GetIsInDesignMode(this)) return;
        _window = Window.GetWindow(this);
        if (_window == null) return;
        _window.IsVisibleChanged += OnVisibilityChanged;
        _window.StateChanged += OnWindowStateChanged;
        _window.PreviewMouseMove += OnMouseMove;
        _window.PreviewTouchMove += OnTouchMove;
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        Initialize();
        UpdateState();
    }

    private async void Initialize() {
        if (_starting || _view != null || _disposed || _window == null ||
            !AnimationEnabled || !SystemParameters.ClientAreaAnimation) return;
        _starting = true;
        try {
            var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Network");
            if (!File.Exists(Path.Combine(assets, "index.html"))) return;
            var view = new WebView2CompositionControl {
                IsHitTestVisible = false,
                Focusable = false,
                DefaultBackgroundColor = System.Drawing.Color.Transparent
            };
            _view = view;
            Children.Add(view);
            view.ApplyTemplate();
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VictusControl", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: cache);
            if (_disposed || _view != view) return;
            await view.EnsureCoreWebView2Async(environment);
            if (_disposed || _view != view) return;
            var core = view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.SetVirtualHostNameToFolderMapping("victus-background.local", assets,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) => {
                if (args.Uri != "https://victus-background.local/index.html") args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += (_, args) => {
                if (args.Source != "https://victus-background.local/index.html") return;
                using var message = JsonDocument.Parse(args.WebMessageAsJson);
                if (message.RootElement.GetProperty("type").GetString() == "ready") {
                    _ready = true;
                    UpdateState();
                }
            };
            core.ProcessFailed += (_, _) => ReleaseView();
            core.Navigate("https://victus-background.local/index.html");
        } catch (Exception ex) {
            // Missing WebView2 / WebGL is decorative failure only; keep the native UI usable.
            Debug.WriteLine($"Network background unavailable: {ex.Message}");
            ReleaseView();
        } finally {
            _starting = false;
        }
    }

    private void UpdateState() => Post(new {
        type = "state", light = IsLight,
        active = AnimationEnabled && SystemParameters.ClientAreaAnimation &&
                 IsLoaded && _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized
    });

    private void Post(object message) {
        if (!_ready || _disposed || _view?.CoreWebView2 == null) return;
        try { _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) {
            Debug.WriteLine($"Network background stopped: {ex.Message}");
            ReleaseView();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => ForwardPointer(e.GetPosition(this));
    private void OnTouchMove(object? sender, TouchEventArgs e) => ForwardPointer(e.GetTouchPoint(this).Position);

    private void ForwardPointer(Point point) {
        long now = Environment.TickCount64;
        if (!_ready || !AnimationEnabled || !SystemParameters.ClientAreaAnimation ||
            ActualWidth <= 0 || ActualHeight <= 0 || now - _lastPointer < 33) return;
        _lastPointer = now;
        Post(new { type = "pointer", x = point.X / ActualWidth, y = point.Y / ActualHeight });
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateState();
    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateState();
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        Dispatcher.InvokeAsync(() => { UpdateState(); Initialize(); });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
        UpdateState();
        if (_window != null) {
            _window.IsVisibleChanged -= OnVisibilityChanged;
            _window.StateChanged -= OnWindowStateChanged;
            _window.PreviewMouseMove -= OnMouseMove;
            _window.PreviewTouchMove -= OnTouchMove;
            _window = null;
        }
        SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        ReleaseView();
    }

    private void ReleaseView() {
        _ready = false;
        _view?.Dispose();
        _view = null;
        Children.Clear();
    }

    public void Dispose() {
        if (_disposed) return;
        OnUnloaded(this, new RoutedEventArgs());
        _disposed = true;
    }
}
