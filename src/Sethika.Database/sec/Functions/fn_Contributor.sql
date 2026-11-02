/* A co-sponsor's row (plan 07, H18): the institution whose booking it is, the person who joined or
   declined it, the donor who invited them, and the platform for its jobs. Nobody else, so one
   donor's invitations never show another's names. */
CREATE FUNCTION sec.fn_Contributor(@InstitutionId INT, @UserId UNIQUEIDENTIFIER, @InvitedBy UNIQUEIDENTIFIER)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS Allowed
WHERE CAST(SESSION_CONTEXT(N'IsPlatform') AS BIT) = 1
   OR @InstitutionId = CAST(SESSION_CONTEXT(N'InstitutionId') AS INT)
   OR (@UserId IS NOT NULL AND @UserId = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER))
   OR @InvitedBy = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER);
GO
