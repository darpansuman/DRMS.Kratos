namespace AgreementCleaner.Core.Configuration;

/// <summary>
/// Everything the app reads from appsettings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Folder that holds the SQL scripts. Normally a shared network folder
    /// (for example \\fileserver\Shared\AgreementCleaner\SqlScripts) so every
    /// user picks up script changes straight away. A relative path is
    /// resolved against the folder the app runs from.
    /// </summary>
    public string ScriptFolder { get; set; } = "";

    /// <summary>How long (in seconds) a single script may run before it is stopped.</summary>
    public int CommandTimeoutSeconds { get; set; } = 300;

    /// <summary>File names of the scripts inside <see cref="ScriptFolder"/>.</summary>
    public ScriptFileNames Scripts { get; set; } = new();

    /// <summary>The environments the user can pick from (DEV, TEST, ...).</summary>
    public List<EnvironmentSettings> Environments { get; set; } = new();
}

public sealed class ScriptFileNames
{
    public string FindAgreement { get; set; } = "FindAgreement.sql";
    public string DeleteGateway { get; set; } = "DeleteGateway.sql";
    public string DeleteDrms { get; set; } = "DeleteDRMS.sql";
    public string ClearComms { get; set; } = "ClearCommsData.sql";
}

/// <summary>
/// Connection strings for one environment. Each script runs on its own database.
/// </summary>
public sealed class EnvironmentSettings
{
    public string Name { get; set; } = "";
    public string DrmsConnectionString { get; set; } = "";
    public string GatewayConnectionString { get; set; } = "";
    public string CommsConnectionString { get; set; } = "";

    public override string ToString() => Name;
}
