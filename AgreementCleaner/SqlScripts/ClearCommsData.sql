/*
    ClearCommsData.sql  (runs against the Comms database)
    ----------------------------------------------------------------------------
    Purpose : Detach the customer's messages so they no longer show up.
              Messages are not deleted - their CustomerId is replaced with a
              new random GUID.
              This is step 4 and must run AFTER DeleteDRMS.sql, otherwise the
              DRMS job keeps creating new notifications.

    Parameters passed in by the app (do NOT declare them in this file):
        @CustomerId  UNIQUEIDENTIFIER

    Required output: the LAST result set must have the columns
        TableName, RowsAffected

    To test in SSMS, un-comment and fill in the line below.
*/
-- DECLARE @CustomerId UNIQUEIDENTIFIER = 'C527105A-13D3-A82F-B3E5-366A52636969';

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Results TABLE (TableName NVARCHAR(200), RowsAffected INT);
DECLARE @FakeCustomerId UNIQUEIDENTIFIER = NEWID();

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE Message SET CustomerId = @FakeCustomerId WHERE CustomerId = @CustomerId;
    INSERT INTO @Results VALUES ('Message (CustomerId cleared)', @@ROWCOUNT);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT TableName, RowsAffected FROM @Results;
