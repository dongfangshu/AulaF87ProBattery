namespace AulaBattery.Core;

public sealed class LowBatteryAlert
{
    private bool notified;

    public bool ShouldNotify(BatteryReading? freshReading)
    {
        if (freshReading is null)
            return false;
        // Hysteresis avoids repeated alerts when readings fluctuate around 10%.
        if (freshReading.ExternalPower || freshReading.Percent is >= 15 and <= 100)
        {
            notified = false;
            return false;
        }
        if (notified || freshReading.Percent is not (>= 1 and <= 10))
            return false;
        notified = true;
        return true;
    }
}
