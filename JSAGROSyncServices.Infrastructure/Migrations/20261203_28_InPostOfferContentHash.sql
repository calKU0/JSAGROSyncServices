-- Skrot tresci oferty InPost (nazwa, opis, atrybuty, zdjecia, wymiary, GPSR, zwroty...) bez ceny
-- i stanu. Gdy dane po naszej stronie sie zmienia, skrot sie rozjedzie i serwis wysle PATCH tresci -
-- cena i stan maja osobne, zbiorcze endpointy.

IF COL_LENGTH('dbo.InPostOffers', 'ContentHash') IS NULL
    ALTER TABLE dbo.InPostOffers ADD ContentHash NVARCHAR(64) NULL;
GO

CREATE OR ALTER PROCEDURE dbo.InPostOffers_Upsert
    @AllegroOfferId NVARCHAR(128),
    @Account INT,
    @ExternalId NVARCHAR(255),
    @InPostOfferId UNIQUEIDENTIFIER = NULL,
    @LastCommandId UNIQUEIDENTIFIER = NULL,
    @Status NVARCHAR(30) = NULL,
    @Price DECIMAL(18,2) = NULL,
    @Stock INT = NULL,
    @ValidationErrors NVARCHAR(MAX) = NULL,
    @LastError NVARCHAR(MAX) = NULL,
    @ContentHash NVARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.InPostOffers
    SET ExternalId = @ExternalId,
        InPostOfferId = COALESCE(@InPostOfferId, InPostOfferId),
        LastCommandId = COALESCE(@LastCommandId, LastCommandId),
        Status = COALESCE(@Status, Status),
        Price = COALESCE(@Price, Price),
        Stock = COALESCE(@Stock, Stock),
        ValidationErrors = CASE WHEN @ValidationErrors IS NULL THEN ValidationErrors ELSE NULLIF(@ValidationErrors, '') END,
        LastError = CASE WHEN @LastError IS NULL THEN LastError ELSE NULLIF(@LastError, '') END,
        ContentHash = COALESCE(@ContentHash, ContentHash),
        UpdatedAt = SYSUTCDATETIME()
    WHERE AllegroOfferId = @AllegroOfferId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.InPostOffers
            (AllegroOfferId, Account, ExternalId, InPostOfferId, LastCommandId, Status, Price, Stock, ValidationErrors, LastError, ContentHash)
        VALUES
            (@AllegroOfferId, @Account, @ExternalId, @InPostOfferId, @LastCommandId, @Status, @Price, @Stock,
             NULLIF(@ValidationErrors, ''), NULLIF(@LastError, ''), @ContentHash);
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.InPostOffers_GetTracked
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT AllegroOfferId, ExternalId, InPostOfferId, LastCommandId, Status, Price, Stock, ContentHash
    FROM dbo.InPostOffers
    WHERE Account = @Account;
END
GO
