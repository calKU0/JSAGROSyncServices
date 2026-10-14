-- Archiwizacja pomijala produkty bez przypisanej kategorii.
--
-- Filtr kategorii istnieje po to, zeby nie archiwizowac produktow, ktorych w ogole nie pobieramy -
-- ich brak w odpowiedzi dostawcy nic nie znaczy. Realizuje go #Allowed, czyli produkty
-- przypisane do skonfigurowanej kategorii albo jej potomka. Produkt bez ani jednego przypisania
-- nie trafial do tego zbioru, wiec nie dawal sie zarchiwizowac - niezaleznie od tego, jak dawno
-- dostawca przestal go oddawac.
--
-- A przypisanie powstaje dopiero przy pobraniu szczegolow. Produkty, ktorych szczegolow nigdy
-- nie udalo sie pobrac - czyli najbardziej podejrzane o wycofanie - byly wiec na archiwizacje
-- odporne na stale. Do bazy nie ma innej drogi niz pobranie skonfigurowanej kategorii, wiec brak
-- wiedzy o kategorii nie jest powodem, by taki produkt chronic.
--
-- Procedura zwraca teraz takze liczbe produktow, ktore przeszly karencje, ale zostaly pominiete
-- jako lezace poza skonfigurowanymi kategoriami - inaczej "zarchiwizowano 0" nie odrozniało
-- braku kandydatow od wyciecia ich przez filtr.

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
      AND (@HasCategoryFilter = 0
           OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id)
           OR NOT EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id));

    DECLARE @Archived INT = @@ROWCOUNT;

    -- Co zostalo po karencji, to dokladnie to, co wyciely kategorie.
    DECLARE @Skipped INT =
    (
        SELECT COUNT(*)
        FROM dbo.RolmarProducts p
        WHERE p.IntegrationCompany = @IntegrationCompany
          AND p.ArchivedAt IS NULL
          AND p.LastSeenAt IS NOT NULL
          AND p.LastSeenAt < @Cutoff
    );

    SELECT @Archived AS Archived, @Skipped AS SkippedOutsideCategories;

    DROP TABLE #Allowed;
END
GO
