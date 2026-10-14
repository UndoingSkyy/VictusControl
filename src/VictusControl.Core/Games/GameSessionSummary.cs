using System.Text.Json.Serialization;
using VictusControl.Core.Monitoring;

namespace VictusControl.Core.Games;

public sealed class SampleStatistics {
    public int Count { get; set; }
    public double Sum { get; set; }
    public double? Peak { get; set; }
    [JsonIgnore] public double? Average => Count > 0 ? Sum / Count : null;
    public void Add(double? value) {
        if (value is not double v || !double.IsFinite(v) || v < 0) return;
        Count++; Sum += v; Peak = Math.Max(Peak ?? v, v);
    }
    public SampleStatistics Copy() => (SampleStatistics)MemberwiseClone();
}

public sealed class GameSessionSummary {
    public DateTime StartedUtc { get; set; }
    public DateTime LastObservedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public string EndReason { get; set; } = "";
    public double DurationSeconds { get; set; }
    public SampleStatistics CpuUsage { get; set; } = new();
    public SampleStatistics GpuUsage { get; set; } = new();
    public SampleStatistics CpuTemperature { get; set; } = new();
    public SampleStatistics GpuTemperature { get; set; } = new();
    public SampleStatistics CpuFan { get; set; } = new();
    public SampleStatistics GpuFan { get; set; } = new();
    public int ThrottlingEvents { get; set; }
    public int ThrottlingSamples { get; set; }
    [JsonIgnore] private bool? _previousThrottling;
    [JsonIgnore] private DateTime? _lastSampleUtc;

    public void Record(TelemetrySample sample) {
        if (EndedUtc != null || sample.TimestampUtc < StartedUtc || sample.TimestampUtc <= _lastSampleUtc) return;
        if (_lastSampleUtc is DateTime previous && sample.TimestampUtc - previous > TimeSpan.FromSeconds(20))
            _previousThrottling = null;
        _lastSampleUtc = sample.TimestampUtc;
        CpuUsage.Add(sample.CpuUsage); GpuUsage.Add(sample.GpuUsage);
        CpuTemperature.Add(sample.CpuTemperature); GpuTemperature.Add(sample.GpuTemperature);
        CpuFan.Add(sample.CpuFanRpm); GpuFan.Add(sample.GpuFanRpm);
        if (sample.Throttling is bool throttling) {
            ThrottlingSamples++;
            if (throttling && _previousThrottling != true) ThrottlingEvents++;
            _previousThrottling = throttling;
        }
    }

    public GameSessionSummary Copy() {
        var copy = (GameSessionSummary)MemberwiseClone();
        copy.CpuUsage = CpuUsage.Copy(); copy.GpuUsage = GpuUsage.Copy();
        copy.CpuTemperature = CpuTemperature.Copy(); copy.GpuTemperature = GpuTemperature.Copy();
        copy.CpuFan = CpuFan.Copy(); copy.GpuFan = GpuFan.Copy();
        return copy;
    }
}
