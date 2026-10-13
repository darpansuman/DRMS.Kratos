using System.Text.Json;

namespace AgreementCleaner.Core.Configuration;

/// <summary>
/// Reads appsettings.json and checks that it makes sense before the app uses it.
/// </summary>
public static class AppSettingsLoader
{
    /// <summary>
    /// Words that must never appear in an environment name. This tool is only
    /// for DEV and TEST, so a production entry is refused outright.
    /// </summary>
    private static readonly string[] BlockedEnvironmentWords = { "PROD", "LIVE" };

    public static AppSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new InvalidOperationException($"Settings file not found: {filePath}");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        AppSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Settings file {filePath} is not valid JSON: {ex.Message}", ex);
        }

        if (settings is null)
            throw new InvalidOperationException($"Settings file {filePath} is empty.");

        Validate(settings);
        return settings;
    }

    /// <summary>Throws an exception that lists every problem found in the settings.</summary>
    public static void Validate(AppSettings settings)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ScriptFolder))
            errors.Add("ScriptFolder is empty.");

        if (settings.CommandTimeoutSeconds <= 0)
            errors.Add("CommandTimeoutSeconds must be greater than 0.");

        if (settings.Environments.Count == 0)
            errors.Add("No Environments are configured.");

        foreach (var env in settings.Environments)
        {
            var name = string.IsNullOrWhiteSpace(env.Name) ? "(no name)" : env.Name;

            if (string.IsNullOrWhiteSpace(env.Name))
                errors.Add("An environment has no Name.");
            else if (BlockedEnvironmentWords.Any(w => env.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Environment '{env.Name}' looks like production. This tool may only be used on DEV and TEST.");

            if (string.IsNullOrWhiteSpace(env.DrmsConnectionString))
                errors.Add($"Environment '{name}' has no DrmsConnectionString.");
            if (string.IsNullOrWhiteSpace(env.GatewayConnectionString))
                errors.Add($"Environment '{name}' has no GatewayConnectionString.");
            if (string.IsNullOrWhiteSpace(env.CommsConnectionString))
                errors.Add($"Environment '{name}' has no CommsConnectionString.");
        }

        var duplicates = settings.Environments
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var duplicate in duplicates)
            errors.Add($"Environment '{duplicate}' is configured more than once.");

        if (errors.Count > 0)
            throw new InvalidOperationException("The settings file has problems:" + Environment.NewLine + "- " +
                                                string.Join(Environment.NewLine + "- ", errors));
    }
}
