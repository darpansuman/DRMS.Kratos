using AgreementCleaner.Core.Configuration;
using AgreementCleaner.Core.Services;

namespace AgreementCleaner.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var log = new FileLog();

        // Last line of defence: show unexpected errors instead of crashing silently.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            log.Write("UNEXPECTED ERROR: " + e.Exception);
            MessageBox.Show("An unexpected error happened:" + Environment.NewLine + Environment.NewLine +
                            e.Exception.Message, "Agreement Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppSettings settings;
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        try
        {
            settings = AppSettingsLoader.Load(settingsPath);
        }
        catch (Exception ex)
        {
            log.Write("Settings error: " + ex.Message);
            MessageBox.Show(ex.Message + Environment.NewLine + Environment.NewLine + "Fix appsettings.json and start the app again.",
                "Agreement Cleaner - settings problem", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm(settings, log));
    }
}
