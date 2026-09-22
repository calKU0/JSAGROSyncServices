-- Filtr kategorii w RolmarProducts_GetToUpload byl liczony korelowanymi podzapytaniami dla kazdego wiersza,
-- przez co zapytanie zwalnialo z kilku sekund do kilku minut. Ponizej dozwolone produkty sa wyliczane
-- raz, do tabeli tymczasowej, a glowne zapytanie robi juz tylko zwykle zlaczenie.

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpload
    @MinProductStock INT,
    @MinProductPrice DECIMAL(15,4),
    @IntegrationCompany INT,
    @Account INT,
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Cat (Value NVARCHAR(400) NOT NULL, ValueId INT NULL, ValueLen INT NOT NULL);

    IF @Categories IS NOT NULL AND LTRIM(RTRIM(@Categories)) <> ''
    BEGIN
        INSERT INTO #Cat (Value, ValueId, ValueLen)
        SELECT DISTINCT
            REPLACE(REPLACE(LTRIM(RTRIM([value])), ' >', '>'), '> ', '>'),
            TRY_CAST(LTRIM(RTRIM([value])) AS INT),
            LEN(REPLACE(REPLACE(LTRIM(RTRIM([value])), ' >', '>'), '> ', '>'))
        FROM OPENJSON(@Categories)
        WHERE LTRIM(RTRIM([value])) <> '';
    END

    DECLARE @HasCategoryFilter BIT = CASE WHEN EXISTS (SELECT 1 FROM #Cat) THEN 1 ELSE 0 END;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    IF @HasCategoryFilter = 1
    BEGIN
        IF @IntegrationCompany = 1 -- Rolmar: kategorie to sciezki nazw
        BEGIN
            INSERT INTO #Allowed (ProductId)
            SELECT DISTINCT rc.ProductId
            FROM dbo.RolmarCategory rc
            JOIN #Cat c ON LEFT(REPLACE(REPLACE(rc.Name, ' >', '>'), '> ', '>'), c.ValueLen) = c.Value;

            -- produkt bez zadnej informacji o kategorii traktujemy jako dozwolony
            INSERT INTO #Allowed (ProductId)
            SELECT p.Id
            FROM dbo.RolmarProducts p
            WHERE p.IntegrationCompany = @IntegrationCompany
              AND NOT EXISTS (SELECT 1 FROM dbo.RolmarCategory rc WHERE rc.ProductId = p.Id)
              AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id);
        END
        ELSE -- Gaska: kategorie to id kategorii dostawcy
        BEGIN
            INSERT INTO #Allowed (ProductId)
            SELECT DISTINCT psc.ProductId
            FROM dbo.ProductSupplierCategories psc
            JOIN #Cat c ON c.ValueId = psc.CategoryId;

            INSERT INTO #Allowed (ProductId)
            SELECT p.Id
            FROM dbo.RolmarProducts p
            WHERE p.IntegrationCompany = @IntegrationCompany
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductSupplierCategories psc WHERE psc.ProductId = p.Id)
              AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id);
        END
    END

    SELECT
        p.Id,
        p.Code,
        p.Name,
        p.Description,
        p.Ean,
        p.Weight,
        p.Fits,
        p.SupplierName,
        p.InStock,
        p.Unit,
        p.CurrencyPrice,
        p.PriceNet,
        p.PriceGross,
        p.DefaultAllegroCategory,
        p.Package,
        p.CreatedDate,
        p.UpdatedDate,
        p.Substitutes,
        p.AllegroId,
        p.DeliveryType,
        ps.Id,
        ps.ProductId,
        ps.Name,
        ps.Value,
        ps.UnitName,
        pp.Id,
        pp.ProductId,
        pp.CategoryParameterId,
        cp.Name,
        pp.Value,
        pp.IsForProduct,
        ap.Id,
        ap.ApplicationId,
        ap.Name,
        ap.ParentID,
        ap.ProductId,
        pack.Id,
        pack.PackEan,
        pack.PackGrossWeight,
        pack.PackNettWeight,
        pack.PackQty,
        pack.PackRequired,
        pack.PackUnit,
        pack.ProductId
    FROM RolmarProducts p
    LEFT JOIN ProductSpecifications ps ON ps.ProductId = p.Id
    JOIN RolmarProductParameters pp ON pp.ProductId = p.Id
    JOIN CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    JOIN RolmarCategory rc ON rc.ProductId = p.Id
    LEFT JOIN ProductApplications ap ON ap.ProductId = p.Id
    LEFT JOIN ProductPackages pack ON pack.ProductId = p.Id
    LEFT JOIN AllegroOffers ao ON ao.ExternalId = p.Code AND ao.Account = @Account
    WHERE p.InStock >= @MinProductStock AND p.PriceNet >= @MinProductPrice
      AND NULLIF(p.DefaultAllegroCategory, 0) IS NOT NULL
      AND ao.Id IS NULL
      AND p.IntegrationCompany = @IntegrationCompany
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id))
    ORDER BY p.Id;

    DROP TABLE #Allowed;
    DROP TABLE #Cat;
END
GO

-- Indeksy pod nowe zapytania: dopasowanie kategorii i szukanie ofert po ExternalId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RolmarCategory_ProductId' AND object_id = OBJECT_ID('dbo.RolmarCategory'))
    CREATE NONCLUSTERED INDEX IX_RolmarCategory_ProductId ON dbo.RolmarCategory (ProductId) INCLUDE (Name);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductSupplierCategories_CategoryId' AND object_id = OBJECT_ID('dbo.ProductSupplierCategories'))
    CREATE NONCLUSTERED INDEX IX_ProductSupplierCategories_CategoryId ON dbo.ProductSupplierCategories (CategoryId) INCLUDE (ProductId);
GO

-- ExternalId/Status/DeliveryName byly NVARCHAR(MAX) (spadek po Entity Framework), przez co nie dalo sie
-- ich zaindeksowac, a laczenie ofert z produktami bylo zawsze pelnym skanem.
-- Najdluzsze wartosci w bazie: ExternalId 40, Status 8, DeliveryName 30 znakow.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AllegroOffers') AND name = 'ExternalId' AND max_length = -1)
   AND NOT EXISTS (SELECT 1 FROM dbo.AllegroOffers WHERE LEN(ExternalId) > 255)
BEGIN
    ALTER TABLE dbo.AllegroOffers ALTER COLUMN ExternalId NVARCHAR(255) NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AllegroOffers') AND name = 'Status' AND max_length = -1)
   AND NOT EXISTS (SELECT 1 FROM dbo.AllegroOffers WHERE LEN(Status) > 50)
BEGIN
    ALTER TABLE dbo.AllegroOffers ALTER COLUMN Status NVARCHAR(50) NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AllegroOffers') AND name = 'DeliveryName' AND max_length = -1)
   AND NOT EXISTS (SELECT 1 FROM dbo.AllegroOffers WHERE LEN(DeliveryName) > 255)
BEGIN
    ALTER TABLE dbo.AllegroOffers ALTER COLUMN DeliveryName NVARCHAR(255) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AllegroOffers_Account_ExternalId' AND object_id = OBJECT_ID('dbo.AllegroOffers'))
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AllegroOffers') AND name = 'ExternalId' AND max_length > 0)
    CREATE NONCLUSTERED INDEX IX_AllegroOffers_Account_ExternalId ON dbo.AllegroOffers (Account, ExternalId) INCLUDE (Status, DeliveryName);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RolmarProducts_IntegrationCompany_Code' AND object_id = OBJECT_ID('dbo.RolmarProducts'))
    CREATE NONCLUSTERED INDEX IX_RolmarProducts_IntegrationCompany_Code ON dbo.RolmarProducts (IntegrationCompany, Code) INCLUDE (Id);
GO
