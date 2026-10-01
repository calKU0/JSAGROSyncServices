-- Krok parametrow przypisuje je tylko produktom, ktore nie maja zadnego parametru
-- (RolmarProducts_GetToUpdateParameters), wiec nowe mapowanie nigdy nie trafia na produkty
-- juz raz przetworzone. Dwa parametry nie zaleza od danych produktu i mozna je uzupelnic
-- wprost w bazie, bez odpytywania dostawcy:
--
--   "Liczba ... w ofercie"  - sprzedajemy jedna pozycje, wiec zawsze 1,
--   "Strona zabudowy"       - wartosc ze slownika kategorii, ktora nie zawezza zastosowania.
--
-- Bez tego Allegro odrzucalo oferty bledem "Uzupelnij parametry obowiazkowe: Strona zabudowy,
-- Liczba tarcz w ofercie" w kazdym cyklu. Nazwy i kolejnosc preferencji podaje wywolujacy,
-- zeby slownik wartosci byl w jednym miejscu - w kodzie.

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
    --    Gdy slownik ma same wartosci kierunkowe (sam "przod" i "tyl"), nie wstawiamy nic:
    --    zmyslony bok wprowadzalby kupujacego w blad.
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

    -- 3. Wiersze juz istniejace, ale z pusta wartoscia. Dla parametru nieobowiazkowego krok
    --    parametrow zapisywal pusta wartosc, a taki wiersz blokuje oba wstawienia powyzej.
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
