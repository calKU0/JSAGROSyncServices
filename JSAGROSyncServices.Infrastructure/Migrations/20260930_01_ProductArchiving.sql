-- Archiwizacja produktow wycofanych u dostawcy.
--
-- Dotad produkt, ktorego dostawca przestal zwracac, zostawal w bazie z ostatnim znanym stanem.
-- Stan zerowal sie tylko wtedy, gdy dostawca jawnie oddal zero - a produkt usuniety z katalogu
-- po prostu znika z odpowiedzi, wiec jego oferta wisiala na Allegro bez pokrycia w towarze.
--
--   LastSeenAt - kiedy dostawca ostatni raz oddal ten produkt (API lub plik CSV).
--                Odnotowywane przy kazdym pobraniu listy produktow, takze gdy nic sie nie zmienilo.
--   ArchivedAt - kiedy uznalismy produkt za wycofany. Ustawiane dopiero po okresie karencji,
--                bo jedno nieudane pobranie od dostawcy nie moze pokonczyc calego asortymentu.
--                Powrot produktu do odpowiedzi kasuje znacznik.

ALTER TABLE dbo.RolmarProducts ADD
    LastSeenAt DATETIME2 NULL,
    ArchivedAt DATETIME2 NULL;
GO

-- Produkty sprzed migracji traktujemy jak widziane ostatnio wtedy, kiedy ostatnio sie zmienily -
-- inaczej pierwszy przebieg po wdrozeniu zarchiwizowalby wszystko, czego akurat nie odswiezyl.
UPDATE dbo.RolmarProducts
SET LastSeenAt = ISNULL(UpdatedDate, CreatedDate)
WHERE LastSeenAt IS NULL;
GO

CREATE INDEX IX_RolmarProducts_Archived
    ON dbo.RolmarProducts (IntegrationCompany, ArchivedAt)
    INCLUDE (Code, LastSeenAt);
GO

-- ============================================================ odnotowanie obecnosci

-- Wolane po kazdym pobraniu listy produktow od dostawcy, komplet kodow z tego pobrania.
-- Produkt, ktory wrocil do oferty dostawcy, traci znacznik archiwalnego i wraca do wystawiania.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_MarkSeen
    @IntegrationCompany INT,
    @Codes dbo.ProductCodeType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET p.LastSeenAt = SYSUTCDATETIME(),
        p.ArchivedAt = NULL
    FROM dbo.RolmarProducts p
    JOIN (SELECT DISTINCT Code FROM @Codes) c ON c.Code = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany;
END
GO

-- ============================================================ archiwizacja

-- Oznacza jako archiwalne produkty, ktorych dostawca nie oddal od @GraceDays dni.
--
-- Karencja jest tu po to, zeby chwilowa awaria API albo niekompletny plik CSV nie pokonczyly
-- ofert calego katalogu - produkt musi zniknac z kilku kolejnych pobran z rzedu.
--
-- @Categories zawezaja sprawdzenie do kategorii, ktore nadal synchronizujemy. Produkt poza nimi
-- nie jest pobierany wcale, wiec jego brak nic nie znaczy - jego oferte i tak konczy filtr kategorii.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_ArchiveMissing
    @IntegrationCompany INT,
    @GraceDays INT,
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Cutoff DATETIME2 = DATEADD(DAY, -ABS(@GraceDays), SYSUTCDATETIME());

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    DECLARE @HasCategoryFilter BIT =
        CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]'))) THEN 1 ELSE 0 END;

    IF @HasCategoryFilter = 1
        EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

    UPDATE p
    SET p.ArchivedAt = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.ArchivedAt IS NULL
      AND p.LastSeenAt IS NOT NULL
      AND p.LastSeenAt < @Cutoff
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id));

    SELECT @@ROWCOUNT AS Archived;

    DROP TABLE #Allowed;
END
GO

-- ============================================================ filtry korzystajace z archiwum

-- Produktu wycofanego u dostawcy nie wystawiamy.
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
      AND p.ArchivedAt IS NULL
      AND (@RequireDetails = 0 OR p.DetailsFetchedAt IS NOT NULL)
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id))
      -- Kategorie zapisujemy razem ze szczegolami, wiec ich brak znaczy "szczegoly niepobrane":
      -- produkt bez opisu i zdjec nie moze trafic do wystawienia.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
    ORDER BY p.Id;

    DROP TABLE #Allowed;
END
GO

-- Oferty do zakonczenia: produkt wycofany u dostawcy albo wypadl ze skonfigurowanych kategorii.
--
-- Wczesniej procedura konczyla prace, gdy nie bylo skonfigurowanych kategorii - teraz brak filtra
-- kategorii wylacza tylko czesc kategoryjna, a oferty produktow archiwalnych konczymy zawsze.
CREATE OR ALTER PROCEDURE dbo.AllegroOffers_GetOffersToEnd
    @IntegrationCompany INT,
    @Account INT,
    @DeliveryNames NVARCHAR(MAX),
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @DeliveryNames IS NULL OR LTRIM(RTRIM(@DeliveryNames)) = N''
        RETURN;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    DECLARE @HasCategoryFilter BIT =
        CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]'))) THEN 1 ELSE 0 END;

    IF @HasCategoryFilter = 1
        EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

    SELECT
        ao.Id AS OfferId,
        ao.Status,
        ao.DeliveryName,
        p.Id AS ProductId,
        p.Code,
        p.Name
    FROM dbo.AllegroOffers ao
    JOIN dbo.RolmarProducts p
        ON p.Code = ao.ExternalId
       AND p.IntegrationCompany = @IntegrationCompany
    WHERE ao.Account = @Account
      AND ao.Status IN ('ACTIVE', 'INACTIVE')
      AND ao.DeliveryName IN (SELECT TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM [value]) FROM OPENJSON(@DeliveryNames))
      AND
      (
          -- Produkt wycofany u dostawcy - nie mamy juz czego sprzedac.
          p.ArchivedAt IS NOT NULL
          OR
          (
              @HasCategoryFilter = 1
              AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id)
              -- Konczymy tylko przy pewnosci, ze produkt jest poza kategoriami. Produkt bez
              -- przypisanej kategorii zostawiamy - to brak danych, a nie decyzja o wycofaniu.
              AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
          )
      );

    DROP TABLE #Allowed;
END
GO

-- Ofert produktow archiwalnych nie aktualizujemy - ida do zakonczenia, a patch przywrocilby im ACTIVE.
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
        AND p.BlockedReturn = 0
        AND p.ArchivedAt IS NULL;

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

-- Katalogu Allegro nie przeszukujemy dla produktow wycofanych u dostawcy.
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
      AND ArchivedAt IS NULL
      AND (AllegroSearchedAt IS NULL
           OR AllegroSearchedAt < DATEADD(DAY, -@RetryAfterDays, SYSUTCDATETIME()));
END
GO

-- ============================================================ kolejka szczegolow

-- Kolejka szczegolow po kodzie produktu.
--
-- Inter Cars nie limituje liczby zapytan, wiec nie ma powodu dzielic katalogu na dzienne porcje:
-- @Limit <= 0 znaczy "wszystko, co wymaga pobrania". Odswiezamy produkty bez szczegolow
-- oraz te, ktorych szczegoly sa starsze niz @RefreshAfterDays - dzieki czemu krok sam sie konczy,
-- zamiast krecic katalog w kolko.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetCodesForDetailUpdate
    @Limit INT,
    @IntegrationCompany INT,
    @RefreshAfterDays INT = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Take INT = CASE WHEN @Limit > 0 THEN @Limit ELSE 2147483647 END;
    DECLARE @Cutoff DATETIME2 = DATEADD(DAY, -ABS(@RefreshAfterDays), SYSUTCDATETIME());

    SELECT TOP (@Take) p.Code
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.ArchivedAt IS NULL
      AND (p.DetailsFetchedAt IS NULL OR p.DetailsFetchedAt < @Cutoff)
    ORDER BY
        CASE WHEN p.DetailsFetchedAt IS NULL THEN 0 ELSE 1 END,
        p.DetailsFetchedAt,
        p.Code;
END
GO
