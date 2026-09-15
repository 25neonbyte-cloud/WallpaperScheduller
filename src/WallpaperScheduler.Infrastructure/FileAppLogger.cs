using System.Text;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.Infrastructure;

public sealed class FileAppLogger : IAppLogger
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int RetainedFiles = 3;
    private readonly object _sync = new();
    private readonly string _directory;

    public FileAppLogger(string logDirectory)
    {
        _directory = logDirectory;
        LogFilePath = Path.Combine(logDirectory, "WallpaperScheduler.log");
    }

    public string LogFilePath { get; }

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded();

                var text = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (exception is not null)
                    text += Environment.NewLine + exception;

                File.AppendAllText(LogFilePath, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging nunca deve interromper o scheduler.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath)) return;
        if (new FileInfo(LogFilePath).Length < MaxBytes) return;

        for (var index = RetainedFiles; index >= 1; index--)
        {
            var source = index == 1
                ? LogFilePath
                : Path.Combine(_directory, $"WallpaperScheduler.{index - 1}.log");
            var destination = Path.Combine(_directory, $"WallpaperScheduler.{index}.log");

            if (File.Exists(destination)) File.Delete(destination);
            if (File.Exists(source)) File.Move(source, destination);
        }
    }
}
