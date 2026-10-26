namespace AulaBattery.Windows;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        using var instance = new Mutex(true, @"Local\AulaBattery.F87Pro", out var firstInstance);
        if (!firstInstance)
        {
            if (args.Length == 0)
                MessageBox.Show("电量程序已经在右下角托盘运行。", "AULA Battery");
            return 3;
        }
        if (args.Length > 0 && args[0] == "--diagnose")
        {
            var path = Path.GetFullPath(args.Length > 1 ? args[1] : "diagnostics.json");
            try
            {
                return await Diagnostics.SaveAsync(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"无法保存诊断文件：{ex.Message}", "AULA Battery");
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext(args.Contains("--test-notification"));
        using var smokeTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        if (args.Length > 0 && args[0] == "--smoke-test")
        {
            smokeTimer.Tick += (_, _) => context.ExitThread();
            smokeTimer.Start();
        }
        Application.Run(context);
        return 0;
    }
}
