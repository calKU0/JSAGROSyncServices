-- Jedno drzewo kategorii dostawcow zamiast trzech roznych zapisow:
--   * RolmarCategory            - sciezka tekstowa per produkt (Rolmar i czesc Gaski),
--   * ProductSupplierCategories - id skonfigurowanej kategorii Gaski per produkt.
--
-- SupplierCategories  - wezly drzewa (Gaska: id z API /categories, Rolmar: znormalizowana sciezka).
-- ProductCategories   - przypisanie produktu do kategorii; podkategorie wyznacza drzewo.
--                       Zapisywane wylacznie razem ze szczegolami produktu, wiec - tak jak dotad
--                       RolmarCategory - jest jednoczesnie znacznikiem "szczegoly pobrane".
--
-- Skrypt jest wykonywany w jednej transakcji (DbUp: transakcja per skrypt) i sam sprawdza,
-- czy dane zostaly przeniesione, zanim usunie stare tabele.

-- ============================================================ tabele

CREATE TABLE dbo.SupplierCategories
(
    Id                 INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_SupplierCategories PRIMARY KEY,
    IntegrationCompany INT            NOT NULL,
    SourceKey          NVARCHAR(255)  NOT NULL,   -- Gaska: id kategorii z API; Rolmar: sciezka bez spacji wokol '>'
    ParentId           INT            NULL CONSTRAINT FK_SupplierCategories_Parent REFERENCES dbo.SupplierCategories (Id),
    Name               NVARCHAR(255)  NOT NULL,
    Path               NVARCHAR(1000) NOT NULL,   -- sciezka do wyswietlania ("A > B > C")
    Depth              INT            NOT NULL CONSTRAINT DF_SupplierCategories_Depth DEFAULT 0,
    UpdatedAt          DATETIME2      NOT NULL CONSTRAINT DF_SupplierCategories_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SupplierCategories_Source UNIQUE (IntegrationCompany, SourceKey)
);

CREATE INDEX IX_SupplierCategories_Parent ON dbo.SupplierCategories (ParentId);

CREATE TABLE dbo.ProductCategories
(
    ProductId  INT NOT NULL CONSTRAINT FK_ProductCategories_Product  REFERENCES dbo.RolmarProducts (Id) ON DELETE CASCADE,
    CategoryId INT NOT NULL CONSTRAINT FK_ProductCategories_Category REFERENCES dbo.SupplierCategories (Id),
    CONSTRAINT PK_ProductCategories PRIMARY KEY (ProductId, CategoryId)
);

CREATE INDEX IX_ProductCategories_Category ON dbo.ProductCategories (CategoryId) INCLUDE (ProductId);

CREATE TYPE dbo.SupplierCategoryNodeType AS TABLE
(
    SourceKey       NVARCHAR(255) NOT NULL PRIMARY KEY,
    ParentSourceKey NVARCHAR(255) NULL,
    Name            NVARCHAR(255) NOT NULL
);

CREATE TYPE dbo.ProductCategoryLinkType AS TABLE
(
    Code              NVARCHAR(255) NOT NULL,
    CategorySourceKey NVARCHAR(255) NOT NULL
);
GO

-- ============================================================ procedury drzewa

-- Przelicza sciezki i glebokosc wezlow firmy od korzeni w dol.
CREATE OR ALTER PROCEDURE dbo.SupplierCategories_RebuildPaths
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Tree AS
    (
        SELECT sc.Id, CAST(sc.Name AS NVARCHAR(1000)) AS Path, 0 AS Depth
        FROM dbo.SupplierCategories sc
        WHERE sc.IntegrationCompany = @IntegrationCompany AND sc.ParentId IS NULL

        UNION ALL

        SELECT ch.Id, CAST(t.Path + N' > ' + ch.Name AS NVARCHAR(1000)), t.Depth + 1
        FROM dbo.SupplierCategories ch
        JOIN Tree t ON ch.ParentId = t.Id
        WHERE t.Depth < 30   -- ochrona przed cyklem w danych od dostawcy
    )
    UPDATE sc
    SET sc.Path = t.Path, sc.Depth = t.Depth
    FROM dbo.SupplierCategories sc
    JOIN Tree t ON t.Id = sc.Id
    WHERE sc.Path <> t.Path OR sc.Depth <> t.Depth;
END
GO

-- Dodaje i aktualizuje wezly drzewa. Wezly znikniete u dostawcy zostaja -
-- moga byc wciaz wskazane w konfiguracji lub przypisaniach produktow.
CREATE OR ALTER PROCEDURE dbo.SupplierCategories_Upsert
    @IntegrationCompany INT,
    @Nodes dbo.SupplierCategoryNodeType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.SupplierCategories AS target
    USING @Nodes AS source
       ON target.IntegrationCompany = @IntegrationCompany
      AND target.SourceKey = source.SourceKey
    WHEN MATCHED AND target.Name <> source.Name THEN
        UPDATE SET Name = source.Name, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (IntegrationCompany, SourceKey, Name, Path)
        VALUES (@IntegrationCompany, source.SourceKey, source.Name, source.Name);

    UPDATE child
    SET child.ParentId = parent.Id
    FROM dbo.SupplierCategories child
    JOIN @Nodes n ON n.SourceKey = child.SourceKey
    LEFT JOIN dbo.SupplierCategories parent
           ON parent.IntegrationCompany = @IntegrationCompany
          AND parent.SourceKey = n.ParentSourceKey
    WHERE child.IntegrationCompany = @IntegrationCompany
      AND ISNULL(child.ParentId, -1) <> ISNULL(parent.Id, -1);

    EXEC dbo.SupplierCategories_RebuildPaths @IntegrationCompany;
END
GO

-- Zastepuje przypisania kategorii dla podanych produktow (po kodzie).
-- Dla klucza spoza drzewa zaklada wezel tymczasowy - nazwe uzupelni najblizsza synchronizacja
-- drzewa. Inaczej nieudane pobranie drzewa kasowaloby przypisania, a z nimi informacje
-- o pobranych szczegolach produktu.
CREATE OR ALTER PROCEDURE dbo.ProductCategories_ReplaceByCodes
    @IntegrationCompany INT,
    @Codes dbo.ProductCodeType READONLY,
    @Links dbo.ProductCategoryLinkType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Products TABLE (Id INT NOT NULL PRIMARY KEY, Code NVARCHAR(255) NOT NULL);

    INSERT INTO @Products (Id, Code)
    SELECT p.Id, p.Code
    FROM dbo.RolmarProducts p
    JOIN (SELECT DISTINCT Code FROM @Codes) c ON c.Code = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany;

    DELETE pc
    FROM dbo.ProductCategories pc
    JOIN @Products p ON p.Id = pc.ProductId;

    INSERT INTO dbo.SupplierCategories (IntegrationCompany, SourceKey, Name, Path)
    SELECT DISTINCT @IntegrationCompany, l.CategorySourceKey, N'Kategoria ' + l.CategorySourceKey, N'Kategoria ' + l.CategorySourceKey
    FROM @Links l
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.SupplierCategories sc
        WHERE sc.IntegrationCompany = @IntegrationCompany AND sc.SourceKey = l.CategorySourceKey);

    INSERT INTO dbo.ProductCategories (ProductId, CategoryId)
    SELECT DISTINCT p.Id, sc.Id
    FROM @Links l
    JOIN @Products p ON p.Code = l.Code
    JOIN dbo.SupplierCategories sc
      ON sc.IntegrationCompany = @IntegrationCompany
     AND sc.SourceKey = l.CategorySourceKey;
END
GO

CREATE OR ALTER PROCEDURE dbo.SupplierCategories_GetByCompany
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT SourceKey, Path, Depth
    FROM dbo.SupplierCategories
    WHERE IntegrationCompany = @IntegrationCompany
    ORDER BY Path;
END
GO

-- ============================================================ przeniesienie danych

-- Rolmar: sciezki tekstowe -> wezly dla kazdego prefiksu sciezki + przypisanie do liscia.
-- Przycinanie musi byc identyczne jak string.Trim() w C# (takze tabulatory) - inaczej
-- pierwsza synchronizacja z serwisu utworzylaby dla tej samej kategorii drugi wezel.
DECLARE @RolmarPaths TABLE (Path NVARCHAR(255) NOT NULL PRIMARY KEY);

INSERT INTO @RolmarPaths (Path)
SELECT DISTINCT REPLACE(REPLACE(TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM rc.Name), N' >', N'>'), N'> ', N'>')
FROM dbo.RolmarCategory rc
JOIN dbo.RolmarProducts p ON p.Id = rc.ProductId AND p.IntegrationCompany = 1
WHERE TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM rc.Name) <> N'';

-- Wszystkie prefiksy: "A>B>C" daje "A", "A>B", "A>B>C".
WITH Prefixes AS
(
    SELECT Path AS FullPath, CHARINDEX(N'>', Path) AS Pos
    FROM @RolmarPaths

    UNION ALL

    SELECT FullPath, CHARINDEX(N'>', FullPath, Pos + 1)
    FROM Prefixes
    WHERE Pos > 0
),
Keys AS
(
    SELECT DISTINCT CASE WHEN Pos = 0 THEN FullPath ELSE LEFT(FullPath, Pos - 1) END AS SourceKey
    FROM Prefixes
)
INSERT INTO dbo.SupplierCategories (IntegrationCompany, SourceKey, Name, Path)
SELECT 1,
       k.SourceKey,
       -- nazwa = ostatni segment sciezki
       RIGHT(k.SourceKey, CHARINDEX(N'>', REVERSE(k.SourceKey) + N'>') - 1),
       k.SourceKey
FROM Keys k;

UPDATE child
SET child.ParentId = parent.Id
FROM dbo.SupplierCategories child
JOIN dbo.SupplierCategories parent
  ON parent.IntegrationCompany = 1
 AND parent.SourceKey = LEFT(child.SourceKey, LEN(child.SourceKey) - CHARINDEX(N'>', REVERSE(child.SourceKey)))
WHERE child.IntegrationCompany = 1
  AND CHARINDEX(N'>', child.SourceKey) > 0;

EXEC dbo.SupplierCategories_RebuildPaths 1;

INSERT INTO dbo.ProductCategories (ProductId, CategoryId)
SELECT DISTINCT rc.ProductId, sc.Id
FROM dbo.RolmarCategory rc
JOIN dbo.RolmarProducts p ON p.Id = rc.ProductId AND p.IntegrationCompany = 1
JOIN dbo.SupplierCategories sc
  ON sc.IntegrationCompany = 1
 AND sc.SourceKey = REPLACE(REPLACE(TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM rc.Name), N' >', N'>'), N'> ', N'>');

-- Gaska: wezly dla kategorii uzywanych dotad przez filtr (po id).
-- Nazwy i rodzicow uzupelni pierwsza synchronizacja drzewa z API /categories.
INSERT INTO dbo.SupplierCategories (IntegrationCompany, SourceKey, Name, Path)
SELECT DISTINCT 2,
       CAST(psc.CategoryId AS NVARCHAR(255)),
       N'Kategoria ' + CAST(psc.CategoryId AS NVARCHAR(20)),
       N'Kategoria ' + CAST(psc.CategoryId AS NVARCHAR(20))
FROM dbo.ProductSupplierCategories psc;

-- Przypisania tylko dla produktow, ktore maja pobrane szczegoly (czyli wiersz w RolmarCategory).
-- Brak przypisania = szczegoly do pobrania, dokladnie jak przed ta migracja. Gdybysmy przenosili
-- wszystkie wiersze z listy produktow, przypisanie przestaloby cokolwiek mowic o szczegolach
-- i do wystawienia trafilyby produkty bez zdjec i opisu.
INSERT INTO dbo.ProductCategories (ProductId, CategoryId)
SELECT DISTINCT psc.ProductId, sc.Id
FROM dbo.ProductSupplierCategories psc
JOIN dbo.SupplierCategories sc
  ON sc.IntegrationCompany = 2
 AND sc.SourceKey = CAST(psc.CategoryId AS NVARCHAR(255))
WHERE EXISTS (SELECT 1 FROM dbo.RolmarCategory rc WHERE rc.ProductId = psc.ProductId);

-- ============================================================ kontrola przed usunieciem starych tabel

DECLARE @OldRolmarProducts INT = (
    SELECT COUNT(DISTINCT rc.ProductId)
    FROM dbo.RolmarCategory rc JOIN dbo.RolmarProducts p ON p.Id = rc.ProductId AND p.IntegrationCompany = 1
    WHERE TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM rc.Name) <> N'');

DECLARE @NewRolmarProducts INT = (
    SELECT COUNT(DISTINCT pc.ProductId)
    FROM dbo.ProductCategories pc JOIN dbo.SupplierCategories sc ON sc.Id = pc.CategoryId AND sc.IntegrationCompany = 1);

-- Gaska: przenosimy przypisania tylko produktow ze szczegolami, wiec tyle samo musi byc po migracji.
DECLARE @OldGaskaProducts INT = (
    SELECT COUNT(DISTINCT psc.ProductId)
    FROM dbo.ProductSupplierCategories psc
    WHERE EXISTS (SELECT 1 FROM dbo.RolmarCategory rc WHERE rc.ProductId = psc.ProductId));

DECLARE @NewGaskaProducts INT = (
    SELECT COUNT(DISTINCT pc.ProductId)
    FROM dbo.ProductCategories pc JOIN dbo.SupplierCategories sc ON sc.Id = pc.CategoryId AND sc.IntegrationCompany = 2);

IF @OldRolmarProducts <> @NewRolmarProducts OR @OldGaskaProducts <> @NewGaskaProducts
BEGIN
    DECLARE @Msg NVARCHAR(400) = CONCAT(
        N'Przeniesienie kategorii niekompletne. Rolmar: ', @OldRolmarProducts, N' -> ', @NewRolmarProducts,
        N', Gaska: ', @OldGaskaProducts, N' -> ', @NewGaskaProducts, N'. Stare tabele pozostaja.');
    THROW 50001, @Msg, 1;
END
GO

-- ============================================================ procedury korzystajace z nowego drzewa

-- Produkty w skonfigurowanych kategoriach (lacznie z podkategoriami) -> #Allowed.
-- Wspolna logika dla tworzenia i konczenia ofert.
CREATE OR ALTER PROCEDURE dbo.ProductCategories_FillAllowed
    @IntegrationCompany INT,
    @Categories NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- Klucze z konfiguracji: Gaska - id, Rolmar - sciezka (spacje wokol '>' bez znaczenia).
    WITH Configured AS
    (
        SELECT DISTINCT REPLACE(REPLACE(TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM [value]), N' >', N'>'), N'> ', N'>') AS SourceKey
        FROM OPENJSON(@Categories)
        WHERE TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM [value]) <> N''
    ),
    Tree AS
    (
        SELECT sc.Id, 0 AS Depth
        FROM dbo.SupplierCategories sc
        JOIN Configured c ON c.SourceKey = sc.SourceKey
        WHERE sc.IntegrationCompany = @IntegrationCompany

        UNION ALL

        SELECT ch.Id, t.Depth + 1
        FROM dbo.SupplierCategories ch
        JOIN Tree t ON ch.ParentId = t.Id
        WHERE t.Depth < 30
    )
    INSERT INTO #Allowed (ProductId)
    SELECT DISTINCT pc.ProductId
    FROM dbo.ProductCategories pc
    JOIN (SELECT DISTINCT Id FROM Tree) t ON t.Id = pc.CategoryId;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpload
    @MinProductStock INT,
    @MinProductPrice DECIMAL(15,4),
    @IntegrationCompany INT,
    @Account INT,
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    DECLARE @HasCategoryFilter BIT =
        CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]'))) THEN 1 ELSE 0 END;

    IF @HasCategoryFilter = 1
        EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

    SELECT
        p.Id, p.Code, p.Name, p.Description, p.Ean, p.Weight, p.Fits, p.SupplierName, p.InStock,
        p.Unit, p.CurrencyPrice, p.PriceNet, p.PriceGross, p.DefaultAllegroCategory, p.Package,
        p.CreatedDate, p.UpdatedDate, p.Substitutes, p.AllegroId, p.DeliveryType,
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
      -- Produkt musi nalezec do skonfigurowanej kategorii. Brak informacji o kategorii
      -- nie wystarcza - Gaska pobiera caly katalog, wiec "nieznane" nie moze znaczyc "dozwolone".
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id))
      -- Kategorie zapisujemy razem ze szczegolami, wiec ich brak znaczy "szczegoly niepobrane":
      -- produkt bez opisu i zdjec nie moze trafic do wystawienia.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
    ORDER BY p.Id;

    DROP TABLE #Allowed;
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

    IF NOT EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]')))
        RETURN;

    IF @DeliveryNames IS NULL OR LTRIM(RTRIM(@DeliveryNames)) = N''
        RETURN;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

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
      AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id)
      -- Konczymy tylko przy pewnosci, ze produkt jest poza kategoriami. Produkt bez
      -- przypisanej kategorii zostawiamy - to brak danych, a nie decyzja o wycofaniu.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id);

    DROP TABLE #Allowed;
END
GO

-- Kolejka pobierania szczegolow: produkty bez przypisanej kategorii, czyli bez szczegolow.
-- Ta sama zasada co wczesniej, tyle ze na nowej tabeli (dawniej RolmarCategory).
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetForDetailUpdate
    @Limit INT,
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit) p.IntegrationId
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
    ORDER BY p.IntegrationId;
END
GO

-- ============================================================ usuniecie starych struktur

DROP PROCEDURE IF EXISTS dbo.RolmarCategory_ReplaceBatch;
DROP PROCEDURE IF EXISTS dbo.RolmarCategory_ReplaceByProductId;
DROP PROCEDURE IF EXISTS dbo.RolmarCategory_DeleteByProductId;
DROP PROCEDURE IF EXISTS dbo.RolmarCategory_Insert;
DROP PROCEDURE IF EXISTS dbo.ProductSupplierCategories_ReplaceByCodes;
GO

DROP TABLE IF EXISTS dbo.RolmarCategory;
DROP TABLE IF EXISTS dbo.ProductSupplierCategories;
GO

DROP TYPE IF EXISTS dbo.ProductCategoryBatchType;
DROP TYPE IF EXISTS dbo.RolmarCategoryType;
DROP TYPE IF EXISTS dbo.ProductSupplierCategoryType;
GO
