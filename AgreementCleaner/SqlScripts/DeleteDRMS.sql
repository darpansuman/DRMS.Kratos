/*
    DeleteDRMS.sql  (runs against the DRMS database)
    ----------------------------------------------------------------------------
    Purpose : Delete ALL DRMS data for one customer (every rental contract the
              customer has, plus customer level data).
              This is step 3. It must run AFTER DeleteGateway.sql and BEFORE
              ClearCommsData.sql.

    Parameters passed in by the app (do NOT declare them in this file):
        @CustomerId  UNIQUEIDENTIFIER

    Required output: the LAST result set must have the columns
        TableName, RowsAffected

    To test in SSMS, un-comment and fill in the line below.
*/
-- DECLARE @CustomerId UNIQUEIDENTIFIER = 'F2246EE9-46D9-8560-9E2A-46257ACD17B0';

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Results TABLE (TableName NVARCHAR(200), RowsAffected INT);

BEGIN TRY
    BEGIN TRANSACTION;

    ----------------------------------------------------
    -- 1. Customer list
    ----------------------------------------------------
    DECLARE @CustomerIdentifiers TABLE (CustomerIdentifier UNIQUEIDENTIFIER);
    INSERT INTO @CustomerIdentifiers (CustomerIdentifier) VALUES (@CustomerId);

    ----------------------------------------------------
    -- 2. RentalContracts of the customer
    ----------------------------------------------------
    DECLARE @RentalContracts TABLE (Id UNIQUEIDENTIFIER);
    INSERT INTO @RentalContracts (Id)
    SELECT RC.Id
    FROM RentalContracts RC
    JOIN @CustomerIdentifiers C ON RC.CustomerId = C.CustomerIdentifier;

    ----------------------------------------------------
    -- 3. IMEI Ids
    ----------------------------------------------------
    DECLARE @ImeiIds TABLE (Id UNIQUEIDENTIFIER);
    INSERT INTO @ImeiIds (Id)
    SELECT DISTINCT CD.ImeiSimDetailId
    FROM ContractDetail CD
    JOIN @RentalContracts RC ON CD.RentalContractId = RC.Id;

    ----------------------------------------------------
    -- 4. Child tables
    ----------------------------------------------------
    DELETE FROM OneXPaymentDetails    WHERE RentalContractsId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('OneXPaymentDetails', @@ROWCOUNT);

    DELETE FROM OneXSalesDetails      WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('OneXSalesDetails', @@ROWCOUNT);

    DELETE FROM Billings              WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('Billings', @@ROWCOUNT);

    DELETE FROM CancellationContracts WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('CancellationContracts', @@ROWCOUNT);

    DELETE FROM ContractStatuses      WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('ContractStatuses', @@ROWCOUNT);

    DELETE FROM Instalments           WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('Instalments', @@ROWCOUNT);

    DELETE FROM RentalDevices         WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('RentalDevices', @@ROWCOUNT);

    DELETE FROM ImeiSwapHistory       WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('ImeiSwapHistory', @@ROWCOUNT);

    DELETE FROM BenefitHistories      WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('BenefitHistories', @@ROWCOUNT);

    DELETE FROM ContractDetail        WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('ContractDetail', @@ROWCOUNT);

    DELETE FROM [dbo].[EOCDetails]            WHERE CustomerId IN (SELECT CustomerIdentifier FROM @CustomerIdentifiers);
    INSERT INTO @Results VALUES ('EOCDetails', @@ROWCOUNT);

    DELETE FROM [dbo].[DeviceStatusDetails]   WHERE CustomerId IN (SELECT CustomerIdentifier FROM @CustomerIdentifiers);
    INSERT INTO @Results VALUES ('DeviceStatusDetails', @@ROWCOUNT);

    DELETE FROM [dbo].[AgentDetails]          WHERE CustomerIdentifier IN (SELECT CustomerIdentifier FROM @CustomerIdentifiers);
    INSERT INTO @Results VALUES ('AgentDetails', @@ROWCOUNT);

    DELETE FROM [dbo].[SimMsisdnAudits]       WHERE CustomerId IN (SELECT CustomerIdentifier FROM @CustomerIdentifiers);
    INSERT INTO @Results VALUES ('SimMsisdnAudits', @@ROWCOUNT);

    DELETE FROM [dbo].[DeviceUnlockHistories] WHERE CustomerIdentifier IN (SELECT CustomerIdentifier FROM @CustomerIdentifiers);
    INSERT INTO @Results VALUES ('DeviceUnlockHistories', @@ROWCOUNT);

    DELETE FROM [dbo].[SimReplacements]       WHERE CustomerId IN (SELECT CustomerIdentifier FROM @CustomerIdentifiers);
    INSERT INTO @Results VALUES ('SimReplacements', @@ROWCOUNT);

    DELETE FROM [dbo].[OneXSRManualTransactions] WHERE RentalContractId IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('OneXSRManualTransactions', @@ROWCOUNT);

    ----------------------------------------------------
    -- 5. RentalContracts
    ----------------------------------------------------
    DELETE FROM RentalContracts WHERE Id IN (SELECT Id FROM @RentalContracts);
    INSERT INTO @Results VALUES ('RentalContracts', @@ROWCOUNT);

    ----------------------------------------------------
    -- 6. ImeiSimDetails
    ----------------------------------------------------
    DELETE ISD
    FROM ImeiSimDetails ISD
    JOIN @ImeiIds I ON ISD.Id = I.Id
    JOIN @CustomerIdentifiers C ON ISD.CustomerIdentifier = C.CustomerIdentifier;
    INSERT INTO @Results VALUES ('ImeiSimDetails', @@ROWCOUNT);

    ----------------------------------------------------
    -- 7. Carts
    ----------------------------------------------------
    DELETE C
    FROM Carts C
    JOIN @CustomerIdentifiers CI ON C.CustomerIdentifier = CI.CustomerIdentifier;
    INSERT INTO @Results VALUES ('Carts', @@ROWCOUNT);

    ----------------------------------------------------
    -- 8. Orphan Products
    --    NOTE: kept from the original script. This removes EVERY product that
    --    no cart uses, not only this customer's products.
    ----------------------------------------------------
    DELETE P
    FROM Products P
    WHERE NOT EXISTS (SELECT 1 FROM Carts C WHERE C.ProductId = P.Id);
    INSERT INTO @Results VALUES ('Products (orphans)', @@ROWCOUNT);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT TableName, RowsAffected FROM @Results;
