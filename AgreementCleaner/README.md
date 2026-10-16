# Agreement Cleaner

A small Windows app that removes a test agreement (and its customer data) from the **DEV** and **TEST** environments.
It runs SQL scripts that live in a shared folder, so the scripts can be changed without rebuilding the app.

> This tool deletes data and cannot undo it. It refuses to start if an environment name contains `PROD` or `LIVE`.

---

## 1. What it does

1. You pick the environment (DEV or TEST).
2. You type one or more of: **Agreement Id**, **Customer Id**, **IMEI**, **Phone Number**, **SIM Number**, and click **Find agreement**.
3. The app runs `FindAgreement.sql` and lists the matching agreements.
4. You select one and click **Delete selected agreement...**.
5. The app looks up **all** agreements of that customer and asks you to confirm, showing what will be removed.
6. After you click **Yes**, the scripts run in this fixed order:

| Step | Script | Database | Input |
|------|--------|----------|-------|
| 1 | `FindAgreement.sql` | DRMS | the search values → gives CustomerId and RentalContractId(s) |
| 2 | `DeleteGateway.sql` | Subscription Gateway | each RentalContractId (run once per contract) |
| 3 | `DeleteDRMS.sql` | DRMS | CustomerId |
| 4 | `ClearCommsData.sql` | Comms | CustomerId |

7. The **Result** table shows the number of rows deleted (or cleared) per table, plus a total.

If a step fails, the steps after it are **not** run. The app tells you which step failed and what to do next.
Every action is also written to a log file (see section 6).

### Why the customer's other agreements are included
`DeleteDRMS.sql` deletes by **CustomerId**, so it removes *every* contract the customer has.
To keep the Gateway database in step, the app deletes the gateway orders of every one of those contracts too.
The confirmation box lists all of them, so there are no surprises.

---

## 2. Project layout

```
AgreementCleaner/
├── AgreementCleaner.sln
├── SqlScripts/                      <- copy these to the shared folder
│   ├── FindAgreement.sql
│   ├── DeleteGateway.sql
│   ├── DeleteDRMS.sql
│   └── ClearCommsData.sql
├── src/
│   ├── AgreementCleaner.App/        <- Windows Forms app (UI only)
│   │   ├── Program.cs               start-up, loads settings, global error handler
│   │   ├── MainForm.cs              the window: layout, buttons, prompts, results
│   │   └── appsettings.json         script folder + connection strings
│   └── AgreementCleaner.Core/       <- all logic, no Windows code (unit tested)
│       ├── Configuration/           AppSettings + loader/validator
│       ├── Models/                  simple data classes
│       └── Services/
│           ├── InputValidator.cs        checks and cleans the search fields
│           ├── ScriptRepository.cs      reads scripts from the shared folder
│           ├── SqlExecutor.cs           runs a script, reads the row counts
│           ├── AgreementCleanupService.cs  the find + delete sequence
│           └── FileLog.cs               daily log file
└── tests/AgreementCleaner.Core.Tests/  xUnit tests for the Core project
```

The UI is built in code inside `MainForm.cs` (no designer file), so everything about the window is in one readable place.

---

## 3. Requirements

- Windows 10/11
- .NET 8 Desktop Runtime (to run) or .NET 8 SDK (to build)
- Read access to the shared script folder
- A SQL login (or Windows account) that can read and delete in the DEV/TEST DRMS, Gateway and Comms databases

---

## 4. Setup

1. **Copy the scripts** from `SqlScripts/` to a shared folder, for example
   `\\fileserver\Shared\AgreementCleaner\SqlScripts`.
2. **Edit `appsettings.json`** (next to `AgreementCleaner.exe` after building):

```jsonc
{
  "ScriptFolder": "\\\\fileserver\\Shared\\AgreementCleaner\\SqlScripts",
  "CommandTimeoutSeconds": 300,
  "Scripts": {                         // file names inside ScriptFolder
    "FindAgreement": "FindAgreement.sql",
    "DeleteGateway": "DeleteGateway.sql",
    "DeleteDrms": "DeleteDRMS.sql",
    "ClearComms": "ClearCommsData.sql"
  },
  "Environments": [
    {
      "Name": "DEV",
      "DrmsConnectionString":    "Server=...;Database=DRMS;...",
      "GatewayConnectionString": "Server=...;Database=SubscriptionGateway;...",
      "CommsConnectionString":   "Server=...;Database=Comms;..."
    },
    { "Name": "TEST", "...": "..." }
  ]
}
```

   - In JSON, every `\` must be written as `\\`.
   - Comments (`// ...`) are allowed in this file.
   - A relative `ScriptFolder` (e.g. `"SqlScripts"`) is resolved against the folder of the `.exe` – handy for local testing.
3. Start the app and click **Check scripts**. Every script should say `OK`.

---

## 5. Build and run

```bash
dotnet build AgreementCleaner.sln
dotnet test  AgreementCleaner.sln
dotnet run --project src/AgreementCleaner.App
```

To make a folder you can copy to other PCs:

```bash
dotnet publish src/AgreementCleaner.App -c Release -r win-x64 --self-contained false -o publish
```

Then copy the `publish` folder and edit `publish\appsettings.json`.

---

## 6. Using the app

| Field | Format | Example |
|-------|--------|---------|
| Agreement Id | whole number | `713` |
| Customer Id | GUID (with or without `{}`) | `F2246EE9-46D9-8560-9E2A-46257ACD17B0` |
| IMEI | 14–16 digits | `350142322650009` |
| Phone Number | 7–15 digits; spaces, dashes, brackets and a leading `+` are removed | `068 920 9279` |
| SIM Number | SIM card number (ICCID), 10–22 digits; spaces are removed | `8927000011760597297` |

- Fill in **at least one** field. If you fill in more than one, **all** of them must match.
- Phone numbers are matched with "contains", so `689209279` also finds `0689209279` and `27689209279`.
- SIM numbers are also matched with "contains" (`ImeiSimDetails.SimNo LIKE '%...%'`), so you can type part of the number (at least 10 digits).
- The list of matching agreements shows the IMEI, SIM Number and Phone Number linked to each agreement.
- Press **Enter** to search.
- Changing the environment clears the list of matches, so you can never delete in the wrong environment by accident.

**Log file:** `%LOCALAPPDATA%\AgreementCleaner\logs\AgreementCleaner-yyyyMMdd.log`
(includes the Windows user name, the environment, what was deleted and the counts).

### If something goes wrong

| Failed step | State of the data | What to do |
|-------------|-------------------|------------|
| Loading scripts | Nothing changed | Fix the script folder / file, click **Check scripts**, try again. |
| 2 – Gateway | DRMS and Comms untouched (each script runs in its own transaction) | Fix the problem and run again. |
| 3 – DRMS | Gateway orders deleted, DRMS rolled back | Fix the problem and run again – repeating is safe. |
| 4 – Comms | Gateway and DRMS deleted, Comms not cleared | The agreement can no longer be found in the app. Run `ClearCommsData.sql` by hand in SSMS with the CustomerId shown in the error. |

The three databases are separate, so there is no single transaction across them. That is why the order matters and why a failed step stops the rest.

---

## 7. Changing the SQL scripts (the "contract")

You can change the scripts on the share at any time; the app reads them fresh every time it uses them.
Keep to these rules so the app can still run them:

1. **Do not `DECLARE` the input parameters.** The app passes them in:

   | Script | Parameters |
   |--------|-----------|
   | `FindAgreement.sql` | `@AgreementId` (nvarchar), `@CustomerId` (uniqueidentifier), `@Imei` (nvarchar), `@PhoneNumber` (nvarchar, digits only), `@SimNumber` (nvarchar, digits only). Empty fields arrive as `NULL`. |
   | `DeleteGateway.sql` | `@RentalContractId` (uniqueidentifier) |
   | `DeleteDRMS.sql` | `@CustomerId` (uniqueidentifier) |
   | `ClearCommsData.sql` | `@CustomerId` (uniqueidentifier) |

   Each script has a commented-out `DECLARE` block at the top – un-comment it to test the script in SSMS, then comment it out again before saving to the share.

2. **`FindAgreement.sql` must return the columns**
   `RentalContractId, AgreementId, CustomerId, Imei, PhoneNumber, SimNo`.

3. **The delete scripts must end with a result set that has the columns `TableName, RowsAffected`.**
   The pattern used in all three scripts:

   ```sql
   DECLARE @Results TABLE (TableName NVARCHAR(200), RowsAffected INT);
   ...
   DELETE FROM SomeTable WHERE ...;
   INSERT INTO @Results VALUES ('SomeTable', @@ROWCOUNT);   -- straight after the DELETE
   ...
   SELECT TableName, RowsAffected FROM @Results;            -- last statement
   ```

   When you add a new `DELETE`, add the `INSERT INTO @Results` line right after it, or its rows will not be counted.

4. **No `GO` lines.** `GO` is an SSMS command, not SQL. The app refuses scripts that contain it.

5. Keep the `BEGIN TRY / BEGIN TRANSACTION / COMMIT / CATCH ROLLBACK; THROW;` pattern so a failure rolls the script back and the app sees the error.

After changing a script, click **Check scripts** in the app. You can also run `dotnet test` – some tests check the scripts in `SqlScripts/` follow these rules.

### Changes made to the original scripts
The original scripts had hard-coded IDs. They were changed to use the parameters above and to report row counts.
The delete logic itself is the same, with these notes:

- `FindAgreement.sql` is new. It is built from the commented `SELECT` lines at the top of the original `DeleteDRMS.sql`
  (`RentalContracts`, `ImeiSimDetails`), joined through `ContractDetail`. Adjust the column names if your schema differs.
- `DeleteGateway.sql`: the "check impact" `SELECT` and `@@ROWCOUNT` select were replaced by the `@Results` pattern; it now rolls back on error.
- `ClearCommsData.sql`: the fixed "fake" GUID is now a new `NEWID()` on each run. Messages are still *updated*, not deleted, and are counted as "cleared".
- `DeleteDRMS.sql`: now inside `TRY/CATCH` so it rolls back on error.
  **Note:** step 8 (orphan `Products`) was kept from the original. It removes every product that no cart uses – not only this customer's.

---

## 8. Where to change things (for developers)

| I want to... | Change |
|--------------|--------|
| Add an environment | `appsettings.json` → `Environments` |
| Move the scripts | `appsettings.json` → `ScriptFolder` |
| Change what is deleted | the `.sql` file on the share (follow section 7) |
| Change validation of a field | `Core/Services/InputValidator.cs` |
| Add a step to the sequence | `Core/Services/AgreementCleanupService.cs` → `RunCleanupAsync`, plus a script name in `AppSettings.ScriptFileNames` |
| Change the window | `App/MainForm.cs` |
| Block more environment names | `Core/Configuration/AppSettingsLoader.cs` → `BlockedEnvironmentWords` |
