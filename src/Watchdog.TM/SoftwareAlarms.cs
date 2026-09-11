namespace Watchdog.TM;

public static class SoftwareAlarms
{
    // Only fresh, usable measurements can raise a current temperature alarm.
    public static string Evaluate(Sensor sensor, Reading reading)
    {
        if (!sensor.Enabled || sensor.Retired || reading.Quality != "VALID" ||
            reading.Temperature is not double temperature || !double.IsFinite(temperature))
            return "Unavailable";
        if (sensor.SoftwareHighLimit is double high && temperature > high) return "High";
        if (sensor.SoftwareLowLimit is double low && temperature < low) return "Low";
        return "Normal";
    }
}
