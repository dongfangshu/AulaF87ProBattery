using System.ComponentModel;
using AulaBattery.Core;

namespace AulaBattery.Windows;

internal sealed class HidChannel : IReceiverChannel, IDisposable
{
    private readonly FileStream stream;
    private readonly HidDevice device;
    private readonly List<string>? trace;

    internal HidChannel(HidDevice device, List<string>? trace = null)
    {
        this.device = device;
        this.trace = trace;
        var handle = NativeHid.Open(device.Path, NativeHid.ReadWrite, true);
        if (handle.IsInvalid)
        {
            var error = MarshalError();
            handle.Dispose();
            throw new Win32Exception(error, "无法打开接收器，请检查设备连接或关闭占用设备的驱动软件。");
        }
        try
        {
            stream = new FileStream(handle, FileAccess.ReadWrite, device.InputLength, true);
            if (!NativeHid.HidD_FlushQueue(handle))
                throw new Win32Exception(MarshalError(), "无法清空接收器旧报文。");
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken = default)
    {
        var padded = new byte[device.OutputLength];
        report.CopyTo(padded);
        trace?.Add($"TX control {Convert.ToHexString(report.Span)}");
        // This receiver requires USB SET_REPORT over the control endpoint.
        // Keep the driver's synchronous control transfer off the UI thread.
        var handle = stream.SafeFileHandle;
        var result = await Task.Run(() =>
        {
            var success = NativeHid.HidD_SetOutputReport(handle, padded, (uint)padded.Length);
            return (Success: success, Error: success ? 0 : MarshalError());
        }, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new Win32Exception(result.Error, "无法发送 HID 电量查询。");
    }

    public async Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[device.InputLength];
        var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        trace?.Add($"RX {Convert.ToHexString(buffer.AsSpan(0, count))}");
        return buffer.AsMemory(0, count);
    }

    private static int MarshalError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
    public void Dispose() => stream.Dispose();
}
