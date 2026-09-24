/* Gap-free, per-scope, per-year numbering: REQ-2026-000123, BK-2026-000451,
   receipt numbers per institution per year. Row is locked with UPDLOCK when incremented. */
CREATE TABLE core.NumberSeries (
    SeriesCode  VARCHAR(20)  NOT NULL,     -- REQUEST, BOOKING, RECEIPT
    ScopeKey    VARCHAR(20)  NOT NULL,     -- 'PLATFORM' or institution code
    SeriesYear  SMALLINT     NOT NULL,
    Prefix      VARCHAR(20)  NOT NULL,
    LastNumber  INT          NOT NULL DEFAULT 0
        CONSTRAINT CK_NumberSeries_LastNumber CHECK (LastNumber >= 0),
    PadLength   TINYINT      NOT NULL DEFAULT 6,
    PRIMARY KEY (SeriesCode, ScopeKey, SeriesYear)
);
GO
