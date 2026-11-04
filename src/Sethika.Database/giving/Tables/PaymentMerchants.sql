/*  Each institution's own PayHere merchant (plan 09, M10): donors' money goes straight to the
    institution, never through Sethika. Built in D.3; published with D.1's schema.

    The merchant secret is stored encrypted (M21): AES-256-GCM with a key from configuration
    (SecretKeyId names it, so keys can be rotated), a random nonce per value, and the institution's id
    as associated data, so a ciphertext copied to another institution's row does not decrypt.
    SecretCipher is the ciphertext followed by the 16-byte tag. The API never returns it.
    RLS fn_Tenant: the institution alone; the notification reads it as the platform.               */
CREATE TABLE giving.PaymentMerchants (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    InstitutionId INT           NOT NULL REFERENCES org.Institutions(Id),
    Provider      VARCHAR(20)   NOT NULL CONSTRAINT DF_PayMerchant_Provider DEFAULT 'PayHere'
        CONSTRAINT CK_PayMerchant_Provider CHECK (Provider = 'PayHere'),
    MerchantId    VARCHAR(30)   NOT NULL,
    SecretKeyId   VARCHAR(20)   NOT NULL,
    SecretNonce   BINARY(12)    NOT NULL,
    SecretCipher  VARBINARY(256) NOT NULL,
    IsEnabled     BIT           NOT NULL CONSTRAINT DF_PayMerchant_Enabled DEFAULT 0,
    UpdatedBy     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_PayMerchant_UpdatedBy REFERENCES auth.Users(Id),
    UpdatedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_PayMerchant_UpdatedAt DEFAULT SYSUTCDATETIME(),
    RowVersion    ROWVERSION,
    CONSTRAINT UX_PayMerchant_Institution UNIQUE (InstitutionId)
);
GO
