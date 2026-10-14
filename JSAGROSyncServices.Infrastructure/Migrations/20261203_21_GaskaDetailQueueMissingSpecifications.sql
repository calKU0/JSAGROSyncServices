-- Specyfikacje produktow Gaski ginely co cykl. Lista kategorii nie niesie parametrow, wiec
-- produkt z listy mial pusta liste specyfikacji - a pusta lista w ProductSpecifications_ReplaceBatch
-- znaczy "wyczysc". Przyczyne usunieto w kodzie (brak specyfikacji = null, zapis ich nie rusza),
-- ale same dane nie wroca: kolejka szczegolow brala tylko produkty bez kategorii, a te maja
-- kategorie od pierwszego pobrania szczegolow.
--
-- Dlatego kolejka obejmuje teraz takze produkty bez ani jednej specyfikacji. Kolejnosc:
-- najpierw produkty bez kategorii (nigdy nie pobrane szczegoly), potem od najdawniej
-- odswiezanych - przez co produkty, ktore u dostawcy po prostu nie maja parametrow,
-- nie blokuja porcji na stale, tylko wracaja raz na obrot katalogu.

-- Kolumna pochodzi z migracji Inter Cars, ale ta migracja moze byc w danej bazie jeszcze
-- niezastosowana - zakladamy ja warunkowo, zeby procedura powstala niezaleznie od stanu bazy.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.RolmarProducts') AND name = 'DetailsFetchedAt'
)
BEGIN
    ALTER TABLE dbo.RolmarProducts ADD DetailsFetchedAt DATETIME2 NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.RolmarProducts') AND name = 'ArchivedAt'
)
BEGIN
    ALTER TABLE dbo.RolmarProducts ADD ArchivedAt DATETIME2 NULL;
END
GO

-- Specyfikacje czytamy i podmieniamy po ProductId, a tabela nie miala na nim indeksu.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ProductSpecifications_ProductId' AND object_id = OBJECT_ID('dbo.ProductSpecifications')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ProductSpecifications_ProductId
        ON dbo.ProductSpecifications (ProductId);
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetForDetailUpdate
    @Limit INT,
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Take INT = CASE WHEN @Limit > 0 THEN @Limit ELSE 2147483647 END;

    SELECT TOP (@Take) p.IntegrationId
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.ArchivedAt IS NULL
      AND (
            NOT EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
         OR NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications ps WHERE ps.ProductId = p.Id)
          )
    ORDER BY
        CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id) THEN 0 ELSE 1 END,
        CASE WHEN p.DetailsFetchedAt IS NULL THEN 0 ELSE 1 END,
        p.DetailsFetchedAt,
        p.IntegrationId;
END
GO
