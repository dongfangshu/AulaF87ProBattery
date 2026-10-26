namespace AulaBattery.Core;

public sealed record BatteryReading(int? Percent, bool ExternalPower);

public static class BatteryProtocol
{
    public const int ReportLength = 20;
    public const ushort VendorId = 0x3554;
    public const ushort ProductId = 0xFA09;

    public static byte[] CreateQuery()
    {
        var report = new byte[ReportLength];
        report[0] = 0x13;
        report[1] = 0x4A;
        report[19] = 0x5D;
        return report;
    }

    public static bool TryParse(ReadOnlySpan<byte> report, out BatteryReading? reading)
    {
        reading = null;
        // A request echo has subcommand 0 and must never become a 0% reading.
        if (report.Length < ReportLength || report[0] != 0x13 ||
            (report[1] & 0x7F) != 0x4A || report[2] != 1 ||
            report[3] != 0 || report[4] != 2)
            return false;

        var sum = 0;
        for (var i = 0; i < ReportLength - 1; i++)
            sum += report[i];
        if ((byte)sum != report[19] || report[6] is not (0x01 or 0x10))
            return false;

        var external = report[6] == 0x10;
        // Firmware reports 100 on external power, regardless of actual charge.
        int? percent = !external && report[5] is >= 1 and <= 100 ? report[5] : null;
        reading = new BatteryReading(percent, external);
        return true;
    }
}
