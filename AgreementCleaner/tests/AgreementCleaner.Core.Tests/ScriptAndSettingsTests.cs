using AgreementCleaner.Core.Configuration;
using AgreementCleaner.Core.Services;
using Xunit;

namespace AgreementCleaner.Core.Tests;

public class ScriptAndSettingsTests
{
    private static string ShippedScriptFolder => Path.Combine(AppContext.BaseDirectory, "SqlScripts");

    [Fact]
    public void Shipped_scripts_all_load()
    {
        var repository = new ScriptRepository(new AppSettings { ScriptFolder = ShippedScriptFolder });
        var results = repository.CheckAll();
        Assert.All(results, r => Assert.True(r.Ok, $"{r.FileName}: {r.Message}"));
    }

    [Theory]
    [InlineData("DeleteGateway.sql")]
    [InlineData("DeleteDRMS.sql")]
    [InlineData("ClearCommsData.sql")]
    public void Delete_scripts_report_counts_and_do_not_declare_their_parameters(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(ShippedScriptFolder, fileName));

        Assert.Contains("SELECT TableName, RowsAffected FROM @Results", text);

        // Parameters come from the app. An active DECLARE would cause "variable already declared".
        var activeLines = text.Split('\n').Where(l => !l.TrimStart().StartsWith("--"));
        Assert.DoesNotContain(activeLines, l => l.Contains("DECLARE @CustomerId ", StringComparison.OrdinalIgnoreCase) ||
                                                l.Contains("DECLARE @RentalContractId ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Script_with_GO_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ScriptRepository.Check("x.sql", "SELECT 1\r\nGO\r\nSELECT 2"));
        Assert.Contains("GO", ex.Message);
    }

    [Fact]
    public void Word_GO_inside_a_line_is_allowed()
    {
        ScriptRepository.Check("x.sql", "SELECT 'GOOD' AS GoValue -- go on");
    }

    [Fact]
    public void Missing_script_folder_gives_a_clear_message()
    {
        var repository = new ScriptRepository(new AppSettings { ScriptFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) });
        var ex = Assert.Throws<InvalidOperationException>(() => repository.LoadDeleteDrms());
        Assert.Contains("script folder cannot be found", ex.Message);
    }

    [Theory]
    [InlineData("PROD")]
    [InlineData("Live-EU")]
    [InlineData("preprod")]
    public void Production_environment_is_refused(string name)
    {
        var settings = ValidSettings();
        settings.Environments[0].Name = name;
        var ex = Assert.Throws<InvalidOperationException>(() => AppSettingsLoader.Validate(settings));
        Assert.Contains("production", ex.Message);
    }

    [Fact]
    public void Valid_settings_pass()
    {
        AppSettingsLoader.Validate(ValidSettings());
    }

    [Fact]
    public void Settings_file_with_comments_loads()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              // comment
              "ScriptFolder": "C:\\Scripts",
              "Environments": [
                { "Name": "DEV", "DrmsConnectionString": "a", "GatewayConnectionString": "b", "CommsConnectionString": "c" },
              ]
            }
            """);
        try
        {
            var settings = AppSettingsLoader.Load(path);
            Assert.Equal("DEV", settings.Environments.Single().Name);
            Assert.Equal("DeleteDRMS.sql", settings.Scripts.DeleteDrms);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AppSettings ValidSettings() => new()
    {
        ScriptFolder = "C:\\Scripts",
        Environments =
        {
            new EnvironmentSettings { Name = "DEV", DrmsConnectionString = "a", GatewayConnectionString = "b", CommsConnectionString = "c" },
            new EnvironmentSettings { Name = "TEST", DrmsConnectionString = "a", GatewayConnectionString = "b", CommsConnectionString = "c" }
        }
    };
}
