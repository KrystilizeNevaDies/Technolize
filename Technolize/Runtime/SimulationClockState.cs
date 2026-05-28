namespace Technolize.Runtime;

public sealed class SimulationClockState(double initialTicksPerSecond)
{
    private const double MinTicksPerSecond = 0.0;
    private const double MaxTicksPerSecond = 3840.0;

    private readonly object _sync = new();
    private double _targetTicksPerSecond = initialTicksPerSecond;
    private int _timesTicked;
    private int _renderSamples;
    private int _pollutionCount;
    private double _totalRenderMs;
    private double _totalSimulationMs;

    public double GetTargetTicksPerSecond()
    {
        lock (_sync)
        {
            return _targetTicksPerSecond;
        }
    }

    public void SetTargetTicksPerSecond(double ticksPerSecond)
    {
        lock (_sync)
        {
            _targetTicksPerSecond = Math.Clamp(ticksPerSecond, MinTicksPerSecond, MaxTicksPerSecond);
        }
    }

    public void AdjustTargetTicksPerSecond(double delta)
    {
        lock (_sync)
        {
            _targetTicksPerSecond = Math.Clamp(_targetTicksPerSecond + delta, MinTicksPerSecond, MaxTicksPerSecond);
        }
    }

    public void RecordSimulationTick(double elapsedMilliseconds)
    {
        lock (_sync)
        {
            _timesTicked++;
            _totalSimulationMs += elapsedMilliseconds;
        }
    }

    public void RecordRenderFrame(double elapsedMilliseconds)
    {
        lock (_sync)
        {
            _renderSamples++;
            _totalRenderMs += elapsedMilliseconds;
        }
    }

    public void SetPollutionCount(int pollutionCount)
    {
        lock (_sync)
        {
            _pollutionCount = pollutionCount;
        }
    }

    public SimulationMetrics Snapshot()
    {
        lock (_sync)
        {
            return new SimulationMetrics(
                _targetTicksPerSecond,
                _timesTicked,
                _renderSamples,
                _pollutionCount,
                _totalRenderMs,
                _totalSimulationMs);
        }
    }
}

public readonly record struct SimulationMetrics(
    double TargetTicksPerSecond,
    int TimesTicked,
    int RenderSamples,
    int PollutionCount,
    double TotalRenderMs,
    double TotalSimulationMs)
{
    public double AverageRenderMs => RenderSamples > 0 ? TotalRenderMs / RenderSamples : 0.0;
    public double AverageSimulationMs => TimesTicked > 0 ? TotalSimulationMs / TimesTicked : 0.0;
    public double TotalMeasuredMs => TotalRenderMs + TotalSimulationMs;
    public double RenderPercent => TotalMeasuredMs > 0 ? TotalRenderMs / TotalMeasuredMs * 100.0 : 0.0;
    public double SimulationPercent => TotalMeasuredMs > 0 ? TotalSimulationMs / TotalMeasuredMs * 100.0 : 0.0;
    public double TimeScale => TargetTicksPerSecond / 60.0;
}