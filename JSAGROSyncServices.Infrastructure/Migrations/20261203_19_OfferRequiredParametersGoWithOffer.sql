-- IsForProduct bralo sie wprost z DescribesProduct, wiec parametr opisujacy produkt nigdy nie
-- jechal z oferta. "Strona zabudowy" ma DescribesProduct = 1 i w czesci kategorii jest zarazem
-- wymagana na ofercie - wartosc byla wiec zapisana w bazie, ale nie trafiala do Allegro
-- i oferta wracala z bledem "Uzupelnij parametry obowiazkowe: Strona zabudowy".
--
-- Parametr wymagany na ofercie nalezy do oferty niezaleznie od tego, czy opisuje produkt.

UPDATE pp
SET pp.IsForProduct = 0
FROM dbo.RolmarProductParameters pp
JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
WHERE cp.Required = 1
  AND pp.IsForProduct = 1;
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
    SELECT p.Id, cp.Id, @CountInOfferValue, CASE WHEN cp.Required = 1 THEN 0 ELSE cp.DescribesProduct END
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
    SELECT p.Id, cp.Id, pick.Value, CASE WHEN cp.Required = 1 THEN 0 ELSE cp.DescribesProduct END
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
