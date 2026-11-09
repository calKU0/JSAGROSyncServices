-- Produkty bez żadnego wiersza parametrów też respektują ParametersCheckedAt.
--
-- Przy parametrach z katalogu (Rolmar) produkt w kategorii bez obowiązkowych parametrów oferty nie ma
-- żadnego wiersza parametrów. Gałąź "brak parametrów" wybierała go niezależnie od ostatniego sprawdzenia,
-- więc krok parametrów przeliczał tysiące takich produktów w każdym cyklu. Teraz wraca dopiero po zmianie
-- produktu albo odświeżeniu parametrów którejś z jego kategorii - tak jak produkt z brakującym parametrem.
-- Treść jak w migracji 30, z warunkiem świeżości także w pierwszej gałęzi.

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpdateParameters
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Targets (ProductId INT NOT NULL, CategoryId INT NOT NULL, PRIMARY KEY (ProductId, CategoryId));

    INSERT INTO #Targets (ProductId, CategoryId)
    EXEC dbo.RolmarProducts_GetParameterTargetCategories @IntegrationCompany = @IntegrationCompany;

    CREATE TABLE #ToUpdate (ProductId INT NOT NULL PRIMARY KEY);

    INSERT INTO #ToUpdate (ProductId)
    SELECT p.Id
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND (p.DefaultAllegroCategory <> 0 OR p.AllegroId IS NOT NULL)
      AND (
            (
                NOT EXISTS (SELECT 1 FROM dbo.RolmarProductParameters pp WHERE pp.ProductId = p.Id)
                AND (
                    p.ParametersCheckedAt IS NULL
                    OR p.ParametersCheckedAt < p.UpdatedDate
                    OR EXISTS (
                        SELECT 1
                        FROM #Targets t
                        JOIN dbo.CategoryParameters cp ON cp.CategoryId = t.CategoryId
                        WHERE t.ProductId = p.Id
                          AND p.ParametersCheckedAt < cp.FetchedAt)
                )
            )
            OR (
                EXISTS (
                    SELECT 1
                    FROM #Targets t
                    JOIN dbo.CategoryParameters cp ON cp.CategoryId = t.CategoryId
                    WHERE t.ProductId = p.Id
                      AND (cp.Required = 1 OR cp.RequiredForProduct = 1)
                      AND NOT EXISTS (
                          SELECT 1 FROM dbo.RolmarProductParameters pp
                          WHERE pp.ProductId = p.Id AND pp.CategoryParameterId = cp.Id)
                      AND (p.ParametersCheckedAt IS NULL
                           OR p.ParametersCheckedAt < p.UpdatedDate
                           OR p.ParametersCheckedAt < cp.FetchedAt)
                )
            )
          );

    SELECT p.*, pa.*, ps.*
    FROM #ToUpdate u
    JOIN dbo.RolmarProducts p ON p.Id = u.ProductId
    LEFT JOIN dbo.ProductApplications pa ON pa.ProductId = p.Id
    LEFT JOIN dbo.ProductSpecifications ps ON ps.ProductId = p.Id
    ORDER BY p.Id;
END
GO
