using System;
using System.IO;

namespace damuku_kano.Services;

/// <summary>
/// Minimal file logger for unhandled exceptions and renderer failures, so crashes
/// outside a debugger can still be diagnosed. Writes to %LocalAppData%\KanoDanmaku\crash.log.
/// </summary>
public static class CrashLog
{
    private static readonly object _sync = new();
    private static readonly string _logFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KanoDanmaku", "crash.log");

    public static void Write(string context, Exception ex) => Write($"{context}: {ex}");

    public static void Write(string message)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
