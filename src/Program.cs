using System.Diagnostics;

namespace ClaudeUsageTray;

internal static class Program
{
    private const string MutexName = "Global\\ClaudeUsageTray.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "ClaudeUsageTray はすでに起動しています。通知領域のアイコンを確認してください。",
                "ClaudeUsageTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // ウィンドウ生成前に呼ぶ必要がある
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppConfig config;
        try
        {
            config = AppConfig.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"設定ファイルを読めませんでした。既定値で起動します。\n\n{AppConfig.ConfigPath}\n{ex.Message}",
                "ClaudeUsageTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            config = new AppConfig();
        }

        try
        {
            StartupManager.Apply(config.RunAtStartup);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"自動起動の設定に失敗: {ex}");
        }

        using var context = new TrayContext(config);
        Application.Run(context);
    }
}
