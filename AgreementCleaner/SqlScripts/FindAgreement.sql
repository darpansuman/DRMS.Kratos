/*
    FindAgreement.sql  (runs against the DRMS database)
    ----------------------------------------------------------------------------
    Purpose : Find agreements that match the search values typed in the app.
              This is the "initial select" part of DeleteDRMS.sql, moved into
              its own file so the app can run it before anything is deleted.

    Parameters passed in by the app (do NOT declare them in this file):
        @AgreementId  NVARCHAR(50)      - RentalContracts.RentalId, or NULL
        @CustomerId   UNIQUEIDENTIFIER  - RentalContracts.CustomerId, or NULL
        @Imei         NVARCHAR(50)      - ImeiSimDetails.Imei1, or NULL
        @PhoneNumber  NVARCHAR(50)      - digits only, matched against ImeiSimDetails.MobileNo, or NULL

    A NULL parameter means "the user left this field empty", so it is ignored.
    All filled-in values must match (AND).

    Required output columns (names must stay the same, the app reads them):
        RentalContractId, AgreementId, CustomerId, Imei, PhoneNumber

    To test in SSMS, un-comment and fill in the block below.
*/
-- DECLARE @AgreementId NVARCHAR(50)     = '713';
-- DECLARE @CustomerId  UNIQUEIDENTIFIER = NULL;
-- DECLARE @Imei        NVARCHAR(50)     = NULL;
-- DECLARE @PhoneNumber NVARCHAR(50)     = NULL;

SET NOCOUNT ON;

SELECT DISTINCT
    RC.Id          AS RentalContractId,
    RC.RentalId    AS AgreementId,
    RC.CustomerId  AS CustomerId,
    ISD.Imei1      AS Imei,
    ISD.MobileNo   AS PhoneNumber
FROM RentalContracts RC
LEFT JOIN ContractDetail CD  ON CD.RentalContractId = RC.Id
LEFT JOIN ImeiSimDetails ISD ON ISD.Id = CD.ImeiSimDetailId
WHERE (@AgreementId IS NULL OR RC.RentalId   = @AgreementId)
  AND (@CustomerId  IS NULL OR RC.CustomerId = @CustomerId)
  AND (@Imei        IS NULL OR ISD.Imei1     = @Imei)
  AND (@PhoneNumber IS NULL OR ISD.MobileNo LIKE '%' + @PhoneNumber + '%')
ORDER BY RC.RentalId;
