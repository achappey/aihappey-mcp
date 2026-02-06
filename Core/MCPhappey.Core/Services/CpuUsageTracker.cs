using System.Diagnostics;

public class CpuUsageTracker
{
    private TimeSpan _lastTotalProcessorTime;
    private DateTime _lastCheck;

    public double GetCpuUsagePercent()
    {
        var process = Process.GetCurrentProcess();

        var now = DateTime.UtcNow;
        var totalCpu = process.TotalProcessorTime;

        var cpuUsedMs = (totalCpu - _lastTotalProcessorTime).TotalMilliseconds;
        var elapsedMs = (now - _lastCheck).TotalMilliseconds;

        _lastTotalProcessorTime = totalCpu;
        _lastCheck = now;

        if (elapsedMs <= 0) return 0;

        return cpuUsedMs / (Environment.ProcessorCount * elapsedMs) * 100;
    }

    public CpuUsageTracker()
    {
        _lastTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
        _lastCheck = DateTime.UtcNow;
    }
}
