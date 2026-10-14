using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VictusControl.Core.Monitoring;

namespace VictusControl.App.Controls;

public sealed class HistoryChart : FrameworkElement {
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CpuBrushProperty = DependencyProperty.Register(
        nameof(CpuBrush), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GpuBrushProperty = DependencyProperty.Register(
        nameof(GpuBrush), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush CpuBrush { get => (Brush)GetValue(CpuBrushProperty); set => SetValue(CpuBrushProperty, value); }
    public Brush GpuBrush { get => (Brush)GetValue(GpuBrushProperty); set => SetValue(GpuBrushProperty, value); }
    public string Metric { get; set; } = "Temperature";
    private IReadOnlyList<TelemetrySample> _samples = Array.Empty<TelemetrySample>();
    private DateTime _endUtc;
    private int _minutes = 5;
    private double? _hoverX;

    public HistoryChart() {
        MinHeight = 155;
        MouseMove += (_, e) => { _hoverX = e.GetPosition(this).X; InvalidateVisual(); };
        MouseLeave += (_, _) => { _hoverX = null; InvalidateVisual(); };
        Focusable = false;
    }

    public void SetSamples(IReadOnlyList<TelemetrySample> samples, DateTime endUtc, int minutes) {
        _samples = samples; _endUtc = endUtc; _minutes = minutes; InvalidateVisual();
    }

    private (double? cpu, double? gpu) Values(TelemetrySample s) => Metric switch {
        "Usage" => (s.CpuUsage, s.GpuUsage),
        "Fans" => (s.CpuFanRpm, s.GpuFanRpm),
        _ => (s.CpuTemperature, s.GpuTemperature)
    };

    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        double width = ActualWidth - 54, height = ActualHeight - 50;
        if (width < 40 || height < 30) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var start = _endUtc.AddMinutes(-_minutes);
        var visible = _samples.Where(s => s.TimestampUtc >= start && s.TimestampUtc <= _endUtc).ToArray();
        var valid = visible.SelectMany(s => new[] { Values(s).cpu, Values(s).gpu })
            .Where(v => v is double n && double.IsFinite(n) && n >= 0).Select(v => v!.Value).ToArray();
        double max = Metric == "Usage" ? 100 : Metric == "Fans" ? Math.Max(6000, Math.Ceiling(valid.DefaultIfEmpty(0).Max() / 1000) * 1000) : Math.Max(100, Math.Ceiling(valid.DefaultIfEmpty(0).Max() / 20) * 20);
        string unit = Metric == "Usage" ? "%" : Metric == "Fans" ? " rpm" : "°C";
        double X(DateTime time) => 46 + (time - start).TotalSeconds / (_minutes * 60) * width;
        double Y(double value) => 10 + height * (1 - Math.Clamp(value / max, 0, 1));
        void Text(string text, double x, double y, Brush brush, double size = 11) {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        }
        for (int i = 0; i <= 4; i++) {
            double y = 10 + i * height / 4;
            dc.PushOpacity(.15);
            dc.DrawLine(new Pen(Foreground, 1), new Point(46, y), new Point(46 + width, y));
            dc.Pop();
            Text((max * (4 - i) / 4).ToString("0"), 0, y - 7, Foreground);
        }
        Text($"−{_minutes} min", 46, height + 18, Foreground);
        Text("Now", width + 20, height + 18, Foreground);

        void Line(bool cpu, Brush brush) {
            Point? previous = null; DateTime last = default;
            var pen = new Pen(brush, 1.8);
            foreach (var sample in visible) {
                var value = cpu ? Values(sample).cpu : Values(sample).gpu;
                if (value is not double v || !double.IsFinite(v)) continue;
                var point = new Point(X(sample.TimestampUtc), Y(v));
                if (previous.HasValue && sample.TimestampUtc - last <= TimeSpan.FromSeconds(20))
                    dc.DrawLine(pen, previous.Value, point);
                else dc.DrawEllipse(brush, null, point, 2, 2);
                previous = point; last = sample.TimestampUtc;
            }
        }
        Line(true, CpuBrush); Line(false, GpuBrush);
        if (!valid.Any()) Text("Waiting for sensor samples…", 60, height / 2, Foreground, 13);
        if (_hoverX is double hover && visible.Length > 0) {
            var nearest = visible.MinBy(s => Math.Abs(X(s.TimestampUtc) - hover))!;
            double x = X(nearest.TimestampUtc);
            dc.DrawLine(new Pen(Foreground, 1) { DashStyle = DashStyles.Dot }, new Point(x, 8), new Point(x, height + 10));
            var values = Values(nearest);
            string Format(double? v) => v is double n ? $"{n:0.#}{unit}" : "not sampled";
            ToolTip = $"{nearest.TimestampUtc.ToLocalTime():HH:mm:ss}\nCPU: {Format(values.cpu)}\nGPU: {Format(values.gpu)}";
        }
    }
}
