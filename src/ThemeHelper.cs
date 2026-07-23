using Microsoft.Win32;

namespace ClaudeUsageTray;

public static class ThemeHelper
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// タスクバーがダークテーマかどうか。
    /// SystemUsesLightTheme はタスクバー/スタート/通知領域に適用される値
    /// （AppsUseLightTheme はアプリ側なので別物）。
    /// </summary>
    public static bool IsTaskbarDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("SystemUsesLightTheme");

            if (value is int i) return i == 0;

            // 値が無い場合の Windows 既定はダーク
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>ポップアップの配色。タスクバーではなくアプリのテーマに合わせる。</summary>
    public static bool AreAppsDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("AppsUseLightTheme");

            if (value is int i) return i == 0;

            return false;
        }
        catch
        {
            return false;
        }
    }
}
