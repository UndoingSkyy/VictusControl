namespace VictusControl.Core.Monitoring;

// Null means this sensor was not sampled or was unavailable, never zero.
public sealed record TelemetrySample(DateTime TimestampUtc, double? CpuUsage, double? GpuUsage,
    double? CpuTemperature, double? GpuTemperature, double? CpuFanRpm, double? GpuFanRpm,
    bool? Throttling);

public sealed class TelemetryHistory {
    private readonly Queue<TelemetrySample> _samples = new();
    public void Add(TelemetrySample sample) {
        if (_samples.Count > 0 && sample.TimestampUtc < _samples.Last().TimestampUtc) return;
        _samples.Enqueue(sample);
        while (_samples.Count > 3600 || (_samples.Count > 0 &&
               sample.TimestampUtc - _samples.Peek().TimestampUtc > TimeSpan.FromMinutes(30)))
            _samples.Dequeue();
    }
    public IReadOnlyList<TelemetrySample> Snapshot() => _samples.ToArray();
}
