-- Inter Cars jako trzeci dostawca (IntegrationCompany = 3).
--
-- Dwie nowe kolumny produktu:
--   BlockedReturn    - towar bez prawa zwrotu. Inter Cars oznacza tak czesc asortymentu;
--                      takich produktow nie wystawiamy i nie aktualizujemy na Allegro.
--                      Pozostali dostawcy maja tu 0, wiec filtr ich nie dotyczy.
--   AllegroName      - nazwa produktu z katalogu Allegro, ustalona przy dopasowaniu. Inter Cars
--                      podaje tylko nazwe grupy asortymentowej ("Pozostale elementy zawieszenia TUZ"),
--                      wiec tytul oferty budujemy z nazwy katalogowej, gdy ja znamy.
--   DetailsFetchedAt - kiedy pobralismy szczegoly produktu (waga, wymiary, EAN).
--                      Inter Cars zwraca je wylacznie po SKU, po jednym zapytaniu na produkt,
--                      wiec lista produktow sama w sobie nie wystarcza do wystawienia oferty:
--                      bez wagi i wymiarow cennik dostawy wybralby sie losowo.

ALTER TABLE dbo.RolmarProducts ADD
    BlockedReturn    BIT            NOT NULL CONSTRAINT DF_RolmarProducts_BlockedReturn DEFAULT (0),
    AllegroName      NVARCHAR(255)  NULL,
    DetailsFetchedAt DATETIME2      NULL;
GO

-- Nazwa produktu z katalogu Allegro zapisywana razem z dopasowaniem.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UpdateAllegroId
    @ProductId INT,
    @AllegroId NVARCHAR(255),
    @CategoryId INT,
    @AllegroName NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RolmarProducts
    SET AllegroId = @AllegroId,
        DefaultAllegroCategory = @CategoryId,
        AllegroName = ISNULL(@AllegroName, AllegroName)
    WHERE Id = @ProductId;
END
GO

-- ============================================================ typ tabelaryczny upserta

-- Typu tabelarycznego nie da sie zmienic w miejscu - procedura, ktora go uzywa, musi zniknac
-- na czas podmiany. Odtwarzamy ja nizej w niezmienionej postaci, poza nowa kolumna.
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
    InStock FLOAT NOT NULL,
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
    BlockedReturn BIT NOT NULL
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
            p.BlockedReturn
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
        ISNULL(target.InStock, 0) <> ISNULL(source.InStock, 0) OR
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
        target.AcceptedPriceGross IS NULL
    ) THEN
        UPDATE SET
            Name = source.Name,
            SupplierLogo = source.SupplierLogo,
            SupplierName = source.SupplierName,
            Description = source.Description,
            CustomerCode = source.CustomerCode,
            Ean = source.Ean,
            InStock = source.InStock,
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
            PriceNet, PriceGross, AcceptedPriceGross, Package, BlockedReturn, CreatedDate, UpdatedDate
        )
        VALUES
        (
            source.Code, source.Name, source.SupplierLogo, source.SupplierName, source.Description, source.CustomerCode, source.Ean,
            source.InStock, source.Weight, source.Fits, source.Unit, source.CurrencyPrice, source.Substitutes,
            source.IntegrationCompany, source.IntegrationId, source.DeliveryType,
            source.PriceNet, source.PriceGross, source.PriceGross, source.Package, source.BlockedReturn, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
END
GO

-- Upsert pojedynczego produktu - ta sama nowa kolumna, domyslnie 0 dla wywolan bez niej.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_Upsert
    @Code NVARCHAR(255),
    @Name NVARCHAR(255),
    @SupplierLogo NVARCHAR(255) = NULL,
    @SupplierName NVARCHAR(255) = NULL,
    @Description NVARCHAR(MAX) = NULL,
    @CustomerCode NVARCHAR(255) = NULL,
    @Ean NVARCHAR(50) = NULL,
    @InStock FLOAT = 0,
    @Weight FLOAT,
    @Fits NVARCHAR(MAX) = NULL,
    @Unit NVARCHAR(50),
    @Currency NVARCHAR(50) = NULL,
    @Substitutes NVARCHAR(MAX) = NULL,
    @IntegrationCompany INT,
    @IntegrationId INT = NULL,
    @DeliveryType INT = 0,
    @PriceNet DECIMAL(18, 2),
    @PriceGross DECIMAL(18, 2),
    @Package DECIMAL(18, 2),
    @MaxPriceDropPercent DECIMAL(5, 2) = 0,
    @BlockedReturn BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Result TABLE (Id INT NOT NULL);

    MERGE dbo.RolmarProducts AS target
    USING
    (
        SELECT
            @Code AS Code,
            LEFT(@Name,
                CASE
                    WHEN LEN(@Name) <= 75 THEN LEN(@Name)
                    ELSE 75 - CHARINDEX(' ', REVERSE(LEFT(@Name, 75))) + 1
                END) AS Name,
            @SupplierLogo AS SupplierLogo,
            @SupplierName AS SupplierName,
            @Description AS Description,
            @CustomerCode AS CustomerCode,
            @Ean AS Ean,
            @InStock AS InStock,
            @Weight AS Weight,
            NULLIF(@Fits, '') AS Fits,
            @Unit AS Unit,
            @Currency AS CurrencyPrice,
            NULLIF(@Substitutes, '') AS Substitutes,
            @IntegrationCompany AS IntegrationCompany,
            @IntegrationId AS IntegrationId,
            @DeliveryType AS DeliveryType,
            @PriceNet AS PriceNet,
            @PriceGross AS PriceGross,
            @Package AS Package,
            @BlockedReturn AS BlockedReturn
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
        ISNULL(target.InStock, 0) <> ISNULL(source.InStock, 0) OR
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
        target.AcceptedPriceGross IS NULL
    ) THEN
        UPDATE SET
            Name = source.Name,
            SupplierLogo = source.SupplierLogo,
            SupplierName = source.SupplierName,
            Description = source.Description,
            CustomerCode = source.CustomerCode,
            Ean = source.Ean,
            InStock = source.InStock,
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
            PriceNet, PriceGross, AcceptedPriceGross, Package, BlockedReturn, CreatedDate, UpdatedDate
        )
        VALUES
        (
            source.Code, source.Name, source.SupplierLogo, source.SupplierName, source.Description, source.CustomerCode, source.Ean,
            source.InStock, source.Weight, source.Fits, source.Unit, source.CurrencyPrice, source.Substitutes,
            source.IntegrationCompany, source.IntegrationId, source.DeliveryType,
            source.PriceNet, source.PriceGross, source.PriceGross, source.Package, source.BlockedReturn, SYSUTCDATETIME(), SYSUTCDATETIME()
        )
    OUTPUT inserted.Id INTO @Result (Id);

    -- Gdy nic sie nie zmienilo, MERGE nie wykonuje zadnej akcji - wtedy zwracamy istniejacy wiersz.
    IF NOT EXISTS (SELECT 1 FROM @Result)
    BEGIN
        INSERT INTO @Result (Id)
        SELECT TOP (1) Id
        FROM dbo.RolmarProducts
        WHERE Code = @Code
          AND IntegrationCompany = @IntegrationCompany
        ORDER BY Id DESC;
    END

    SELECT TOP (1) Id FROM @Result;
END
GO

-- ============================================================ szczegoly produktu

-- Kolejka szczegolow po kodzie produktu. Odpowiednik RolmarProducts_GetForDetailUpdate,
-- ktory zwraca IntegrationId - a Inter Cars identyfikuje produkty alfanumerycznym SKU.
-- Najpierw produkty bez szczegolow, a gdy juz wszystkie je maja - od najdawniej odswiezanych,
-- dzieki czemu katalog odswieza sie w kolko, bez zadnego progu czasowego.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetCodesForDetailUpdate
    @Limit INT,
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit) p.Code
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
    ORDER BY
        CASE WHEN p.DetailsFetchedAt IS NULL THEN 0 ELSE 1 END,
        p.DetailsFetchedAt,
        p.Code;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_MarkDetailsFetched
    @IntegrationCompany INT,
    @Codes dbo.ProductCodeType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET p.DetailsFetchedAt = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    JOIN (SELECT DISTINCT Code FROM @Codes) c ON c.Code = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany;
END
GO

-- Katalogu Allegro nie ma sensu przeszukiwac dla towarow, ktorych i tak nie wystawimy -
-- u Inter Cars to co piaty produkt, a limit zapytan do Allegro jest wspolny dla calego cyklu.
CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_GetWithoutAllegroId]
    @IntegrationCompany INT,
    @RetryAfterDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.RolmarProducts
    WHERE AllegroId IS NULL
      AND IntegrationCompany = @IntegrationCompany
      AND BlockedReturn = 0
      AND (AllegroSearchedAt IS NULL
           OR AllegroSearchedAt < DATEADD(DAY, -@RetryAfterDays, SYSUTCDATETIME()));
END
GO

-- ============================================================ filtry ofert

-- Produkty do wystawienia: bez towarow bez prawa zwrotu i - dla dostawcow, u ktorych
-- szczegoly przychodza osobnym zapytaniem - bez produktow, ktorych szczegolow jeszcze nie mamy.
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
        p.CreatedDate, p.UpdatedDate, p.Substitutes, p.AllegroId, p.AllegroName, p.DeliveryType, p.BlockedReturn,
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

-- Oferty do aktualizacji: pomijamy towary bez prawa zwrotu - ich ofert nie ruszamy wcale.
-- Oddajemy tez ao.ProductId, czyli id produktu z katalogu Allegro podpietego do oferty. Bez niego
-- patch nie wiedzial, ze oferta ma juz produkt, i wysylal pelna propozycje nowego - a takie zadanie
-- Allegro odrzuca bledami walidacji parametrow.
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
        CreatedDate DATETIME2 NOT NULL,
        UpdatedDate DATETIME2 NOT NULL,
        OfferPrice DECIMAL(18, 2) NOT NULL
    );

    INSERT INTO #OffersWithProducts
    (
        OfferId, OfferProductId, ExternalId, OfferName, CategoryId, Status, StartingAt, DeliveryName,
        ProductId, AllegroId, AllegroName, Code, CustomerCode, ProductName, Description, Ean, Weight, Fits, SupplierName,
        Substitutes, InStock, Unit, CurrencyPrice, PriceNet, PriceGross, AcceptedPriceGross,
        PriceDropDetectedAt, DefaultAllegroCategory, Package, DeliveryType, CreatedDate, UpdatedDate, OfferPrice
    )
    SELECT
        ao.Id, ao.ProductId, ao.ExternalId, ao.Name, ao.CategoryId, ao.Status, ao.StartingAt, ao.DeliveryName,
        p.Id, p.AllegroId, p.AllegroName, p.Code, p.CustomerCode, p.Name, p.Description,
        p.Ean, p.Weight, p.Fits, p.SupplierName, p.Substitutes, p.InStock, p.Unit,
        p.CurrencyPrice, p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DefaultAllegroCategory, p.Package, p.DeliveryType,
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
