-- Wycofanie migracji 20261203_19. Zalozylem tam, ze parametr wymagany przez kategorie
-- (Required = 1) nalezy do sekcji oferty - to nieprawda. Allegro przyjmuje parametr opisujacy
-- produkt wylacznie w sekcji produktu i odrzuca oferte komunikatem
-- "Parameter `227349:Strona zabudowy` should not be specified as in section `offer`".
--
-- O sekcji decyduje samo DescribesProduct. Przywracamy ten stan dla wszystkich dostawcow:
-- poprzedni UPDATE nie mial filtra po IntegrationCompany, wiec dotknal calej tabeli.

UPDATE pp
SET pp.IsForProduct = cp.DescribesProduct
FROM dbo.RolmarProductParameters pp
JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
WHERE pp.IsForProduct <> cp.DescribesProduct;
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProductParameters_FillDataIndependent
    @IntegrationCompany INT,
    @CountInOfferPattern NVARCHAR(100),
    @CountInOfferValue NVARCHAR(50),
    @MountingSideName NVARCHAR(100),
    @UniversalSides NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. "Liczba ... w ofercie"
    INSERT INTO dbo.RolmarProductParameters (ProductId, CategoryParameterId, Value, IsForProduct)
    SELECT p.Id, cp.Id, @CountInOfferValue, cp.DescribesProduct
    FROM dbo.RolmarProducts p
    JOIN dbo.CategoryParameters cp
        ON cp.CategoryId = p.DefaultAllegroCategory
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.DefaultAllegroCategory <> 0
      AND LOWER(cp.Name) LIKE @CountInOfferPattern
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolmarProductParameters pp
          WHERE pp.ProductId = p.Id AND pp.CategoryParameterId = cp.Id);

    -- 2. "Strona zabudowy" - pierwsza wartosc z listy preferencji, ktora kategoria dopuszcza.
    INSERT INTO dbo.RolmarProductParameters (ProductId, CategoryParameterId, Value, IsForProduct)
    SELECT p.Id, cp.Id, pick.Value, cp.DescribesProduct
    FROM dbo.RolmarProducts p
    JOIN dbo.CategoryParameters cp
        ON cp.CategoryId = p.DefaultAllegroCategory
    CROSS APPLY (
        SELECT TOP 1 v.Value
        FROM dbo.CategoryParameterValues v
        JOIN OPENJSON(@UniversalSides) j
            ON LOWER(LTRIM(RTRIM(v.Value))) = LOWER(j.[value])
        WHERE v.CategoryParameterId = cp.Id
        ORDER BY CAST(j.[key] AS INT)
    ) pick
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.DefaultAllegroCategory <> 0
      AND LOWER(cp.Name) = LOWER(@MountingSideName)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolmarProductParameters pp
          WHERE pp.ProductId = p.Id AND pp.CategoryParameterId = cp.Id);

    -- 3. Wiersze juz istniejace, ale z pusta wartoscia.
    UPDATE pp
    SET pp.Value = @CountInOfferValue
    FROM dbo.RolmarProductParameters pp
    JOIN dbo.RolmarProducts p ON p.Id = pp.ProductId
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND LOWER(cp.Name) LIKE @CountInOfferPattern
      AND NULLIF(LTRIM(RTRIM(ISNULL(pp.Value, N''))), N'') IS NULL;

    UPDATE pp
    SET pp.Value = pick.Value
    FROM dbo.RolmarProductParameters pp
    JOIN dbo.RolmarProducts p ON p.Id = pp.ProductId
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    CROSS APPLY (
        SELECT TOP 1 v.Value
        FROM dbo.CategoryParameterValues v
        JOIN OPENJSON(@UniversalSides) j
            ON LOWER(LTRIM(RTRIM(v.Value))) = LOWER(j.[value])
        WHERE v.CategoryParameterId = cp.Id
        ORDER BY CAST(j.[key] AS INT)
    ) pick
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND LOWER(cp.Name) = LOWER(@MountingSideName)
      AND NULLIF(LTRIM(RTRIM(ISNULL(pp.Value, N''))), N'') IS NULL;

    SELECT @@ROWCOUNT;
END
GO
