namespace AgreementCleaner.Core.Models;

/// <summary>
/// The search values typed in by the user. A null value means the field was left empty.
/// </summary>
public sealed record AgreementSearch(string? AgreementId, Guid? CustomerId, string? Imei, string? PhoneNumber);

/// <summary>One row returned by FindAgreement.sql.</summary>
public sealed record AgreementInfo(
    Guid RentalContractId,
    string AgreementId,
    Guid CustomerId,
    string? Imei,
    string? PhoneNumber);

/// <summary>Number of rows a script deleted (or updated) in one table.</summary>
public sealed record TableCount(string TableName, int RowsAffected);

public enum StepStatus
{
    NotRun,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>The outcome of one step of the clean-up (one script).</summary>
public sealed class StepResult
{
    public StepResult(int number, string name, string database)
    {
        Number = number;
        Name = name;
        Database = database;
    }

    public int Number { get; }
    public string Name { get; }
    public string Database { get; }
    public StepStatus Status { get; set; } = StepStatus.NotRun;
    public List<TableCount> Counts { get; } = new();
    public string? ErrorMessage { get; set; }

    public int TotalRowsAffected => Counts.Sum(c => c.RowsAffected);
}

/// <summary>What will be deleted: one customer and all of the customer's rental contracts.</summary>
public sealed record CleanupPlan(Guid CustomerId, IReadOnlyList<AgreementInfo> Agreements)
{
    public IReadOnlyList<Guid> RentalContractIds =>
        Agreements.Select(a => a.RentalContractId).Distinct().ToList();
}

/// <summary>The outcome of the whole clean-up.</summary>
public sealed class CleanupResult
{
    public List<StepResult> Steps { get; } = new();

    public bool Succeeded => Steps.Count > 0 && Steps.All(s => s.Status == StepStatus.Succeeded);

    public int TotalRowsAffected => Steps.Sum(s => s.TotalRowsAffected);

    public StepResult? FailedStep => Steps.FirstOrDefault(s => s.Status == StepStatus.Failed);
}
