namespace AulaBattery.Core;

public sealed record BatteryDisplayState(BatteryReading? Reading = null,
    DateTimeOffset? ReadAt = null, bool IsCached = false)
{
    public BatteryDisplayState Update(BatteryReading? reading, DateTimeOffset receivedAt) =>
        reading is { ExternalPower: true } or { Percent: >= 1 and <= 100 }
            ? new BatteryDisplayState(reading, receivedAt, false)
            : this with { IsCached = Reading is not null };
}
