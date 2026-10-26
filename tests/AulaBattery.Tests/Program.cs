using AulaBattery.Core;
using AulaBattery.Windows;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {name}");
    checks++;
    Console.WriteLine($"PASS: {name}");
}

byte[] Reply(byte level = 68, byte state = 1, byte command = 0x4A)
{
    var bytes = new byte[20];
    bytes[0] = 0x13;
    bytes[1] = command;
    bytes[2] = 1;
    bytes[4] = 2;
    bytes[5] = level;
    bytes[6] = state;
    Seal(bytes);
    return bytes;
}

void Seal(byte[] bytes) => bytes[19] = (byte)bytes.Take(19).Sum(b => b);

Check(Convert.ToHexString(BatteryProtocol.CreateQuery()) == "134A00000000000000000000000000000000005D", "Exact battery query");
Check(BatteryProtocol.TryParse(Reply(), out var value) && value == new BatteryReading(68, false), "Battery reply");
Check(BatteryProtocol.TryParse(Reply(command: 0xCA), out value) && value?.Percent == 68, "Response command high bit");
Check(BatteryProtocol.TryParse(Reply(100), out value) && value?.Percent == 100, "Real 100 percent on battery");
Check(BatteryProtocol.TryParse(Reply(1), out value) && value?.Percent == 1, "One percent");
Check(BatteryProtocol.TryParse(Reply(100, 0x10), out value) && value == new BatteryReading(null, true), "External power does not imply full charge");
Check(BatteryProtocol.TryParse(Reply(0), out value) && value?.Percent is null, "Firmware zero is unknown");
Check(BatteryProtocol.TryParse(Reply(101), out value) && value?.Percent is null, "Out of range percentage is unknown");
Check(!BatteryProtocol.TryParse(BatteryProtocol.CreateQuery(), out _), "Request echo rejected");
Check(!BatteryProtocol.TryParse(new byte[19], out _), "Truncated report rejected");
Check(!BatteryProtocol.TryParse(Reply(state: 0xFF), out _), "Unknown power state rejected");
var invalid = Reply();
invalid[5]++;
Check(!BatteryProtocol.TryParse(invalid, out _), "Bad checksum rejected");
foreach (var offset in new[] { 0, 1, 2, 3, 4 })
{
    var report = Reply();
    report[offset]++;
    Seal(report);
    Check(!BatteryProtocol.TryParse(report, out _), $"Wrong response header {offset} rejected");
}
var padded = new byte[64];
Reply().CopyTo(padded, 0);
Check(BatteryProtocol.TryParse(padded, out value) && value?.Percent == 68, "HID padding accepted");

var channel = new FakeChannel([BatteryProtocol.CreateQuery(), invalid, Reply()]);
value = await BatteryReader.QueryAsync(channel);
Check(value?.Percent == 68 && channel.Sends == 1, "Skip echo and corrupt frame before valid reply");
channel = new FakeChannel([], replyOnSend: 2, reply: Reply(39));
value = await BatteryReader.QueryAsync(channel, responseTimeout: TimeSpan.FromMilliseconds(20));
Check(value?.Percent == 39 && channel.Sends == 2, "Lost reply retries successfully");
channel = new FakeChannel([]);
value = await BatteryReader.QueryAsync(channel, responseTimeout: TimeSpan.FromMilliseconds(20));
Check(value is null && channel.Sends == 2, "Silent receiver ends after two bounded attempts");
using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await BatteryReader.QueryAsync(new FakeChannel([]), canceled.Token);
    throw new InvalidOperationException("Caller cancellation was swallowed");
}
catch (OperationCanceledException)
{
    Check(true, "Caller cancellation propagates");
}
try
{
    await BatteryReader.QueryAsync(new FakeChannel([Array.Empty<byte>()]));
    throw new InvalidOperationException("Disconnect was swallowed");
}
catch (IOException)
{
    Check(true, "Zero byte read reports disconnect");
}

var receiver = new HidDevice("test", 0x3554, 0xFA09, 0xFF02, 2, 20, 20);
Check(receiver.IsBatteryEndpoint, "Select vendor battery endpoint");
Check(!(receiver with { UsagePage = 1 }).IsBatteryEndpoint, "Never query standard keyboard collection");
Check(!(receiver with { ProductId = 0xFA08 }).IsBatteryEndpoint, "Never query Bluetooth or another product");
Check(!(receiver with { OutputLength = 0 }).IsBatteryEndpoint, "Reject endpoint without output reports");
Directory.CreateDirectory("artifacts/icons");
foreach (var percent in new int?[] { null, 1, 20, 39, 68, 100 })
{
    using var icon = TrayIconRenderer.Create(percent, false);
    using var bitmap = icon.ToBitmap();
    Check(bitmap.Width == 32 && bitmap.Height == 32, $"Render icon {percent?.ToString() ?? "unknown"}");
    bitmap.Save($"artifacts/icons/{percent?.ToString() ?? "unknown"}.png");
}
using (var icon = TrayIconRenderer.Create(null, true))
using (var bitmap = icon.ToBitmap())
{
    bitmap.Save("artifacts/icons/power.png");
    Check(bitmap.Width == 32, "Render external power icon");
}
using (var icon = TrayIconRenderer.Create(68, false))
using (var bitmap = icon.ToBitmap())
    Check(CountGlyphs(bitmap) == 2, "Both digits remain visible in the tray icon");
using (var icon = TrayIconRenderer.Create(100, false))
using (var bitmap = icon.ToBitmap())
    Check(CountGlyphs(bitmap) == 3, "All three digits of 100 remain visible");
using (var icon = TrayIconRenderer.Create(93, false, true))
using (var bitmap = icon.ToBitmap())
{
    Check(CountGlyphs(bitmap) == 2, "Cached percentage retains both visible digits");
    bitmap.Save("artifacts/icons/cached-93.png");
}
var receivedAt = DateTimeOffset.UtcNow;
var state = new BatteryDisplayState().Update(new BatteryReading(93, false), receivedAt);
state = state.Update(await BatteryReader.QueryAsync(new FakeChannel([]),
    responseTimeout: TimeSpan.FromMilliseconds(20)), receivedAt.AddMinutes(1));
Check(state.Reading?.Percent == 93 && state.ReadAt == receivedAt && state.IsCached,
    "Radio timeout retains real reading and original timestamp");
state = state.Update(new BatteryReading(92, false), receivedAt.AddMinutes(2));
Check(state.Reading?.Percent == 92 && !state.IsCached && state.ReadAt == receivedAt.AddMinutes(2),
    "Fresh radio reply replaces cached percentage");
state = state.Update(new BatteryReading(null, false), receivedAt.AddMinutes(3));
Check(state.Reading?.Percent == 92 && state.IsCached, "Invalid percentage does not erase last valid reading");
state = state.Update(new BatteryReading(null, true), receivedAt.AddMinutes(4));
Check(state.Reading is { Percent: null, ExternalPower: true } && !state.IsCached,
    "External power still replaces cached battery status");
state = new BatteryDisplayState().Update(null, receivedAt);
Check(state.Reading is null && state.ReadAt is null, "Never invent a percentage without a successful reply");
var alert = new LowBatteryAlert();
Check(!alert.ShouldNotify(null), "Missing reply never triggers low battery alert");
Check(!alert.ShouldNotify(new BatteryReading(null, false)), "Unknown battery never triggers alert");
Check(!alert.ShouldNotify(new BatteryReading(11, false)), "No alert above ten percent");
Check(alert.ShouldNotify(new BatteryReading(10, false)), "Exactly ten percent triggers native notification policy");
Check(!alert.ShouldNotify(new BatteryReading(10, false)), "Repeated ten percent does not repeat alert");
Check(!alert.ShouldNotify(new BatteryReading(9, false)), "Falling further does not repeat alert");
Check(!alert.ShouldNotify(null), "Lost radio reply does not rearm alert");
Check(!alert.ShouldNotify(new BatteryReading(11, false)) && !alert.ShouldNotify(new BatteryReading(10, false)),
    "Fluctuation around threshold does not cause notification spam");
Check(!alert.ShouldNotify(new BatteryReading(15, false)) && alert.ShouldNotify(new BatteryReading(9, false)),
    "Recovery to fifteen percent rearms next low battery alert");
Check(!alert.ShouldNotify(new BatteryReading(null, true)) && alert.ShouldNotify(new BatteryReading(8, false)),
    "External power rearms alert for a new discharge period");
Check(new LowBatteryAlert().ShouldNotify(new BatteryReading(7, false)), "Starting below threshold also alerts");
Check(!new LowBatteryAlert().ShouldNotify(new BatteryReading(10, true)), "No low battery alert on external power");
Check(!new LowBatteryAlert().ShouldNotify(new BatteryReading(0, false)), "Invalid zero reading does not alert");
Console.WriteLine($"All {checks} checks passed.");

int CountGlyphs(System.Drawing.Bitmap bitmap)
{
    var visited = new HashSet<(int X, int Y)>();
    var components = 0;
    bool IsInk(int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        return pixel.A > 200 && pixel.R > 200 && pixel.G > 200 && pixel.B > 200;
    }
    for (var y = 0; y < bitmap.Height; y++)
    for (var x = 0; x < bitmap.Width; x++)
    {
        if (!IsInk(x, y) || !visited.Add((x, y)))
            continue;
        var points = new Queue<(int X, int Y)>();
        points.Enqueue((x, y));
        var area = 0;
        while (points.TryDequeue(out var point))
        {
            area++;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var next = (X: point.X + dx, Y: point.Y + dy);
                if (next.X >= 0 && next.X < bitmap.Width && next.Y >= 0 && next.Y < bitmap.Height &&
                    IsInk(next.X, next.Y) && visited.Add(next))
                    points.Enqueue(next);
            }
        }
        if (area > 3)
            components++;
    }
    return components;
}

internal sealed class FakeChannel(IEnumerable<byte[]> reports, int replyOnSend = 0, byte[]? reply = null) : IReceiverChannel
{
    private readonly Queue<byte[]> pending = new(reports);
    public int Sends { get; private set; }

    public Task SendAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sends++;
        if (Sends == replyOnSend && reply is not null)
            pending.Enqueue(reply);
        return Task.CompletedTask;
    }

    public async Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pending.TryDequeue(out var report))
            return report;
        // Model a pending OS read: cancellation is the only completion when no frame arrives.
        var completion = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task;
    }
}
