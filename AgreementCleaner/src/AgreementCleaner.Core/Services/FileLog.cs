namespace AgreementCleaner.Core.Services;

/// <summary>
/// Writes log lines to a daily file so there is a record of what was deleted, by whom and when.
/// Default folder: %LOCALAPPDATA%\AgreementCleaner\logs
/// Logging must never stop the app, so write errors are ignored.
/// </summary>
public sealed class FileLog
{
    private readonly object _lock = new();

    public FileLog(string? folder = null)
    {
        Folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgreementCleaner", "logs");
    }

    public string Folder { get; }

    public string CurrentFile => Path.Combine(Folder, $"AgreementCleaner-{DateTime.Now:yyyyMMdd}.log");

    public void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.UserName}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Ignore: a log file problem must not break a clean-up.
        }
    }
}
