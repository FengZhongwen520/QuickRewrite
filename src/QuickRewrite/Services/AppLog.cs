using System.Diagnostics;
using System.Text;

namespace QuickRewrite.Services;

internal static class AppLog
{
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly object Gate = new();

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickRewrite",
        "logs");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, "app.log");
                RotateIfNeeded(path);

                var builder = new StringBuilder();
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(' ')
                    .Append(level)
                    .Append(' ')
                    .AppendLine(message);

                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            Debug.WriteLine($"AppLog 写入失败：{level} {message} {exception}");
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxLogBytes)
            {
                return;
            }

            var backup = path + ".1";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(path, backup);
        }
        catch
        {
            // 日志轮转失败不应影响主流程。
        }
    }
}
