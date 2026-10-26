namespace AulaBattery.Core;

public interface IReceiverChannel
{
    Task SendAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken = default);
    Task<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default);
}

public static class BatteryReader
{
    public static async Task<BatteryReading?> QueryAsync(
        IReceiverChannel channel,
        CancellationToken cancellationToken = default,
        TimeSpan? responseTimeout = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(responseTimeout ?? TimeSpan.FromSeconds(1));
            try
            {
                await channel.SendAsync(BatteryProtocol.CreateQuery(), deadline.Token).ConfigureAwait(false);
                while (true)
                {
                    var report = await channel.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                    if (report.IsEmpty)
                        throw new IOException("接收器已断开连接。");
                    if (BatteryProtocol.TryParse(report.Span, out var reading))
                        return reading;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A lost radio reply gets one retry; caller cancellation still propagates.
                continue;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }
}
