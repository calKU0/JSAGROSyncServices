-- Klasa wysylki ("cubature") z API Rolmar: INPOST_A/B/C (gabaryt paczkomatu), "paczka" (kurier),
-- "polpaleta", "paleta", "dluzyca" (transport paletowy). Rolmar podaje wymiary tylko w czesci
-- specyfikacji, a klase ma prawie kazdy produkt - to zapasowe zrodlo gabarytu dla doboru cennika
-- Allegro i dla InPost. Zapisuje ja glowny upsert produktow (nowa kolumna typu tabelarycznego).
--
-- Przy okazji: InStock w typie tabelarycznym moze byc NULL = "dostawca nie podal stanu w tych danych".
-- Lista produktow Rolmara nie ma stanow (przychodza osobnym zapytaniem), a zapis zerowal je wszystkim
-- produktom do czasu kroku stanow - w tym oknie oferty wygladaly na niedostepne. Teraz NULL zostawia
-- stan w bazie bez zmian (nowy produkt dostaje 0).

IF COL_LENGTH('dbo.RolmarProducts', 'ShippingClass') IS NULL
    ALTER TABLE dbo.RolmarProducts ADD ShippingClass NVARCHAR(50) NULL;
GO

-- ============================================================ typ tabelaryczny upserta

-- Typu tabelarycznego nie da sie zmienic w miejscu - procedura, ktora go uzywa, musi zniknac
-- na czas podmiany. Odtwarzamy ja w niezmienionej postaci, poza nowa kolumna ShippingClass.
DROP PROCEDURE IF EXISTS dbo.RolmarProducts_UpsertBatch;
GO

DROP TYPE IF EXISTS dbo.RolmarProductUpsertType;
GO

CREATE TYPE dbo.RolmarProductUpsertType AS TABLE
(
    Code NVARCHAR(255) NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    SupplierLogo NVARCHAR(255) NULL,
    SupplierName NVARCHAR(255) NULL,
    Description NVARCHAR(MAX) NULL,
    CustomerCode NVARCHAR(255) NULL,
    Ean NVARCHAR(50) NULL,
    InStock FLOAT NULL,
    Weight FLOAT NOT NULL,
    Fits NVARCHAR(MAX) NULL,
    Unit NVARCHAR(50) NULL,
    CurrencyPrice NVARCHAR(50) NULL,
    Substitutes NVARCHAR(MAX) NULL,
    IntegrationCompany INT NOT NULL,
    IntegrationId INT NULL,
    DeliveryType INT NULL,
    PriceNet DECIMAL(18,2) NOT NULL,
    PriceGross DECIMAL(18,2) NOT NULL,
    Package DECIMAL(18,2) NOT NULL,
    BlockedReturn BIT NOT NULL,
    ShippingClass NVARCHAR(50) NULL
);
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UpsertBatch
    @Products dbo.RolmarProductUpsertType READONLY,
    @MaxPriceDropPercent DECIMAL(5, 2) = 0
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.RolmarProducts AS target
    USING
    (
        SELECT
            p.Code,
            LEFT(p.Name,
                CASE
                    WHEN LEN(p.Name) <= 75 THEN LEN(p.Name)
                    ELSE 75 - CHARINDEX(' ', REVERSE(LEFT(p.Name, 75))) + 1
                END) AS Name,
            p.SupplierLogo,
            p.SupplierName,
            p.Description,
            p.CustomerCode,
            p.Ean,
            p.InStock,
            p.Weight,
            NULLIF(p.Fits, '') AS Fits,
            p.Unit,
            p.CurrencyPrice,
            NULLIF(p.Substitutes, '') AS Substitutes,
            p.IntegrationCompany,
            p.IntegrationId,
            p.DeliveryType,
            p.PriceNet,
            p.PriceGross,
            p.Package,
            p.BlockedReturn,
            NULLIF(LTRIM(RTRIM(p.ShippingClass)), '') AS ShippingClass
        FROM @Products p
    ) AS source
    ON target.Code = source.Code AND target.IntegrationCompany = source.IntegrationCompany
    WHEN MATCHED AND
    (
        ISNULL(target.Name, '') <> ISNULL(source.Name, '') OR
        ISNULL(target.SupplierLogo, '') <> ISNULL(source.SupplierLogo, '') OR
        ISNULL(target.SupplierName, '') <> ISNULL(source.SupplierName, '') OR
        ISNULL(target.Description, '') <> ISNULL(source.Description, '') OR
        ISNULL(target.CustomerCode, '') <> ISNULL(source.CustomerCode, '') OR
        ISNULL(target.Ean, '') <> ISNULL(source.Ean, '') OR
        (source.InStock IS NOT NULL AND ISNULL(target.InStock, 0) <> source.InStock) OR
        ISNULL(target.Weight, 0) <> ISNULL(source.Weight, 0) OR
        ISNULL(target.Fits, '') <> ISNULL(source.Fits, '') OR
        ISNULL(target.Unit, '') <> ISNULL(source.Unit, '') OR
        ISNULL(target.CurrencyPrice, '') <> ISNULL(source.CurrencyPrice, '') OR
        ISNULL(target.Substitutes, '') <> ISNULL(source.Substitutes, '') OR
        ISNULL(target.IntegrationId, 0) <> ISNULL(source.IntegrationId, 0) OR
        ISNULL(target.DeliveryType, 0) <> ISNULL(source.DeliveryType, 0) OR
        ISNULL(target.PriceNet, 0) <> ISNULL(source.PriceNet, 0) OR
        ISNULL(target.PriceGross, 0) <> ISNULL(source.PriceGross, 0) OR
        ISNULL(target.Package, 0) <> ISNULL(source.Package, 0) OR
        target.BlockedReturn <> source.BlockedReturn OR
        ISNULL(target.ShippingClass, '') <> ISNULL(source.ShippingClass, '') OR
        target.AcceptedPriceGross IS NULL
    ) THEN
        UPDATE SET
            Name = source.Name,
            SupplierLogo = source.SupplierLogo,
            SupplierName = source.SupplierName,
            Description = source.Description,
            CustomerCode = source.CustomerCode,
            Ean = source.Ean,
            InStock = COALESCE(source.InStock, target.InStock),
            Weight = source.Weight,
            Fits = source.Fits,
            Unit = source.Unit,
            CurrencyPrice = source.CurrencyPrice,
            Substitutes = source.Substitutes,
            IntegrationId = source.IntegrationId,
            DeliveryType = source.DeliveryType,
            PriceNet = source.PriceNet,
            PriceGross = source.PriceGross,
            Package = source.Package,
            BlockedReturn = source.BlockedReturn,
            ShippingClass = source.ShippingClass,
            AcceptedPriceGross = CASE
                WHEN @MaxPriceDropPercent <= 0
                  OR target.AcceptedPriceGross IS NULL
                  OR target.AcceptedPriceGross <= 0
                  OR source.PriceGross >= target.AcceptedPriceGross * (1 - @MaxPriceDropPercent / 100.0)
                THEN source.PriceGross
                ELSE target.AcceptedPriceGross
            END,
            PriceDropDetectedAt = CASE
                WHEN @MaxPriceDropPercent <= 0
                  OR target.AcceptedPriceGross IS NULL
                  OR target.AcceptedPriceGross <= 0
                  OR source.PriceGross >= target.AcceptedPriceGross * (1 - @MaxPriceDropPercent / 100.0)
                THEN NULL
                ELSE ISNULL(target.PriceDropDetectedAt, SYSUTCDATETIME())
            END,
            UpdatedDate = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            Code, Name, SupplierLogo, SupplierName, Description, CustomerCode, Ean,
            InStock, Weight, Fits, Unit, CurrencyPrice, Substitutes,
            IntegrationCompany, IntegrationId, DeliveryType,
            PriceNet, PriceGross, AcceptedPriceGross, Package, BlockedReturn, ShippingClass, CreatedDate, UpdatedDate
        )
        VALUES
        (
            source.Code, source.Name, source.SupplierLogo, source.SupplierName, source.Description, source.CustomerCode, source.Ean,
            ISNULL(source.InStock, 0), source.Weight, source.Fits, source.Unit, source.CurrencyPrice, source.Substitutes,
            source.IntegrationCompany, source.IntegrationId, source.DeliveryType,
            source.PriceNet, source.PriceGross, source.PriceGross, source.Package, source.BlockedReturn, source.ShippingClass, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
END
GO

-- ============================================================ odczyty z klasa wysylki

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetOffersToUpdate]
    @DeliveryNames NVARCHAR(MAX),
    @IntegrationCompany INT,
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #OffersWithProducts
    (
        OfferId NVARCHAR(255) NOT NULL,
        OfferProductId NVARCHAR(500) NULL,
        ExternalId NVARCHAR(255) NULL,
        OfferName NVARCHAR(255) NOT NULL,
        CategoryId INT NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        StartingAt DATETIME2 NOT NULL,
        DeliveryName NVARCHAR(255) NULL,
        ProductId INT NOT NULL,
        AllegroId NVARCHAR(255) NULL,
        AllegroName NVARCHAR(255) NULL,
        Code NVARCHAR(255) NOT NULL,
        CustomerCode NVARCHAR(255) NULL,
        ProductName NVARCHAR(255) NOT NULL,
        Description NVARCHAR(MAX) NULL,
        Ean NVARCHAR(50) NULL,
        Weight FLOAT NOT NULL,
        Fits NVARCHAR(MAX) NULL,
        SupplierName NVARCHAR(255) NULL,
        Substitutes NVARCHAR(MAX) NULL,
        InStock FLOAT NOT NULL,
        Unit NVARCHAR(50) NULL,
        CurrencyPrice NVARCHAR(50) NULL,
        PriceNet DECIMAL(18, 2) NOT NULL,
        PriceGross DECIMAL(18, 2) NOT NULL,
        AcceptedPriceGross DECIMAL(18, 2) NULL,
        PriceDropDetectedAt DATETIME2 NULL,
        DefaultAllegroCategory INT NOT NULL,
        Package DECIMAL(18, 2) NOT NULL,
        DeliveryType INT NULL,
        ShippingClass NVARCHAR(50) NULL,
        CreatedDate DATETIME2 NOT NULL,
        UpdatedDate DATETIME2 NOT NULL,
        OfferPrice DECIMAL(18, 2) NOT NULL
    );

    INSERT INTO #OffersWithProducts
    (
        OfferId, OfferProductId, ExternalId, OfferName, CategoryId, Status, StartingAt, DeliveryName,
        ProductId, AllegroId, AllegroName, Code, CustomerCode, ProductName, Description, Ean, Weight, Fits, SupplierName,
        Substitutes, InStock, Unit, CurrencyPrice, PriceNet, PriceGross, AcceptedPriceGross,
        PriceDropDetectedAt, DefaultAllegroCategory, Package, DeliveryType, ShippingClass, CreatedDate, UpdatedDate, OfferPrice
    )
    SELECT
        ao.Id, ao.ProductId, ao.ExternalId, ao.Name, ao.CategoryId, ao.Status, ao.StartingAt, ao.DeliveryName,
        p.Id, p.AllegroId, p.AllegroName, p.Code, p.CustomerCode, p.Name, p.Description,
        p.Ean, p.Weight, p.Fits, p.SupplierName, p.Substitutes, p.InStock, p.Unit,
        p.CurrencyPrice, p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DefaultAllegroCategory, p.Package, p.DeliveryType, p.ShippingClass,
        p.CreatedDate, p.UpdatedDate, ao.Price
    FROM dbo.AllegroOffers ao
    INNER JOIN dbo.RolmarProducts p ON p.Code = ao.ExternalId AND p.IntegrationCompany = @IntegrationCompany
    WHERE ao.Status IN ('ACTIVE', 'ENDED', 'INACTIVE') AND ao.Account = @Account
        AND ao.DeliveryName IN (SELECT value FROM STRING_SPLIT(@DeliveryNames, ','))
        AND p.PriceNet > 0
        AND p.BlockedReturn = 0;

    SELECT
        OfferId AS Id,
        OfferProductId AS ProductId,
        ExternalId,
        OfferName AS Name,
        CategoryId,
        Status,
        StartingAt,
        DeliveryName,
        OfferPrice as Price,
        ProductId as Id,
        AllegroId,
        AllegroName,
        Code,
        CustomerCode,
        ProductName AS Name,
        Description,
        Ean,
        Weight,
        Fits,
        SupplierName,
        Substitutes,
        InStock,
        Unit,
        CurrencyPrice,
        PriceNet,
        PriceGross,
        AcceptedPriceGross,
        PriceDropDetectedAt,
        DefaultAllegroCategory,
        Package,
        DeliveryType,
        ShippingClass,
        CreatedDate,
        UpdatedDate
    FROM #OffersWithProducts;

    SELECT ai.*
    FROM dbo.AllegroImages ai
    WHERE ai.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts)
      AND ai.Connected = 1 AND ai.Account = @Account;

    SELECT ps.*
    FROM dbo.ProductSpecifications ps
    WHERE ps.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT pa.*
    FROM dbo.ProductApplications pa
    WHERE pa.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT pack.*
    FROM dbo.ProductPackages pack
    WHERE pack.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT param.Id,
           param.ProductId,
           param.CategoryParameterId,
           param.Value,
           param.IsForProduct,
           catParam.Name
    FROM dbo.RolmarProductParameters param
    JOIN dbo.CategoryParameters catParam ON param.CategoryParameterId = catParam.Id
    WHERE param.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpload
    @MinProductStock INT,
    @MinProductPrice DECIMAL(15,4),
    @IntegrationCompany INT,
    @Account INT,
    @Categories NVARCHAR(MAX) = NULL,
    @RequireDetails BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    DECLARE @HasCategoryFilter BIT =
        CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]'))) THEN 1 ELSE 0 END;

    IF @HasCategoryFilter = 1
        EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

    SELECT
        p.Id, p.Code, p.CustomerCode, p.Name, p.Description, p.Ean, p.Weight, p.Fits, p.SupplierName, p.InStock,
        p.Unit, p.CurrencyPrice, p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DefaultAllegroCategory, p.Package,
        p.CreatedDate, p.UpdatedDate, p.Substitutes, p.AllegroId, p.AllegroName, p.DeliveryType, p.ShippingClass, p.BlockedReturn,
        ps.Id, ps.ProductId, ps.Name, ps.Value, ps.UnitName,
        pp.Id, pp.ProductId, pp.CategoryParameterId, cp.Name, pp.Value, pp.IsForProduct,
        ap.Id, ap.ApplicationId, ap.Name, ap.ParentID, ap.ProductId,
        pack.Id, pack.PackEan, pack.PackGrossWeight, pack.PackNettWeight, pack.PackQty, pack.PackRequired, pack.PackUnit, pack.ProductId
    FROM dbo.RolmarProducts p
    LEFT JOIN dbo.ProductSpecifications ps ON ps.ProductId = p.Id
    JOIN dbo.RolmarProductParameters pp ON pp.ProductId = p.Id
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    LEFT JOIN dbo.ProductApplications ap ON ap.ProductId = p.Id
    LEFT JOIN dbo.ProductPackages pack ON pack.ProductId = p.Id
    LEFT JOIN dbo.AllegroOffers ao ON ao.ExternalId = p.Code AND ao.Account = @Account
    WHERE p.InStock >= @MinProductStock
      AND p.PriceNet >= @MinProductPrice
      AND NULLIF(p.DefaultAllegroCategory, 0) IS NOT NULL
      AND ao.Id IS NULL
      AND p.IntegrationCompany = @IntegrationCompany
      AND p.BlockedReturn = 0
      AND (@RequireDetails = 0 OR p.DetailsFetchedAt IS NOT NULL)
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id))
      -- Kategorie zapisujemy razem ze szczegolami, wiec ich brak znaczy "szczegoly niepobrane":
      -- produkt bez opisu i zdjec nie moze trafic do wystawienia.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
    ORDER BY p.Id;

    DROP TABLE #Allowed;
END
GO

-- Kandydaci InPost: cenniki dostawy podawane razem z dostawca, ktorego produkty za nimi stoja
-- (cenniki Rolmar -> produkty Rolmar, w przyszlosci cenniki Gaski -> produkty Gaski).
-- @DeliveryLists: [{"name":"Mały Asortyment Rolmar","company":1}, ...]
CREATE OR ALTER PROCEDURE dbo.InPostOffers_GetCandidates
    @Account INT,
    @DeliveryLists NVARCHAR(MAX),
    @OfferIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Lists
    (
        Name NVARCHAR(400) NOT NULL,
        Company INT NOT NULL
    );

    INSERT INTO #Lists (Name, Company)
    SELECT Name, Company
    FROM OPENJSON(@DeliveryLists) WITH (Name NVARCHAR(400) '$.name', Company INT '$.company');

    CREATE TABLE #Offers
    (
        OfferId NVARCHAR(128) NOT NULL PRIMARY KEY,
        ProductId INT NOT NULL,
        CategoryId INT NOT NULL
    );

    INSERT INTO #Offers (OfferId, ProductId, CategoryId)
    SELECT o.Id, p.Id, o.CategoryId
    FROM dbo.AllegroOffers o
    JOIN #Lists l ON l.Name = o.DeliveryName
    JOIN dbo.RolmarProducts p
        ON p.Code = o.ExternalId
       AND p.IntegrationCompany = l.Company
    WHERE o.Account = @Account
      AND o.Status = 'ACTIVE'
      AND (@OfferIds IS NULL OR o.Id IN (SELECT [value] FROM OPENJSON(@OfferIds)));

    SELECT
        o.Id, o.ExternalId, o.Name, o.CategoryId, o.Price, o.Stock, o.Status, o.DeliveryName,
        o.ProductId AS AllegroProductId, o.Images, o.Weight, o.HandlingTime,
        o.ResponsibleProducer, o.ResponsiblePerson, o.VisitsCount,
        p.Id AS ProductId, p.Code AS ProductCode, p.Name AS ProductName, p.Description AS ProductDescription,
        p.Ean, p.Weight AS ProductWeight, p.Fits, p.Substitutes, p.InStock, p.Unit,
        p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DeliveryType, p.ArchivedAt, p.Package, p.ShippingClass, p.SupplierName,
        i.InPostOfferId, i.LastCommandId, i.Status AS InPostStatus, i.Price AS InPostPrice, i.Stock AS InPostStock
    FROM #Offers t
    JOIN dbo.AllegroOffers o ON o.Id = t.OfferId
    JOIN dbo.RolmarProducts p ON p.Id = t.ProductId
    LEFT JOIN dbo.InPostOffers i ON i.AllegroOfferId = o.Id;

    SELECT d.OfferId, d.SectionId, d.Type, d.Content
    FROM dbo.AllegroOfferDescriptions d
    JOIN #Offers t ON t.OfferId = d.OfferId
    ORDER BY d.OfferId, d.SectionId, d.Id;

    SELECT a.OfferId, cp.Name, a.Type, a.ValuesJson
    FROM dbo.AllegroOfferAttributes a
    JOIN #Offers t ON t.OfferId = a.OfferId
    JOIN dbo.CategoryParameters cp
        ON cp.ParameterId = TRY_CAST(a.AttributeId AS INT)
       AND cp.CategoryId = t.CategoryId;

    SELECT pp.ProductId, cp.Name, pp.Value
    FROM dbo.RolmarProductParameters pp
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = pp.ProductId
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    WHERE NULLIF(LTRIM(RTRIM(pp.Value)), '') IS NOT NULL;

    SELECT s.ProductId, s.Name, s.Value, s.UnitName
    FROM dbo.ProductSpecifications s
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = s.ProductId;

    SELECT pc.ProductId, sc.SourceKey
    FROM dbo.ProductCategories pc
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = pc.ProductId
    JOIN dbo.SupplierCategories sc ON sc.Id = pc.CategoryId;
END
GO
