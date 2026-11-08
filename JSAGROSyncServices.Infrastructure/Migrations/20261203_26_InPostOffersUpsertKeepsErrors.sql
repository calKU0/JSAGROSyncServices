-- Aktualizacja pojedynczych pol (np. samej ceny) nie moze kasowac zapisanych bledow walidacji
-- ani ostatniego bledu. NULL = "bez zmian", pusty tekst = "wyczysc".

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
    @LastError NVARCHAR(MAX) = NULL
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
        UpdatedAt = SYSUTCDATETIME()
    WHERE AllegroOfferId = @AllegroOfferId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.InPostOffers
            (AllegroOfferId, Account, ExternalId, InPostOfferId, LastCommandId, Status, Price, Stock, ValidationErrors, LastError)
        VALUES
            (@AllegroOfferId, @Account, @ExternalId, @InPostOfferId, @LastCommandId, @Status, @Price, @Stock,
             NULLIF(@ValidationErrors, ''), NULLIF(@LastError, ''));
    END
END
GO
