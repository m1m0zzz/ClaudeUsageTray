using System.Diagnostics;
using Microsoft.Win32;

namespace ClaudeUsageTray;

/// <summary>
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run で自動起動を制御する。
/// HKCU なので管理者権限は不要。
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageTray";

    public static void Apply(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Run キーを開けませんでした。");

        if (!enabled)
        {
            if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return;
        }

        string? exePath = GetExecutablePath();
        if (exePath is null) return;

        string desired = $"\"{exePath}\"";

        // 実行ファイルを移動した場合に追従できるよう、毎回上書きする
        if (key.GetValue(ValueName) as string != desired)
        {
            key.SetValue(ValueName, desired, RegistryValueKind.String);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        // PublishSingleFile では Assembly.Location が空になるため ProcessPath を使う
        string? path = Environment.ProcessPath;

        if (string.IsNullOrEmpty(path))
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
        }

        return string.IsNullOrEmpty(path) ? null : path;
    }
}
