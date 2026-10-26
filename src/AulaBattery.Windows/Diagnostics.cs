using System.Text.Encodings.Web;
using System.Text.Json;

namespace AulaBattery.Windows;

internal static class Diagnostics
{
    internal static async Task<int> SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        var trace = new List<string>();
        object report;
        int exitCode;
        try
        {
            var devices = await Task.Run(NativeHid.Enumerate, cancellationToken).ConfigureAwait(false);
            var result = await ReceiverService.ReadAsync(trace, cancellationToken).ConfigureAwait(false);
            report = new { Time = DateTimeOffset.Now, Devices = devices, Result = result, Trace = trace };
            exitCode = result.Reading is null ? 2 : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report = new { Time = DateTimeOffset.Now, Error = ex.ToString(), Trace = trace };
            exitCode = 1;
        }
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }), cancellationToken).ConfigureAwait(false);
        return exitCode;
    }
}
