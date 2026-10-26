using System.ComponentModel;
using System.Security;
using AulaBattery.Core;

namespace AulaBattery.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new("正在读取电量…") { Enabled = false };
    private readonly ToolStripMenuItem lastUpdate = new("尚无有效读数") { Enabled = false };
    private readonly ToolStripMenuItem refresh = new("立即刷新");
    private readonly ToolStripMenuItem diagnose = new("导出诊断文件…");
    private readonly ToolStripMenuItem startup = new("开机启动");
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly LowBatteryAlert lowBatteryAlert = new();
    private readonly bool testNotification;
    private Icon currentIcon;
    private bool busy;
    private bool closed;
    private bool disposed;
    private BatteryDisplayState display = BatteryCache.Load();
    private string message = "正在读取电量…";

    internal TrayApplicationContext(bool testNotification = false)
    {
        this.testNotification = testNotification;
        currentIcon = TrayIconRenderer.Create(display.Reading?.Percent, display.Reading?.ExternalPower == true, display.IsCached);
        tray = new NotifyIcon { Icon = currentIcon, Text = "狼蛛 F87 Pro · 正在读取", ContextMenuStrip = menu };
        menu.Items.Add(new ToolStripMenuItem("狼蛛 F87 Pro · 2.4G 电量") { Enabled = false });
        menu.Items.Add(status);
        menu.Items.Add(lastUpdate);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(refresh);
        var intervals = new ToolStripMenuItem("刷新间隔");
        foreach (var seconds in new[] { 30, 60, 300 })
        {
            var item = new ToolStripMenuItem(seconds < 60 ? "30 秒" : $"{seconds / 60} 分钟")
            { Tag = seconds, Checked = seconds == Preferences.PollSeconds };
            item.Click += (_, _) => ChangeInterval(seconds, item, intervals);
            intervals.DropDownItems.Add(item);
        }
        menu.Items.Add(intervals);
        startup.Checked = Preferences.StartsAtLogin;
        startup.Click += (_, _) => ToggleStartup();
        menu.Items.Add(startup);
        menu.Items.Add("测试低电量通知", null, (_, _) => ShowLowBatteryNotification(10, true));
        menu.Items.Add(diagnose);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("查看状态", null, (_, _) => ShowStatus());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        refresh.Click += async (_, _) => await RefreshAsync();
        diagnose.Click += async (_, _) => await ExportDiagnosticsAsync();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowStatus(); };
        timer.Interval = Preferences.PollSeconds * 1000;
        timer.Tick += async (_, _) => await RefreshAsync();
        tray.Visible = true;
        UpdateDisplay(null);
        timer.Start();
        // Application.Idle runs on the WinForms UI thread after its synchronization context exists.
        Application.Idle += FirstRefresh;
    }

    private async void FirstRefresh(object? sender, EventArgs e)
    {
        Application.Idle -= FirstRefresh;
        if (testNotification)
            ShowLowBatteryNotification(10, true);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (busy || closed)
            return;
        SetBusy(true);
        try
        {
            var result = await ReceiverService.ReadAsync(cancellationToken: stopping.Token);
            if (closed)
                return;
            message = result.Message;
            UpdateDisplay(result.Reading);
            if (lowBatteryAlert.ShouldNotify(result.Reading) && result.Reading?.Percent is { } percent)
                ShowLowBatteryNotification(percent);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Closing the tray cancels an outstanding HID operation.
            return;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            if (!closed)
            {
                message = $"读取失败：{ex.Message}";
                UpdateDisplay(null);
            }
        }
        finally
        {
            if (!closed)
                SetBusy(false);
        }
    }

    private void UpdateDisplay(BatteryReading? reading)
    {
        display = display.Update(reading, DateTimeOffset.Now);
        if (reading is not null && !display.IsCached)
            BatteryCache.Save(display);
        var next = TrayIconRenderer.Create(display.Reading?.Percent, display.Reading?.ExternalPower == true, display.IsCached);
        tray.Icon = next;
        currentIcon.Dispose();
        currentIcon = next;
        var summary = display.IsCached ? $"上次读数 {Describe(display.Reading)} · 当前未取得有效电量" : message;
        status.Text = summary.Length > 48 ? summary[..48] + "…" : summary;
        status.ToolTipText = message;
        lastUpdate.Text = display.ReadAt is { } at
            ? $"上次回复 {at:MM-dd HH:mm:ss} · {Describe(display.Reading)}" : "尚无有效读数";
        var tooltip = $"狼蛛 F87 Pro · {summary}";
        tray.Text = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        // Retry sooner until the first successful reading so waking is noticed promptly.
        timer.Interval = display.Reading is null ? 5000 : Preferences.PollSeconds * 1000;
    }

    private static string Describe(BatteryReading? reading) => reading switch
    {
        { ExternalPower: true } => "外部供电",
        { Percent: { } percent } => $"{percent}%",
        _ => "电量未知"
    };

    private void ShowLowBatteryNotification(int percent, bool isTest = false) => tray.ShowBalloonTip(
        10000,
        isTest ? "狼蛛 F87 Pro 电量不足（测试）" : "狼蛛 F87 Pro 电量不足",
        isTest ? "这是一条测试通知。键盘电量低于或等于 10% 时，会提醒你及时充电。" :
            $"键盘电量仅剩 {percent}%，请及时充电。",
        ToolTipIcon.Warning);

    private void ShowStatus() => MessageBox.Show(
        $"{message}\n\n{lastUpdate.Text}\n\n数字表示百分比；灰底数字是上次读数，时间见上方。\n闪电表示外部供电；? 表示还没有有效读数。\n" +
        "键盘休眠时可按任意键唤醒，然后右键选择立即刷新。\n如果图标被折叠，可将它从右下角的隐藏图标区域拖出来。",
        "狼蛛 F87 Pro 电量", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ChangeInterval(int seconds, ToolStripMenuItem selected, ToolStripMenuItem parent)
    {
        try
        {
            Preferences.PollSeconds = seconds;
            timer.Interval = seconds * 1000;
            foreach (ToolStripMenuItem item in parent.DropDownItems)
                item.Checked = item == selected;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            MessageBox.Show($"无法保存刷新间隔：{ex.Message}", "AULA Battery");
        }
    }

    private void ToggleStartup()
    {
        try
        {
            Preferences.StartsAtLogin = !startup.Checked;
            startup.Checked = Preferences.StartsAtLogin;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            MessageBox.Show($"无法修改开机启动：{ex.Message}", "AULA Battery");
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        refresh.Enabled = diagnose.Enabled = !value;
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (busy || closed)
            return;
        using var dialog = new SaveFileDialog
        { Filter = "JSON 文件|*.json", FileName = "AulaBattery-diagnostics.json", Title = "导出设备与电量通信诊断" };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        SetBusy(true);
        try
        {
            await Diagnostics.SaveAsync(dialog.FileName, stopping.Token);
            if (!closed)
                MessageBox.Show($"诊断已保存：\n{dialog.FileName}", "AULA Battery");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Exit cancels diagnostic I/O as well as polling.
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!closed)
                MessageBox.Show($"无法保存诊断：{ex.Message}", "AULA Battery");
        }
        finally
        {
            if (!closed)
                SetBusy(false);
        }
    }

    protected override void ExitThreadCore()
    {
        closed = true;
        Application.Idle -= FirstRefresh;
        timer.Stop();
        stopping.Cancel();
        tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            closed = true;
            Application.Idle -= FirstRefresh;
            stopping.Cancel();
            timer.Dispose();
            tray.Dispose();
            menu.Dispose();
            currentIcon.Dispose();
            stopping.Dispose();
        }
        base.Dispose(disposing);
    }
}
