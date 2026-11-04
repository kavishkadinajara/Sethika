/*  Recorded money cannot be changed (plan 09, M7): the project's first trigger.

    A donation is never deleted and never edited. The only changes the database accepts are:
    - the void (M6), once: IsVoided 0 -> 1 with who, why and when (CK_Donations_Void says all three,
      CK_Donations_VoidFourEyes says not the recorder). A voided row is never un-voided or re-voided.
    - DonorUserId of a gift recorded against a donor book row (M3, M22): the account that may read
      the receipt follows the row's link, at sign-in (K36) and when staff change the phone (K37). Who
      gave stays the row and the name snapshot; only who may read it moves. A walk-in's or an online
      donor's account never changes.
    - RowVersion, which SQL Server changes on every update.

    The EF mapping alone would only say what our own C# does; a raw UPDATE from a bug, a script or a
    support session would pass it. Strings are compared under a binary collation, or a case-only change
    to a name would compare equal under the database's case-insensitive one and slip through.

    In Sethika_Test the fixture's reset disables this trigger around its DELETE (RlsTestData.sql).  */
CREATE TRIGGER giving.trg_Donations_Immutable
ON giving.Donations
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        IF EXISTS (SELECT 1 FROM deleted)
            THROW 51100, N'giving.Donations: a donation is never deleted. Void it instead (plan 09, M6).', 1;
        RETURN;
    END;

    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        JOIN deleted AS d ON d.Id = i.Id
        WHERE EXISTS (
            SELECT d.ReceiptNo COLLATE Latin1_General_BIN2, d.InstitutionId, d.InstitutionDonorId,
                   d.DonorName COLLATE Latin1_General_BIN2, d.DonorPhone COLLATE Latin1_General_BIN2, d.IsAnonymous,
                   d.Kind COLLATE Latin1_General_BIN2, d.Amount, d.CurrencyCode COLLATE Latin1_General_BIN2,
                   d.PaymentMethodLookupId, d.ItemDescription COLLATE Latin1_General_BIN2, d.EstimatedValue,
                   d.NeedId, d.PledgeId, d.BookingId, d.ReceiptFileId, d.DonatedAt, d.RecordedBy, d.RecordedAt,
                   d.PublicId, d.Quantity, d.Note COLLATE Latin1_General_BIN2,
                   CASE WHEN d.InstitutionDonorId IS NULL THEN d.DonorUserId END,
                   CASE WHEN d.IsVoided = 1 THEN 1 END,
                   CASE WHEN d.IsVoided = 1 THEN d.VoidedBy END,
                   CASE WHEN d.IsVoided = 1 THEN d.VoidReason END COLLATE Latin1_General_BIN2,
                   CASE WHEN d.IsVoided = 1 THEN d.VoidedAt END
            EXCEPT
            SELECT i.ReceiptNo COLLATE Latin1_General_BIN2, i.InstitutionId, i.InstitutionDonorId,
                   i.DonorName COLLATE Latin1_General_BIN2, i.DonorPhone COLLATE Latin1_General_BIN2, i.IsAnonymous,
                   i.Kind COLLATE Latin1_General_BIN2, i.Amount, i.CurrencyCode COLLATE Latin1_General_BIN2,
                   i.PaymentMethodLookupId, i.ItemDescription COLLATE Latin1_General_BIN2, i.EstimatedValue,
                   i.NeedId, i.PledgeId, i.BookingId, i.ReceiptFileId, i.DonatedAt, i.RecordedBy, i.RecordedAt,
                   i.PublicId, i.Quantity, i.Note COLLATE Latin1_General_BIN2,
                   CASE WHEN d.InstitutionDonorId IS NULL THEN i.DonorUserId END,
                   CASE WHEN d.IsVoided = 1 THEN CAST(i.IsVoided AS INT) END,
                   CASE WHEN d.IsVoided = 1 THEN i.VoidedBy END,
                   CASE WHEN d.IsVoided = 1 THEN i.VoidReason END COLLATE Latin1_General_BIN2,
                   CASE WHEN d.IsVoided = 1 THEN i.VoidedAt END))
        THROW 51101, N'giving.Donations: a recorded donation is never changed. Void it and record it again (plan 09, M7).', 1;
END;
GO
