using System.Diagnostics;
using System.Text;

namespace ClaudeUsageTray;

public sealed record WslReadResult(bool Success, string Content, string Diagnostic);

/// <summary>
/// wsl.exe 経由で WSL 側のファイルを読む。
///
/// 重要: \\wsl$ への UNC アクセスも wsl.exe でのコマンド実行も、停止中のディストロを
/// 起動させる。3 分おきにこれを行うと WSL が永久に落ちなくなるため、
/// 既定では「起動中のディストロがある場合のみ」読みに行く。
/// --list --running はディストロを起動しない（LxssManager に問い合わせるだけ）。
/// </summary>
public static class WslBridge
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);

    public static string WslExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

    public static bool IsAvailable => File.Exists(WslExePath);

    /// <summary>起動中のディストロ名。停止中のものは起動しない。</summary>
    public static IReadOnlyList<string> GetRunningDistros()
    {
        // 注意: --list 系の出力は UTF-16LE。コマンド実行時の出力（UTF-8）とは別。
        var result = Run(new[] { "--list", "--running", "--quiet" }, Encoding.Unicode);

        if (!result.Success) return Array.Empty<string>();

        return result.Content
            .Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    public static bool IsAnyDistroRunning(string? preferredDistro)
    {
        IReadOnlyList<string> running = GetRunningDistros();

        if (running.Count == 0) return false;

        if (string.IsNullOrWhiteSpace(preferredDistro)) return true;

        return running.Any(d =>
            string.Equals(d, preferredDistro, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// WSL 側のファイルを読む。linuxPath は sh で展開されるため ~ を使ってよい。
    /// </summary>
    public static WslReadResult ReadFile(string? distro, string linuxPath)
    {
        if (!IsAvailable)
        {
            return new WslReadResult(false, "", "wsl.exe が見つかりません。");
        }

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(distro))
        {
            args.Add("-d");
            args.Add(distro);
        }

        args.Add("--");
        args.Add("sh");
        args.Add("-c");
        // ログインシェル（-l）は profile を読むぶん遅く、MOTD が混ざる恐れがあるので sh を使う
        args.Add($"cat {linuxPath}");

        // コマンド実行時の標準出力はバイト列がそのまま流れてくるので UTF-8 で読む
        return Run(args, new UTF8Encoding(false));
    }

    private static WslReadResult Run(IEnumerable<string> arguments, Encoding stdoutEncoding)
    {
        var psi = new ProcessStartInfo
        {
            FileName = WslExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = stdoutEncoding,
            StandardErrorEncoding = stdoutEncoding,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return new WslReadResult(false, "", "wsl.exe を起動できませんでした。");
            }

            // 先に読み切ってから待たないとパイプが詰まって固まる
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 競合は無視 */ }
                return new WslReadResult(false, "", "wsl.exe がタイムアウトしました。");
            }

            string output = stdout.GetAwaiter().GetResult();
            string error = stderr.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                string message = error.Trim();
                if (message.Length == 0) message = $"終了コード {process.ExitCode}";
                return new WslReadResult(false, "", message);
            }

            return new WslReadResult(true, output, "");
        }
        catch (Exception ex)
        {
            return new WslReadResult(false, "", ex.Message);
        }
    }
}
