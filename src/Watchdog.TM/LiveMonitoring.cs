namespace Watchdog.TM;

public static class LiveMonitoring
{
    // Live mode is persistent; editing a mapping must not require a second start.
    public static void Apply(Configuration configuration)
    {
        if (configuration.Settings.Simulation) return;
        var controllers = configuration.Controllers
            .Where(c => c.Enabled && c.Protocol is Protocol.RTU or Protocol.TCP)
            .Select(c => c.Id).ToHashSet();
        configuration.Settings.CommissionedSensors = configuration.Sensors
            .Where(s => s.Enabled && !s.Retired && s.Mapped && controllers.Contains(s.ControllerId))
            .Select(s => s.Id).Distinct().ToList();
    }
}
