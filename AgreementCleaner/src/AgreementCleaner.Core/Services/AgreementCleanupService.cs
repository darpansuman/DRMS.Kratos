using System.Data;
using AgreementCleaner.Core.Configuration;
using AgreementCleaner.Core.Models;

namespace AgreementCleaner.Core.Services;

/// <summary>
/// Finds agreements and runs the clean-up scripts in the required order:
///   1. FindAgreement   (DRMS)    - get CustomerId and RentalContractId(s)
///   2. DeleteGateway   (Gateway) - once per RentalContractId
///   3. DeleteDRMS      (DRMS)    - by CustomerId
///   4. ClearCommsData  (Comms)   - by CustomerId
/// If a step fails, the steps after it are not run.
/// </summary>
public sealed class AgreementCleanupService
{
    private readonly ScriptRepository _scripts;
    private readonly ISqlExecutor _sql;

    public AgreementCleanupService(ScriptRepository scripts, ISqlExecutor sql)
    {
        _scripts = scripts;
        _sql = sql;
    }

    /// <summary>Step 1: find the agreements that match the search values.</summary>
    public async Task<IReadOnlyList<AgreementInfo>> FindAgreementsAsync(EnvironmentSettings environment,
        AgreementSearch search, CancellationToken cancellationToken = default)
    {
        var script = _scripts.LoadFindAgreement();
        var parameters = new[]
        {
            new ScriptParameter("@AgreementId", SqlDbType.NVarChar, search.AgreementId, 50),
            new ScriptParameter("@CustomerId", SqlDbType.UniqueIdentifier, search.CustomerId),
            new ScriptParameter("@Imei", SqlDbType.NVarChar, search.Imei, 50),
            new ScriptParameter("@PhoneNumber", SqlDbType.NVarChar, search.PhoneNumber, 50)
        };

        var table = await _sql.QueryAsync(environment.DrmsConnectionString, script.Text, parameters, cancellationToken);
        return ReadAgreements(table, script.Name);
    }

    /// <summary>
    /// Works out exactly what will be deleted for the chosen agreement.
    /// DeleteDRMS.sql removes every contract of the customer, so the gateway
    /// orders of every one of those contracts must be removed too.
    /// </summary>
    public async Task<CleanupPlan> BuildPlanAsync(EnvironmentSettings environment, AgreementInfo selected,
        CancellationToken cancellationToken = default)
    {
        var search = new AgreementSearch(null, selected.CustomerId, null, null);
        var agreements = await FindAgreementsAsync(environment, search, cancellationToken);

        if (agreements.Count == 0)
            throw new InvalidOperationException(
                $"Customer {selected.CustomerId} has no agreements any more. It may already have been deleted.");

        // The search can return one row per IMEI; keep one row per contract.
        var perContract = agreements
            .GroupBy(a => a.RentalContractId)
            .Select(g => g.First())
            .OrderBy(a => a.AgreementId, StringComparer.Ordinal)
            .ToList();

        return new CleanupPlan(selected.CustomerId, perContract);
    }

    /// <summary>Steps 2 to 4: run the delete scripts in the required order.</summary>
    public async Task<CleanupResult> RunCleanupAsync(EnvironmentSettings environment, CleanupPlan plan,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new CleanupResult();
        var gatewayStep = new StepResult(2, "Delete gateway orders", "Gateway");
        var drmsStep = new StepResult(3, "Delete DRMS data", "DRMS");
        var commsStep = new StepResult(4, "Clear Comms messages", "Comms");
        result.Steps.AddRange(new[] { gatewayStep, drmsStep, commsStep });

        // Load all scripts first. If one is missing or broken nothing is deleted.
        SqlScript gatewayScript, drmsScript, commsScript;
        try
        {
            gatewayScript = _scripts.LoadDeleteGateway();
            drmsScript = _scripts.LoadDeleteDrms();
            commsScript = _scripts.LoadClearComms();
        }
        catch (Exception ex)
        {
            gatewayStep.Status = StepStatus.Failed;
            gatewayStep.ErrorMessage = "Scripts could not be loaded, nothing was deleted. " + ex.Message;
            drmsStep.Status = StepStatus.Skipped;
            commsStep.Status = StepStatus.Skipped;
            progress?.Report("ERROR: " + gatewayStep.ErrorMessage);
            return result;
        }

        var steps = new (StepResult Step, Func<Task> Run)[]
        {
            (gatewayStep, async () =>
            {
                foreach (var rentalContractId in plan.RentalContractIds)
                {
                    progress?.Report($"  Gateway: RentalContractId {rentalContractId}");
                    var counts = await _sql.ExecuteCleanupAsync(environment.GatewayConnectionString, gatewayScript.Text,
                        new[] { new ScriptParameter("@RentalContractId", SqlDbType.UniqueIdentifier, rentalContractId) },
                        cancellationToken);
                    gatewayStep.Counts.AddRange(counts);
                }
            }),
            (drmsStep, async () =>
            {
                var counts = await _sql.ExecuteCleanupAsync(environment.DrmsConnectionString, drmsScript.Text,
                    new[] { new ScriptParameter("@CustomerId", SqlDbType.UniqueIdentifier, plan.CustomerId) },
                    cancellationToken);
                drmsStep.Counts.AddRange(counts);
            }),
            (commsStep, async () =>
            {
                var counts = await _sql.ExecuteCleanupAsync(environment.CommsConnectionString, commsScript.Text,
                    new[] { new ScriptParameter("@CustomerId", SqlDbType.UniqueIdentifier, plan.CustomerId) },
                    cancellationToken);
                commsStep.Counts.AddRange(counts);
            })
        };

        var failed = false;
        foreach (var (step, run) in steps)
        {
            if (failed)
            {
                step.Status = StepStatus.Skipped;
                progress?.Report($"Step {step.Number} - {step.Name}: skipped because an earlier step failed.");
                continue;
            }

            progress?.Report($"Step {step.Number} - {step.Name} ({step.Database}) started.");
            try
            {
                await run();
                step.Status = StepStatus.Succeeded;
                progress?.Report($"Step {step.Number} - {step.Name}: done, {step.TotalRowsAffected} row(s).");
            }
            catch (Exception ex)
            {
                failed = true;
                step.Status = StepStatus.Failed;
                step.ErrorMessage = ex.Message;
                progress?.Report($"ERROR in step {step.Number} - {step.Name}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>Turns the FindAgreement result into objects, with a clear error if a column is missing.</summary>
    internal static IReadOnlyList<AgreementInfo> ReadAgreements(DataTable table, string scriptName)
    {
        string[] required = { "RentalContractId", "AgreementId", "CustomerId", "Imei", "PhoneNumber" };
        var missing = required.Where(c => !table.Columns.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Script '{scriptName}' must return the columns {string.Join(", ", required)}. Missing: {string.Join(", ", missing)}.");

        var list = new List<AgreementInfo>();
        foreach (DataRow row in table.Rows)
        {
            list.Add(new AgreementInfo(
                ToGuid(row["RentalContractId"], "RentalContractId"),
                Convert.ToString(row["AgreementId"]) ?? "",
                ToGuid(row["CustomerId"], "CustomerId"),
                row["Imei"] is DBNull ? null : Convert.ToString(row["Imei"]),
                row["PhoneNumber"] is DBNull ? null : Convert.ToString(row["PhoneNumber"])));
        }
        return list;
    }

    private static Guid ToGuid(object value, string column) => value switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var g) => g,
        _ => throw new InvalidOperationException($"Column {column} has a value that is not a GUID: '{value}'.")
    };
}
