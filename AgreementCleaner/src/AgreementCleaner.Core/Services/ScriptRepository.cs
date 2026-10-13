using System.Text.RegularExpressions;
using AgreementCleaner.Core.Configuration;

namespace AgreementCleaner.Core.Services;

/// <summary>A loaded SQL script.</summary>
public sealed record SqlScript(string Name, string FilePath, string Text, DateTime LastModified);

/// <summary>
/// Reads the SQL scripts from the configured (shared) folder.
/// The files are read again every time they are needed, so a script that is
/// updated on the share is used straight away - no app restart or redeploy.
/// </summary>
public sealed class ScriptRepository
{
    // A line that only contains GO (optionally with a repeat count). GO is an
    // SSMS command, not T-SQL, so it cannot be sent to SQL Server.
    private static readonly Regex GoLine = new(@"^\s*GO(\s+\d+)?\s*(--.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private readonly AppSettings _settings;

    public ScriptRepository(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>The full path of the script folder.</summary>
    public string ScriptFolder => Path.GetFullPath(_settings.ScriptFolder, AppContext.BaseDirectory);

    public SqlScript LoadFindAgreement() => Load(_settings.Scripts.FindAgreement);
    public SqlScript LoadDeleteGateway() => Load(_settings.Scripts.DeleteGateway);
    public SqlScript LoadDeleteDrms() => Load(_settings.Scripts.DeleteDrms);
    public SqlScript LoadClearComms() => Load(_settings.Scripts.ClearComms);

    /// <summary>Names of all scripts the app needs, in the order they are used.</summary>
    public IReadOnlyList<string> AllScriptFileNames => new[]
    {
        _settings.Scripts.FindAgreement,
        _settings.Scripts.DeleteGateway,
        _settings.Scripts.DeleteDrms,
        _settings.Scripts.ClearComms
    };

    /// <summary>
    /// Tries to load every script. Returns one line per script describing whether it is OK.
    /// Used by the "Check scripts" button.
    /// </summary>
    public IReadOnlyList<(string FileName, bool Ok, string Message)> CheckAll()
    {
        var results = new List<(string, bool, string)>();
        foreach (var fileName in AllScriptFileNames)
        {
            try
            {
                var script = Load(fileName);
                results.Add((fileName, true, $"OK (last changed {script.LastModified:yyyy-MM-dd HH:mm})"));
            }
            catch (Exception ex)
            {
                results.Add((fileName, false, ex.Message));
            }
        }
        return results;
    }

    /// <summary>Reads one script file and checks it can be run by the app.</summary>
    public SqlScript Load(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("A script file name is missing in the settings file.");

        var folder = ScriptFolder;
        if (!Directory.Exists(folder))
            throw new InvalidOperationException(
                $"The script folder cannot be found: {folder}. Check ScriptFolder in appsettings.json and that you have access to the share.");

        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Script '{fileName}' was not found in {folder}.");

        var text = File.ReadAllText(path);
        Check(fileName, text);

        return new SqlScript(fileName, path, text, File.GetLastWriteTime(path));
    }

    /// <summary>Basic checks so a broken script fails before anything runs.</summary>
    internal static void Check(string fileName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Script '{fileName}' is empty.");

        if (GoLine.IsMatch(text))
            throw new InvalidOperationException(
                $"Script '{fileName}' contains a GO line. GO only works in SSMS; remove it so the app can run the script.");
    }
}
