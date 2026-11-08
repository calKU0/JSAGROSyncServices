-- Oferty Allegro replikowane do InPost Von Halsky.
--
-- InPost tworzy oferte asynchronicznie (POST zwraca tylko offerId i commandId), wiec stan
-- kazdej wyslanej oferty trzymamy u siebie: identyfikator po stronie InPost, ostatnia komende,
-- status, bledy walidacji i ostatnio wyslana cene i stan - po nich poznajemy, co trzeba zaktualizowac.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'InPostOffers' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.InPostOffers
    (
        Id INT IDENTITY(1,1) NOT NULL,
        AllegroOfferId NVARCHAR(128) NOT NULL,
        Account INT NOT NULL,
        ExternalId NVARCHAR(255) NOT NULL,
        InPostOfferId UNIQUEIDENTIFIER NULL,
        LastCommandId UNIQUEIDENTIFIER NULL,
        Status NVARCHAR(30) NULL,
        Price DECIMAL(18,2) NULL,
        Stock INT NULL,
        ValidationErrors NVARCHAR(MAX) NULL,
        LastError NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_InPostOffers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_InPostOffers_UpdatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_InPostOffers PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_InPostOffers_AllegroOfferId UNIQUE (AllegroOfferId)
    );

    CREATE NONCLUSTERED INDEX IX_InPostOffers_ExternalId ON dbo.InPostOffers (Account, ExternalId);
END
GO

-- Kandydaci do InPost: aktywne oferty konta z wybranych cennikow dostawy razem z produktem
-- dostawcy i wszystkim, co da sie przeniesc (opis, atrybuty, parametry, specyfikacje,
-- kategorie dostawcy). Filtr kategorii (tylko czesci rolnicze) robi serwis - zna mapowanie
-- kategorii Allegro na InPost.
--
-- @DeliveryNames i @OfferIds to tablice JSON. @OfferIds = NULL oznacza wszystkie oferty.
CREATE OR ALTER PROCEDURE dbo.InPostOffers_GetCandidates
    @Account INT,
    @IntegrationCompany INT,
    @DeliveryNames NVARCHAR(MAX),
    @OfferIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Offers
    (
        OfferId NVARCHAR(128) NOT NULL PRIMARY KEY,
        ProductId INT NOT NULL,
        CategoryId INT NOT NULL
    );

    INSERT INTO #Offers (OfferId, ProductId, CategoryId)
    SELECT o.Id, p.Id, o.CategoryId
    FROM dbo.AllegroOffers o
    JOIN dbo.RolmarProducts p
        ON p.Code = o.ExternalId
       AND p.IntegrationCompany = @IntegrationCompany
    WHERE o.Account = @Account
      AND o.Status = 'ACTIVE'
      AND o.DeliveryName IN (SELECT [value] FROM OPENJSON(@DeliveryNames))
      AND (@OfferIds IS NULL OR o.Id IN (SELECT [value] FROM OPENJSON(@OfferIds)));

    -- 1. Oferta, produkt i stan po stronie InPost.
    SELECT
        o.Id, o.ExternalId, o.Name, o.CategoryId, o.Price, o.Stock, o.Status, o.DeliveryName,
        o.ProductId AS AllegroProductId, o.Images, o.Weight, o.HandlingTime,
        o.ResponsibleProducer, o.ResponsiblePerson, o.VisitsCount,
        p.Id AS ProductId, p.Code AS ProductCode, p.Name AS ProductName, p.Description AS ProductDescription,
        p.Ean, p.Weight AS ProductWeight, p.Fits, p.Substitutes, p.InStock, p.Unit,
        p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DeliveryType, p.ArchivedAt, p.Package,
        i.InPostOfferId, i.LastCommandId, i.Status AS InPostStatus, i.Price AS InPostPrice, i.Stock AS InPostStock
    FROM #Offers t
    JOIN dbo.AllegroOffers o ON o.Id = t.OfferId
    JOIN dbo.RolmarProducts p ON p.Id = t.ProductId
    LEFT JOIN dbo.InPostOffers i ON i.AllegroOfferId = o.Id;

    -- 2. Opis oferty Allegro, sekcja po sekcji.
    SELECT d.OfferId, d.SectionId, d.Type, d.Content
    FROM dbo.AllegroOfferDescriptions d
    JOIN #Offers t ON t.OfferId = d.OfferId
    ORDER BY d.OfferId, d.SectionId, d.Id;

    -- 3. Parametry oferty Allegro z nazwami ze slownika kategorii.
    SELECT a.OfferId, cp.Name, a.Type, a.ValuesJson
    FROM dbo.AllegroOfferAttributes a
    JOIN #Offers t ON t.OfferId = a.OfferId
    JOIN dbo.CategoryParameters cp
        ON cp.ParameterId = TRY_CAST(a.AttributeId AS INT)
       AND cp.CategoryId = t.CategoryId;

    -- 4. Parametry produktu dostawcy.
    SELECT pp.ProductId, cp.Name, pp.Value
    FROM dbo.RolmarProductParameters pp
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = pp.ProductId
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    WHERE NULLIF(LTRIM(RTRIM(pp.Value)), '') IS NOT NULL;

    -- 5. Specyfikacje produktu (wymiary, material, gwint...).
    SELECT s.ProductId, s.Name, s.Value, s.UnitName
    FROM dbo.ProductSpecifications s
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = s.ProductId;

    -- 6. Sciezki kategorii dostawcy - z nich (obok nazwy) odczytujemy marke.
    SELECT pc.ProductId, sc.SourceKey
    FROM dbo.ProductCategories pc
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = pc.ProductId
    JOIN dbo.SupplierCategories sc ON sc.Id = pc.CategoryId;
END
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
        ValidationErrors = @ValidationErrors,
        LastError = @LastError,
        UpdatedAt = SYSUTCDATETIME()
    WHERE AllegroOfferId = @AllegroOfferId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.InPostOffers
            (AllegroOfferId, Account, ExternalId, InPostOfferId, LastCommandId, Status, Price, Stock, ValidationErrors, LastError)
        VALUES
            (@AllegroOfferId, @Account, @ExternalId, @InPostOfferId, @LastCommandId, @Status, @Price, @Stock, @ValidationErrors, @LastError);
    END
END
GO

-- Oferty juz wyslane do InPost - takze te, ktore wypadly z listy kandydatow
-- (koniec oferty na Allegro, brak stanu, zmiana cennika) i trzeba je zamknac.
CREATE OR ALTER PROCEDURE dbo.InPostOffers_GetTracked
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT AllegroOfferId, ExternalId, InPostOfferId, LastCommandId, Status, Price, Stock
    FROM dbo.InPostOffers
    WHERE Account = @Account;
END
GO
