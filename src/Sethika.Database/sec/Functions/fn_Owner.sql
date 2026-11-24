/* A row that belongs to one person and no institution (plan 07, H13): that person, and the platform
   for its jobs. A memorial plan names someone's dead, so no institution ever sees it. */
CREATE FUNCTION sec.fn_Owner(@UserId UNIQUEIDENTIFIER)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS Allowed
WHERE CAST(SESSION_CONTEXT(N'IsPlatform') AS BIT) = 1
   OR (@UserId IS NOT NULL AND @UserId = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER));
GO
