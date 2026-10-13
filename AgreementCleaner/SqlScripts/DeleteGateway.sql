/*
    DeleteGateway.sql  (runs against the Subscription Gateway database)
    ----------------------------------------------------------------------------
    Purpose : Delete the gateway orders for ONE rental contract.
              This is step 2 and must run BEFORE DeleteDRMS.sql.
              If the customer has several rental contracts, the app runs this
              script once for each RentalContractId.

    Parameters passed in by the app (do NOT declare them in this file):
        @RentalContractId  UNIQUEIDENTIFIER

    Required output: the LAST result set must have the columns
        TableName, RowsAffected
    The app adds these numbers up and shows them to the user.

    To test in SSMS, un-comment and fill in the line below.
*/
-- DECLARE @RentalContractId UNIQUEIDENTIFIER = 'E5F5177D-B333-4F5A-264B-08DEB720F17A';

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Results TABLE (TableName NVARCHAR(200), RowsAffected INT);

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM dbo.orders WHERE RentalContractId = @RentalContractId;
    INSERT INTO @Results VALUES ('dbo.orders', @@ROWCOUNT);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT TableName, RowsAffected FROM @Results;
