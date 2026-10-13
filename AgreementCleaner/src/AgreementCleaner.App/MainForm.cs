using System.Diagnostics;
using AgreementCleaner.Core.Configuration;
using AgreementCleaner.Core.Models;
using AgreementCleaner.Core.Services;

namespace AgreementCleaner.App;

/// <summary>
/// The main (and only) window.
///
/// Flow for the user:
///   1. Pick the environment (DEV / TEST).
///   2. Type one or more search values and click "Find agreement".
///   3. Select the agreement in the list and click "Delete selected agreement...".
///   4. Confirm. The scripts run in order and the rows deleted are shown per table.
///
/// The layout is built in code (no designer file) so it is easy to read and change.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly FileLog _fileLog;
    private readonly ScriptRepository _scripts;
    private readonly AgreementCleanupService _service;

    // Header
    private readonly ComboBox _environmentCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Label _environmentBadge = new() { AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(8, 3, 3, 3) };
    private readonly Label _scriptFolderLabel = new() { AutoSize = true, Margin = new Padding(16, 8, 3, 3) };
    private readonly Button _checkScriptsButton = new() { Text = "Check scripts", AutoSize = true };
    private readonly Button _openFolderButton = new() { Text = "Open script folder", AutoSize = true };

    // Search
    private readonly TextBox _agreementIdText = new() { Width = 320 };
    private readonly TextBox _customerIdText = new() { Width = 320 };
    private readonly TextBox _imeiText = new() { Width = 320 };
    private readonly TextBox _phoneText = new() { Width = 320 };
    private readonly Button _findButton = new() { Text = "Find agreement", AutoSize = true };
    private readonly Button _clearButton = new() { Text = "Clear", AutoSize = true };

    // Matches
    private readonly DataGridView _matchesGrid = CreateGrid();
    private readonly Label _matchesInfoLabel = new() { AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
    private readonly Button _deleteButton = new()
    {
        Text = "Delete selected agreement...",
        AutoSize = true,
        Enabled = false,
        BackColor = Color.Firebrick,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    // Results
    private readonly DataGridView _resultsGrid = CreateGrid();
    private readonly Label _totalLabel = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold), Margin = new Padding(3, 6, 3, 3) };

    // Log + status
    private readonly TextBox _logText = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progressBar = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 150 };

    private bool _busy;

    public MainForm(AppSettings settings, FileLog fileLog)
    {
        _settings = settings;
        _fileLog = fileLog;
        _scripts = new ScriptRepository(settings);
        _service = new AgreementCleanupService(_scripts, new SqlServerExecutor(settings.CommandTimeoutSeconds, AppendLog));

        Text = "Agreement Cleaner - DEV / TEST only";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 760);
        Size = new Size(1150, 860);
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        WireEvents();

        _environmentCombo.Items.AddRange(settings.Environments.Cast<object>().ToArray());
        _environmentCombo.SelectedIndex = 0;
        _scriptFolderLabel.Text = "Scripts: " + _scripts.ScriptFolder;

        SetStatus("Ready. Fill in at least one search field and click \"Find agreement\".");
        AppendLog($"App started. Script folder: {_scripts.ScriptFolder}");
        AppendLog($"Log file: {_fileLog.CurrentFile}");
    }

    private EnvironmentSettings SelectedEnvironment => (EnvironmentSettings)_environmentCombo.SelectedItem!;

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // search
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));   // matches
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));   // results
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));   // log

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSearchGroup(), 0, 1);
        root.Controls.Add(BuildMatchesGroup(), 0, 2);
        root.Controls.Add(BuildResultsGroup(), 0, 3);
        root.Controls.Add(BuildLogGroup(), 0, 4);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_progressBar);

        Controls.Add(root);
        Controls.Add(statusStrip);
        AcceptButton = _findButton;
    }

    private Control BuildHeader()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        panel.Controls.Add(new Label { Text = "Environment:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        panel.Controls.Add(_environmentCombo);
        panel.Controls.Add(_environmentBadge);
        panel.Controls.Add(_scriptFolderLabel);
        panel.Controls.Add(_checkScriptsButton);
        panel.Controls.Add(_openFolderButton);
        return panel;
    }

    private Control BuildSearchGroup()
    {
        var group = new GroupBox { Text = "1. Find the agreement (fill in one or more fields - all filled-in values must match)", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(grid, 0, "Agreement Id:", _agreementIdText, "Numbers only, e.g. 713");
        AddField(grid, 1, "Customer Id:", _customerIdText, "GUID, e.g. F2246EE9-46D9-8560-9E2A-46257ACD17B0");
        AddField(grid, 2, "IMEI:", _imeiText, "14 to 16 digits, e.g. 350142322650009");
        AddField(grid, 3, "Phone Number:", _phoneText, "Contact number, e.g. 068 920 9279 or +27689209279");

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.Add(_findButton);
        buttons.Controls.Add(_clearButton);
        grid.Controls.Add(buttons, 1, 4);

        group.Controls.Add(grid);
        return group;
    }

    private static void AddField(TableLayoutPanel grid, int row, string caption, TextBox textBox, string hint)
    {
        grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 10, 3) }, 0, row);
        grid.Controls.Add(textBox, 1, row);
        grid.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 3, 3) }, 2, row);
    }

    private Control BuildMatchesGroup()
    {
        var group = new GroupBox { Text = "2. Matching agreements (select one)", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        bottom.Controls.Add(_deleteButton);
        bottom.Controls.Add(_matchesInfoLabel);

        _matchesGrid.Columns.AddRange(
            TextColumn(nameof(AgreementInfo.AgreementId), "Agreement Id", 110),
            TextColumn(nameof(AgreementInfo.CustomerId), "Customer Id", 290),
            TextColumn(nameof(AgreementInfo.RentalContractId), "Rental Contract Id", 290),
            TextColumn(nameof(AgreementInfo.Imei), "IMEI", 150),
            TextColumn(nameof(AgreementInfo.PhoneNumber), "Phone Number", 150));

        group.Controls.Add(_matchesGrid);
        group.Controls.Add(bottom);
        return group;
    }

    private Control BuildResultsGroup()
    {
        var group = new GroupBox { Text = "3. Result - records deleted", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _totalLabel.Dock = DockStyle.Bottom;

        _resultsGrid.Columns.AddRange(
            TextColumn("Step", "Step", 50),
            TextColumn("Name", "Action", 180),
            TextColumn("Database", "Database", 90),
            TextColumn("Table", "Table", 260),
            TextColumn("Rows", "Rows affected", 110),
            TextColumn("Status", "Status", 300));
        _resultsGrid.Columns["Rows"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        group.Controls.Add(_resultsGrid);
        group.Controls.Add(_totalLabel);
        return group;
    }

    private Control BuildLogGroup()
    {
        var group = new GroupBox { Text = "Log", Dock = DockStyle.Fill, Padding = new Padding(10) };
        group.Controls.Add(_logText);
        return group;
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window
    };

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width) => new()
    {
        Name = name,
        DataPropertyName = name,
        HeaderText = header,
        Width = width
    };

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    private void WireEvents()
    {
        _environmentCombo.SelectedIndexChanged += (_, _) => OnEnvironmentChanged();
        _checkScriptsButton.Click += (_, _) => CheckScripts();
        _openFolderButton.Click += (_, _) => OpenScriptFolder();
        _findButton.Click += async (_, _) => await FindAsync();
        _clearButton.Click += (_, _) => ClearAll();
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        _matchesGrid.SelectionChanged += (_, _) => UpdateDeleteButton();
        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                MessageBox.Show(this, "Please wait until the current action has finished.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
            }
        };
    }

    private void OnEnvironmentChanged()
    {
        var name = SelectedEnvironment.Name;
        _environmentBadge.Text = name.ToUpperInvariant();
        _environmentBadge.BackColor = name.Contains("DEV", StringComparison.OrdinalIgnoreCase) ? Color.SeaGreen
            : name.Contains("TEST", StringComparison.OrdinalIgnoreCase) ? Color.DarkOrange
            : Color.SlateGray;
        _environmentBadge.ForeColor = Color.White;

        // Results found in another environment are no longer valid.
        _matchesGrid.DataSource = null;
        _matchesInfoLabel.Text = "";
        UpdateDeleteButton();
        AppendLog($"Environment set to {name}.");
    }

    private void CheckScripts()
    {
        var lines = _scripts.CheckAll();
        foreach (var (fileName, ok, message) in lines)
            AppendLog($"Script check: {fileName} - {(ok ? "" : "PROBLEM: ")}{message}");

        var allOk = lines.All(l => l.Ok);
        var text = $"Script folder: {_scripts.ScriptFolder}{Environment.NewLine}{Environment.NewLine}" +
                   string.Join(Environment.NewLine, lines.Select(l => $"{(l.Ok ? "OK     " : "ERROR  ")}{l.FileName}: {l.Message}"));
        MessageBox.Show(this, text, allOk ? "All scripts found" : "Script problems",
            MessageBoxButtons.OK, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void OpenScriptFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _scripts.ScriptFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("The script folder could not be opened.", ex.Message);
        }
    }

    private void ClearAll()
    {
        _agreementIdText.Clear();
        _customerIdText.Clear();
        _imeiText.Clear();
        _phoneText.Clear();
        _matchesGrid.DataSource = null;
        _resultsGrid.Rows.Clear();
        _matchesInfoLabel.Text = "";
        _totalLabel.Text = "";
        UpdateDeleteButton();
        SetStatus("Cleared.");
        _agreementIdText.Focus();
    }

    // ------------------------------------------------------------------
    // Step 1: find
    // ------------------------------------------------------------------

    private async Task FindAsync()
    {
        if (_busy) return;

        var validation = InputValidator.Validate(_agreementIdText.Text, _customerIdText.Text, _imeiText.Text, _phoneText.Text);
        if (!validation.IsValid)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, validation.Errors), "Please check the search fields",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var search = validation.Search!;
        var environment = SelectedEnvironment;
        _matchesGrid.DataSource = null;
        _resultsGrid.Rows.Clear();
        _totalLabel.Text = "";

        SetBusy(true, $"Searching {environment.Name}...");
        AppendLog($"[{environment.Name}] Search: AgreementId={search.AgreementId ?? "-"}, CustomerId={search.CustomerId?.ToString() ?? "-"}, " +
                  $"IMEI={search.Imei ?? "-"}, Phone={search.PhoneNumber ?? "-"}");
        try
        {
            var agreements = await _service.FindAgreementsAsync(environment, search);
            _matchesGrid.DataSource = agreements.ToList();
            AppendLog($"[{environment.Name}] {agreements.Count} matching row(s) found.");

            if (agreements.Count == 0)
            {
                _matchesInfoLabel.Text = "No agreement found.";
                SetStatus("No agreement found.");
                MessageBox.Show(this, $"No agreement was found in {environment.Name} for the values you entered.",
                    "Nothing found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var customers = agreements.Select(a => a.CustomerId).Distinct().Count();
                _matchesInfoLabel.Text = $"{agreements.Count} row(s) found for {customers} customer(s). Select one and click Delete.";
                SetStatus("Agreement found. Select it and click \"Delete selected agreement...\".");
                _matchesGrid.Focus();
            }
        }
        catch (Exception ex)
        {
            AppendLog("ERROR during search: " + ex.Message);
            SetStatus("Search failed.");
            ShowError("The search failed.", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ------------------------------------------------------------------
    // Steps 2 - 4: confirm and delete
    // ------------------------------------------------------------------

    private async Task DeleteSelectedAsync()
    {
        if (_busy || _matchesGrid.CurrentRow?.DataBoundItem is not AgreementInfo selected) return;

        var environment = SelectedEnvironment;
        CleanupPlan plan;

        SetBusy(true, "Checking what will be deleted...");
        try
        {
            plan = await _service.BuildPlanAsync(environment, selected);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR while preparing the delete: " + ex.Message);
            SetStatus("Could not prepare the delete.");
            ShowError("Could not prepare the delete.", ex.Message);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        if (!ConfirmDelete(environment, plan))
        {
            AppendLog("Delete cancelled by the user.");
            SetStatus("Delete cancelled. Nothing was changed.");
            return;
        }

        SetBusy(true, $"Deleting data in {environment.Name}... please wait.");
        AppendLog($"[{environment.Name}] DELETE started for CustomerId {plan.CustomerId}, agreement(s) " +
                  string.Join(", ", plan.Agreements.Select(a => a.AgreementId)));

        CleanupResult result;
        try
        {
            result = await _service.RunCleanupAsync(environment, plan, new Progress<string>(AppendLog));
        }
        finally
        {
            SetBusy(false);
        }

        ShowResult(result);
        ReportResult(environment, plan, result);

        // The agreement list is out of date now.
        _matchesGrid.DataSource = null;
        _matchesInfoLabel.Text = "";
        UpdateDeleteButton();
    }

    private bool ConfirmDelete(EnvironmentSettings environment, CleanupPlan plan)
    {
        var agreements = string.Join(Environment.NewLine,
            plan.Agreements.Select(a => $"   - Agreement {a.AgreementId}  (RentalContractId {a.RentalContractId})"));

        var message =
            $"You are about to DELETE data in {environment.Name.ToUpperInvariant()}.{Environment.NewLine}{Environment.NewLine}" +
            $"Customer Id: {plan.CustomerId}{Environment.NewLine}" +
            $"Agreements of this customer that will be removed ({plan.Agreements.Count}):{Environment.NewLine}{agreements}{Environment.NewLine}{Environment.NewLine}" +
            $"The scripts will run in this order:{Environment.NewLine}" +
            $"   1. DeleteGateway   - gateway orders of each agreement{Environment.NewLine}" +
            $"   2. DeleteDRMS      - all DRMS data of the customer{Environment.NewLine}" +
            $"   3. ClearCommsData  - customer messages in Comms{Environment.NewLine}{Environment.NewLine}" +
            "This cannot be undone. Do you want to continue?";

        return MessageBox.Show(this, message, $"Confirm delete - {environment.Name}",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void ShowResult(CleanupResult result)
    {
        _resultsGrid.Rows.Clear();
        foreach (var step in result.Steps)
        {
            var status = step.Status switch
            {
                StepStatus.Succeeded => "Done",
                StepStatus.Failed => "FAILED: " + step.ErrorMessage,
                StepStatus.Skipped => "Skipped (earlier step failed)",
                _ => "Not run"
            };

            if (step.Counts.Count == 0)
            {
                AddResultRow(step, "-", "-", status);
                continue;
            }

            // Gateway runs once per contract, so add up rows for the same table.
            foreach (var group in step.Counts.GroupBy(c => c.TableName))
                AddResultRow(step, group.Key, group.Sum(c => c.RowsAffected).ToString("N0"), status);
        }

        _totalLabel.Text = $"Total records deleted / cleared: {result.TotalRowsAffected:N0}";
        _totalLabel.ForeColor = result.Succeeded ? Color.SeaGreen : Color.Firebrick;
    }

    private void AddResultRow(StepResult step, string table, string rows, string status)
    {
        var index = _resultsGrid.Rows.Add(step.Number, step.Name, step.Database, table, rows, status);
        _resultsGrid.Rows[index].DefaultCellStyle.ForeColor = step.Status switch
        {
            StepStatus.Failed => Color.Firebrick,
            StepStatus.Skipped => SystemColors.GrayText,
            _ => SystemColors.ControlText
        };
    }

    private void ReportResult(EnvironmentSettings environment, CleanupPlan plan, CleanupResult result)
    {
        var perStep = string.Join(Environment.NewLine,
            result.Steps.Select(s => $"   {s.Name}: {s.TotalRowsAffected:N0} row(s) - {s.Status}"));

        if (result.Succeeded)
        {
            AppendLog($"[{environment.Name}] DELETE finished. Total rows: {result.TotalRowsAffected}.");
            SetStatus($"Done. {result.TotalRowsAffected:N0} record(s) deleted / cleared.");
            MessageBox.Show(this,
                $"Clean-up finished in {environment.Name}.{Environment.NewLine}{Environment.NewLine}{perStep}{Environment.NewLine}{Environment.NewLine}" +
                $"Total: {result.TotalRowsAffected:N0} record(s).",
                "Clean-up complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var failed = result.FailedStep;
        AppendLog($"[{environment.Name}] DELETE did not finish. Failed step: {failed?.Name}.");
        SetStatus("Clean-up did NOT finish. See the result table and log.");

        // Tell the user exactly what state the data is in and how to recover.
        var advice = failed?.Number switch
        {
            2 => "Nothing was deleted in DRMS or Comms. Fix the problem and run the clean-up again.",
            3 => "Gateway orders were deleted, but the DRMS delete was rolled back. Fix the problem and run the clean-up again - it is safe to repeat.",
            4 => $"Gateway and DRMS data were deleted, but Comms was not cleared. The agreement can no longer be found in the app, " +
                 $"so run ClearCommsData.sql by hand for CustomerId {plan.CustomerId}.",
            _ => "Check the log for details."
        };

        ShowError($"The clean-up did not finish (step: {failed?.Name}).",
            $"{failed?.ErrorMessage}{Environment.NewLine}{Environment.NewLine}{perStep}{Environment.NewLine}{Environment.NewLine}{advice}");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void UpdateDeleteButton()
    {
        _deleteButton.Enabled = !_busy && _matchesGrid.CurrentRow?.DataBoundItem is AgreementInfo;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _progressBar.Visible = busy;
        foreach (var control in new Control[]
                 {
                     _environmentCombo, _checkScriptsButton, _openFolderButton, _agreementIdText, _customerIdText,
                     _imeiText, _phoneText, _findButton, _clearButton, _matchesGrid
                 })
        {
            control.Enabled = !busy;
        }
        UpdateDeleteButton();
        if (status is not null) SetStatus(status);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>Adds a line to the log box and the log file. Safe to call from any thread.</summary>
    private void AppendLog(string message)
    {
        _fileLog.Write(message);

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog_UI(message));
            return;
        }
        AppendLog_UI(message);
    }

    private void AppendLog_UI(string message)
    {
        _logText.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }

    private void ShowError(string title, string details)
    {
        MessageBox.Show(this, title + Environment.NewLine + Environment.NewLine + details, "Agreement Cleaner - error",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
