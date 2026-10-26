using Microsoft.Win32;

namespace AulaBattery.Windows;

internal static class Preferences
{
    private const string SettingsKey = @"Software\AulaBattery";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "AulaBatteryF87Pro";

    internal static int PollSeconds
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue("PollSeconds") is int seconds && seconds is 30 or 60 or 300 ? seconds : 60;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue("PollSeconds", value, RegistryValueKind.DWord);
        }
    }

    internal static bool StartsAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string command && command == StartupCommand;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunName, StartupCommand, RegistryValueKind.String);
            else
                key.DeleteValue(RunName, false);
        }
    }

    private static string StartupCommand
    {
        get
        {
            var executable = Environment.ProcessPath ?? throw new IOException("无法确定程序路径。");
            return Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{executable}\" \"{Environment.GetCommandLineArgs()[0]}\""
                : $"\"{executable}\"";
        }
    }
}
