-- Dopasowanie kategorii Rolmara musi konczyc sie na granicy segmentu sciezki:
-- "WARYNSKI ORIGIN" ma obejmowac "WARYNSKI ORIGIN>Narzedzia" (podkategorie),
-- ale nie "WARYNSKI ORIGINAL>..." (inna kategoria zaczynajaca sie tak samo).

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
            CROSS APPLY (SELECT REPLACE(REPLACE(rc.Name, ' >', '>'), '> ', '>') AS Path) n
            JOIN #Cat c
              ON LEFT(n.Path, c.ValueLen) = c.Value
             AND (LEN(n.Path) = c.ValueLen OR SUBSTRING(n.Path, c.ValueLen + 1, 1) = '>');

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

CREATE OR ALTER PROCEDURE dbo.AllegroOffers_GetOffersToEnd
    @IntegrationCompany INT,
    @Account INT,
    @DeliveryNames NVARCHAR(MAX),
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Categories IS NULL OR LTRIM(RTRIM(@Categories)) = ''
        RETURN;

    IF @DeliveryNames IS NULL OR LTRIM(RTRIM(@DeliveryNames)) = ''
        RETURN;

    CREATE TABLE #Cat (Value NVARCHAR(400) NOT NULL, ValueId INT NULL, ValueLen INT NOT NULL);

    INSERT INTO #Cat (Value, ValueId, ValueLen)
    SELECT DISTINCT
        REPLACE(REPLACE(LTRIM(RTRIM([value])), ' >', '>'), '> ', '>'),
        TRY_CAST(LTRIM(RTRIM([value])) AS INT),
        LEN(REPLACE(REPLACE(LTRIM(RTRIM([value])), ' >', '>'), '> ', '>'))
    FROM OPENJSON(@Categories)
    WHERE LTRIM(RTRIM([value])) <> '';

    IF NOT EXISTS (SELECT 1 FROM #Cat)
    BEGIN
        DROP TABLE #Cat;
        RETURN;
    END

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    IF @IntegrationCompany = 1
    BEGIN
        INSERT INTO #Allowed (ProductId)
        SELECT DISTINCT rc.ProductId
        FROM dbo.RolmarCategory rc
        CROSS APPLY (SELECT REPLACE(REPLACE(rc.Name, ' >', '>'), '> ', '>') AS Path) n
        JOIN #Cat c
          ON LEFT(n.Path, c.ValueLen) = c.Value
         AND (LEN(n.Path) = c.ValueLen OR SUBSTRING(n.Path, c.ValueLen + 1, 1) = '>');
    END
    ELSE
    BEGIN
        INSERT INTO #Allowed (ProductId)
        SELECT DISTINCT psc.ProductId
        FROM dbo.ProductSupplierCategories psc
        JOIN #Cat c ON c.ValueId = psc.CategoryId;
    END

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
      AND ao.DeliveryName IN (SELECT LTRIM(RTRIM([value])) FROM OPENJSON(@DeliveryNames))
      AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id)
      -- produkt bez zadnej informacji o kategorii dostawcy zostawiamy w spokoju
      AND
      (
          (@IntegrationCompany = 1 AND EXISTS (SELECT 1 FROM dbo.RolmarCategory rc WHERE rc.ProductId = p.Id))
          OR
          (@IntegrationCompany <> 1 AND EXISTS (SELECT 1 FROM dbo.ProductSupplierCategories psc WHERE psc.ProductId = p.Id))
      );

    DROP TABLE #Allowed;
    DROP TABLE #Cat;
END
GO
