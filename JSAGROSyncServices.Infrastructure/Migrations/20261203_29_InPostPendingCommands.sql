-- Zbiorcze zmiany ceny i stanu w InPost sa asynchroniczne: odpowiedz zwraca komende (zwykle PENDING),
-- a o wyniku decyduje dopiero InPost. Wyslana wartosc trzymamy jako "w drodze" razem z id komendy
-- i zatwierdzamy ja dopiero po SUCCESS. Przy FAILURE zostaje stara wartosc, wiec roznica wraca
-- w nastepnym cyklu i zmiana idzie ponownie.

IF COL_LENGTH('dbo.InPostOffers', 'PendingPrice') IS NULL
    ALTER TABLE dbo.InPostOffers ADD PendingPrice DECIMAL(18,2) NULL, PriceCommandId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.InPostOffers', 'PendingStock') IS NULL
    ALTER TABLE dbo.InPostOffers ADD PendingStock INT NULL, StockCommandId UNIQUEIDENTIFIER NULL;
GO

-- @ClearPending = 1: zmiana poszla innym zapytaniem (PATCH calej oferty), wiec oczekujace komendy
-- ceny i stanu nie moga juz nadpisac nowszej wartosci.
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
    @ContentHash NVARCHAR(64) = NULL,
    @ClearPending BIT = 0
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
        PendingPrice = CASE WHEN @ClearPending = 1 THEN NULL ELSE PendingPrice END,
        PriceCommandId = CASE WHEN @ClearPending = 1 THEN NULL ELSE PriceCommandId END,
        PendingStock = CASE WHEN @ClearPending = 1 THEN NULL ELSE PendingStock END,
        StockCommandId = CASE WHEN @ClearPending = 1 THEN NULL ELSE StockCommandId END,
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

-- @Kind: 'price' albo 'stock'.
CREATE OR ALTER PROCEDURE dbo.InPostOffers_SetPending
    @AllegroOfferId NVARCHAR(128),
    @Kind NVARCHAR(10),
    @Value DECIMAL(18,2),
    @CommandId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF @Kind = 'price'
        UPDATE dbo.InPostOffers
        SET PendingPrice = @Value, PriceCommandId = @CommandId, UpdatedAt = SYSUTCDATETIME()
        WHERE AllegroOfferId = @AllegroOfferId;
    ELSE
        UPDATE dbo.InPostOffers
        SET PendingStock = CAST(@Value AS INT), StockCommandId = @CommandId, UpdatedAt = SYSUTCDATETIME()
        WHERE AllegroOfferId = @AllegroOfferId;
END
GO

-- Wynik komendy: SUCCESS zatwierdza wartosc "w drodze", FAILURE zostawia stara i zapisuje powod.
-- Dotyczy tylko komendy, ktora nadal jest ostatnia - nowsza zmiana ma pierwszenstwo.
CREATE OR ALTER PROCEDURE dbo.InPostOffers_ResolvePending
    @AllegroOfferId NVARCHAR(128),
    @Kind NVARCHAR(10),
    @CommandId UNIQUEIDENTIFIER,
    @Success BIT,
    @Error NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Kind = 'price'
        UPDATE dbo.InPostOffers
        SET Price = CASE WHEN @Success = 1 THEN PendingPrice ELSE Price END,
            LastError = CASE WHEN @Success = 1 THEN LastError ELSE @Error END,
            PendingPrice = NULL,
            PriceCommandId = NULL,
            UpdatedAt = SYSUTCDATETIME()
        WHERE AllegroOfferId = @AllegroOfferId AND PriceCommandId = @CommandId;
    ELSE
        UPDATE dbo.InPostOffers
        SET Stock = CASE WHEN @Success = 1 THEN PendingStock ELSE Stock END,
            LastError = CASE WHEN @Success = 1 THEN LastError ELSE @Error END,
            PendingStock = NULL,
            StockCommandId = NULL,
            UpdatedAt = SYSUTCDATETIME()
        WHERE AllegroOfferId = @AllegroOfferId AND StockCommandId = @CommandId;
END
GO

-- Przepiecie oferty InPost na inna oferte Allegro tego samego produktu (stara sie zakonczyla).
-- Ewentualny pusty wpis nowej oferty (np. po nieudanym zakladaniu) usuwamy, zeby nie zalozyc duplikatu SKU.
CREATE OR ALTER PROCEDURE dbo.InPostOffers_Reassign
    @FromAllegroOfferId NVARCHAR(128),
    @ToAllegroOfferId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.InPostOffers
    WHERE AllegroOfferId = @ToAllegroOfferId AND InPostOfferId IS NULL;

    UPDATE dbo.InPostOffers
    SET AllegroOfferId = @ToAllegroOfferId, UpdatedAt = SYSUTCDATETIME()
    WHERE AllegroOfferId = @FromAllegroOfferId
      AND NOT EXISTS (SELECT 1 FROM dbo.InPostOffers WHERE AllegroOfferId = @ToAllegroOfferId);
END
GO

CREATE OR ALTER PROCEDURE dbo.InPostOffers_GetTracked
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT AllegroOfferId, ExternalId, InPostOfferId, LastCommandId, Status, Price, Stock, ContentHash,
           PendingPrice, PriceCommandId, PendingStock, StockCommandId
    FROM dbo.InPostOffers
    WHERE Account = @Account;
END
GO
