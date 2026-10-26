using System.ComponentModel;
using AulaBattery.Core;

namespace AulaBattery.Windows;

internal sealed record ReceiverResult(BatteryReading? Reading, string Message, string? DevicePath = null);

internal static class ReceiverService
{
    internal static async Task<ReceiverResult> ReadAsync(List<string>? trace = null,
        CancellationToken cancellationToken = default)
    {
        var devices = await Task.Run(NativeHid.Enumerate, cancellationToken).ConfigureAwait(false);
        var candidates = devices.Where(d => d.IsBatteryEndpoint).OrderBy(d => d.UsagePage).ToArray();
        if (candidates.Length == 0)
        {
            var hint = devices.Any(d => d.VendorId == 0x258A && d.ProductId == 0x010C)
                ? "检测到有线键盘，请插入 2.4G 接收器并切到 2.4G 模式。"
                : "未发现支持的 2.4G 接收器（3554:FA09）。";
            return new(null, hint);
        }

        var errors = new List<string>();
        foreach (var device in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            trace?.Add($"Endpoint {device.UsbId} usage {device.UsageId}");
            try
            {
                using var channel = new HidChannel(device, trace);
                var reading = await BatteryReader.QueryAsync(channel, cancellationToken).ConfigureAwait(false);
                if (reading is not null)
                    return new(reading, reading.ExternalPower ? "外部供电 · 实际电量未知" :
                        reading.Percent is { } percent ? $"电量 {percent}%" : "设备未报告有效电量", device.Path);
                errors.Add("接收器在线，键盘未回复；可能休眠、关闭或未切到 2.4G 模式。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                trace?.Add($"Error {ex.Message}");
                errors.Add(ex.Message);
            }
        }
        return new(null, string.Join("；", errors.Distinct()));
    }
}
