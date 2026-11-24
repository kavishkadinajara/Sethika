/*  An online gift through PayHere (plan 09, M8-M10), from the order to PayHere's answer. Built in D.3;
    the table is published with D.1's schema so Phase D has one schema change.

    A payment that has not been confirmed is not a donation: it gets no receipt number. Only PayHere's
    signed server-to-server notification turns an order Paid, and with it records the donation in the
    same transaction (M9). OrderId is what PayHere is given and sends back; MerchantId is the
    institution's own merchant the order was made for (M10), so a notification signed with another
    institution's secret is refused. RLS: the institution and the donor who paid read it (fn_TenantOrOwner);
    the order and the notification both run as the platform, so no donor's session writes it (M34).  */
CREATE TABLE giving.OnlinePayments (
    Id                BIGINT        IDENTITY(1,1) PRIMARY KEY,
    OrderId           UNIQUEIDENTIFIER NOT NULL,        -- v7, set in C#; PayHere's order_id
    InstitutionId     INT           NOT NULL REFERENCES org.Institutions(Id),
    DonorUserId       UNIQUEIDENTIFIER NOT NULL REFERENCES auth.Users(Id),
    MerchantId        VARCHAR(30)   NOT NULL,
    Amount            DECIMAL(12,2) NOT NULL
        CONSTRAINT CK_OnlinePay_Amount CHECK (Amount >= 100 AND Amount <= 1000000),
    CurrencyCode      CHAR(3)       NOT NULL CONSTRAINT DF_OnlinePay_Currency DEFAULT 'LKR',
    NeedId            INT           NULL,
    Status            VARCHAR(20)   NOT NULL CONSTRAINT DF_OnlinePay_Status DEFAULT 'Pending'
        CONSTRAINT CK_OnlinePay_Status CHECK (Status = 'Pending' OR Status = 'Paid' OR Status = 'Failed' OR Status = 'Cancelled'),
    PayHerePaymentId  VARCHAR(30)   NULL,
    PayHereStatusCode SMALLINT      NULL,
    DonationId        BIGINT        NULL,
    CreatedAt         DATETIME2(3)  NOT NULL CONSTRAINT DF_OnlinePay_CreatedAt DEFAULT SYSUTCDATETIME(),
    NotifiedAt        DATETIME2(3)  NULL,
    RowVersion        ROWVERSION,
    CONSTRAINT UQ_OnlinePay_Order UNIQUE (OrderId),
    CONSTRAINT FK_OnlinePay_NeedTenant     FOREIGN KEY (NeedId, InstitutionId)     REFERENCES giving.Needs(Id, InstitutionId),
    CONSTRAINT FK_OnlinePay_DonationTenant FOREIGN KEY (DonationId, InstitutionId) REFERENCES giving.Donations(Id, InstitutionId),
    -- Paid means the donation it made; nothing else has one.
    CONSTRAINT CK_OnlinePay_Paid CHECK ((Status = 'Paid' AND DonationId IS NOT NULL) OR (Status <> 'Paid' AND DonationId IS NULL))
);
GO

-- A paid order makes one donation, never two (M9's idempotence, backed by the database).
CREATE UNIQUE INDEX UX_OnlinePay_Donation ON giving.OnlinePayments(DonationId) WHERE DonationId IS NOT NULL;
GO

CREATE INDEX IX_OnlinePay_Donor ON giving.OnlinePayments(DonorUserId, CreatedAt DESC);
GO
