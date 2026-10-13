using System.Data;
using AgreementCleaner.Core.Configuration;
using AgreementCleaner.Core.Models;
using AgreementCleaner.Core.Services;
using Xunit;

namespace AgreementCleaner.Core.Tests;

public class AgreementCleanupServiceTests
{
    private static readonly Guid Customer = Guid.Parse("F2246EE9-46D9-8560-9E2A-46257ACD17B0");
    private static readonly Guid Contract1 = Guid.Parse("E5F5177D-B333-4F5A-264B-08DEB720F17A");
    private static readonly Guid Contract2 = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly EnvironmentSettings Env = new()
    {
        Name = "DEV",
        DrmsConnectionString = "drms",
        GatewayConnectionString = "gateway",
        CommsConnectionString = "comms"
    };

    private static AgreementCleanupService CreateService(FakeSqlExecutor sql) =>
        new(new ScriptRepository(new AppSettings { ScriptFolder = Path.Combine(AppContext.BaseDirectory, "SqlScripts") }), sql);

    private static CleanupPlan Plan() => new(Customer, new[]
    {
        new AgreementInfo(Contract1, "713", Customer, "350142322650009", "0689209279"),
        new AgreementInfo(Contract2, "714", Customer, null, null)
    });

    [Fact]
    public async Task Scripts_run_in_the_required_order_with_the_right_inputs()
    {
        var sql = new FakeSqlExecutor();
        var result = await CreateService(sql).RunCleanupAsync(Env, Plan());

        Assert.True(result.Succeeded);
        Assert.Equal(new[]
        {
            $"gateway:@RentalContractId={Contract1}",
            $"gateway:@RentalContractId={Contract2}",
            $"drms:@CustomerId={Customer}",
            $"comms:@CustomerId={Customer}"
        }, sql.Calls);

        // Fake returns 2 rows per call: 2 gateway calls + DRMS + Comms = 8.
        Assert.Equal(8, result.TotalRowsAffected);
    }

    [Fact]
    public async Task Later_steps_are_skipped_when_a_step_fails()
    {
        var sql = new FakeSqlExecutor { FailOn = "drms" };
        var result = await CreateService(sql).RunCleanupAsync(Env, Plan());

        Assert.False(result.Succeeded);
        Assert.Equal(StepStatus.Succeeded, result.Steps[0].Status);
        Assert.Equal(StepStatus.Failed, result.Steps[1].Status);
        Assert.Equal(StepStatus.Skipped, result.Steps[2].Status);
        Assert.DoesNotContain(sql.Calls, c => c.StartsWith("comms"));
        Assert.Equal("boom", result.FailedStep!.ErrorMessage);
    }

    [Fact]
    public async Task Nothing_runs_when_a_script_is_missing()
    {
        var sql = new FakeSqlExecutor();
        var service = new AgreementCleanupService(
            new ScriptRepository(new AppSettings { ScriptFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }), sql);

        var result = await service.RunCleanupAsync(Env, Plan());

        Assert.False(result.Succeeded);
        Assert.Empty(sql.Calls);
        Assert.Contains("nothing was deleted", result.Steps[0].ErrorMessage);
    }

    [Fact]
    public async Task Plan_contains_every_contract_of_the_customer_once()
    {
        var sql = new FakeSqlExecutor();
        // Contract1 appears twice (two IMEIs) - it must only be in the plan once.
        sql.FindRows.Add((Contract1, "713", Customer, "1"));
        sql.FindRows.Add((Contract1, "713", Customer, "2"));
        sql.FindRows.Add((Contract2, "714", Customer, null));

        var selected = new AgreementInfo(Contract1, "713", Customer, null, null);
        var plan = await CreateService(sql).BuildPlanAsync(Env, selected);

        Assert.Equal(new[] { Contract1, Contract2 }, plan.RentalContractIds);
        Assert.Contains($"find:@CustomerId={Customer}", sql.Calls);
    }

    [Fact]
    public async Task Plan_fails_when_customer_no_longer_exists()
    {
        var sql = new FakeSqlExecutor();
        var selected = new AgreementInfo(Contract1, "713", Customer, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(sql).BuildPlanAsync(Env, selected));
    }

    [Fact]
    public void Missing_columns_in_find_result_give_a_clear_message()
    {
        var table = new DataTable();
        table.Columns.Add("RentalContractId", typeof(Guid));
        var ex = Assert.Throws<InvalidOperationException>(() => AgreementCleanupService.ReadAgreements(table, "FindAgreement.sql"));
        Assert.Contains("Missing: AgreementId, CustomerId, Imei, PhoneNumber", ex.Message);
    }

    /// <summary>Records every call instead of talking to SQL Server.</summary>
    private sealed class FakeSqlExecutor : ISqlExecutor
    {
        public List<string> Calls { get; } = new();
        public List<(Guid Contract, string AgreementId, Guid Customer, string? Imei)> FindRows { get; } = new();
        public string? FailOn { get; set; }

        public Task<DataTable> QueryAsync(string connectionString, string sql, IReadOnlyList<ScriptParameter> parameters,
            CancellationToken cancellationToken)
        {
            var customer = parameters.Single(p => p.Name == "@CustomerId").Value;
            Calls.Add($"find:@CustomerId={customer}");

            var table = new DataTable();
            table.Columns.Add("RentalContractId", typeof(Guid));
            table.Columns.Add("AgreementId", typeof(string));
            table.Columns.Add("CustomerId", typeof(Guid));
            table.Columns.Add("Imei", typeof(string));
            table.Columns.Add("PhoneNumber", typeof(string));
            foreach (var row in FindRows)
                table.Rows.Add(row.Contract, row.AgreementId, row.Customer, (object?)row.Imei ?? DBNull.Value, DBNull.Value);
            return Task.FromResult(table);
        }

        public Task<IReadOnlyList<TableCount>> ExecuteCleanupAsync(string connectionString, string sql,
            IReadOnlyList<ScriptParameter> parameters, CancellationToken cancellationToken)
        {
            var p = parameters.Single();
            Calls.Add($"{connectionString}:{p.Name}={p.Value}");
            if (connectionString == FailOn)
                throw new InvalidOperationException("boom");
            return Task.FromResult<IReadOnlyList<TableCount>>(new[] { new TableCount("T", 2) });
        }
    }
}
