-- Nazwa i rodzic kazdej kategorii Allegro, w ktorej stoja nasze oferty (obu kont) - az do korzenia.
-- AllegroCategories trzyma tylko galaz czesci rolniczych; InPost mapuje kategorie po pelnej sciezce
-- nazw, wiec potrzebuje tez kategorii narzedzi, chemii, oswietlenia itd.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AllegroCategoryPaths' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AllegroCategoryPaths
    (
        CategoryId INT NOT NULL,
        Name NVARCHAR(255) NOT NULL,
        ParentId INT NULL,
        Leaf BIT NOT NULL CONSTRAINT DF_AllegroCategoryPaths_Leaf DEFAULT (0),
        UpdatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_AllegroCategoryPaths_UpdatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_AllegroCategoryPaths PRIMARY KEY CLUSTERED (CategoryId)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroCategoryPaths_GetMissing
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DISTINCT o.CategoryId
    FROM dbo.AllegroOffers o
    WHERE o.CategoryId IS NOT NULL
      AND o.CategoryId <> 0
      AND NOT EXISTS (SELECT 1 FROM dbo.AllegroCategoryPaths p WHERE p.CategoryId = o.CategoryId);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroCategoryPaths_Upsert
    @CategoryId INT,
    @Name NVARCHAR(255),
    @ParentId INT = NULL,
    @Leaf BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AllegroCategoryPaths
    SET Name = @Name, ParentId = @ParentId, Leaf = @Leaf, UpdatedAt = SYSUTCDATETIME()
    WHERE CategoryId = @CategoryId;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.AllegroCategoryPaths (CategoryId, Name, ParentId, Leaf)
        VALUES (@CategoryId, @Name, @ParentId, @Leaf);
END
GO

-- Kandydaci InPost: cenniki dostawy podawane razem z dostawca, ktorego produkty za nimi stoja
-- (cenniki Rolmar -> produkty Rolmar, w przyszlosci cenniki Gaski -> produkty Gaski).
-- @DeliveryLists: [{"name":"Mały Asortyment Rolmar","company":1}, ...]
CREATE OR ALTER PROCEDURE dbo.InPostOffers_GetCandidates
    @Account INT,
    @DeliveryLists NVARCHAR(MAX),
    @OfferIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Lists
    (
        Name NVARCHAR(400) NOT NULL,
        Company INT NOT NULL
    );

    INSERT INTO #Lists (Name, Company)
    SELECT Name, Company
    FROM OPENJSON(@DeliveryLists) WITH (Name NVARCHAR(400) '$.name', Company INT '$.company');

    CREATE TABLE #Offers
    (
        OfferId NVARCHAR(128) NOT NULL PRIMARY KEY,
        ProductId INT NOT NULL,
        CategoryId INT NOT NULL
    );

    INSERT INTO #Offers (OfferId, ProductId, CategoryId)
    SELECT o.Id, p.Id, o.CategoryId
    FROM dbo.AllegroOffers o
    JOIN #Lists l ON l.Name = o.DeliveryName
    JOIN dbo.RolmarProducts p
        ON p.Code = o.ExternalId
       AND p.IntegrationCompany = l.Company
    WHERE o.Account = @Account
      AND o.Status = 'ACTIVE'
      AND (@OfferIds IS NULL OR o.Id IN (SELECT [value] FROM OPENJSON(@OfferIds)));

    SELECT
        o.Id, o.ExternalId, o.Name, o.CategoryId, o.Price, o.Stock, o.Status, o.DeliveryName,
        o.ProductId AS AllegroProductId, o.Images, o.Weight, o.HandlingTime,
        o.ResponsibleProducer, o.ResponsiblePerson, o.VisitsCount,
        p.Id AS ProductId, p.Code AS ProductCode, p.Name AS ProductName, p.Description AS ProductDescription,
        p.Ean, p.Weight AS ProductWeight, p.Fits, p.Substitutes, p.InStock, p.Unit,
        p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DeliveryType, p.ArchivedAt, p.Package,
        i.InPostOfferId, i.LastCommandId, i.Status AS InPostStatus, i.Price AS InPostPrice, i.Stock AS InPostStock
    FROM #Offers t
    JOIN dbo.AllegroOffers o ON o.Id = t.OfferId
    JOIN dbo.RolmarProducts p ON p.Id = t.ProductId
    LEFT JOIN dbo.InPostOffers i ON i.AllegroOfferId = o.Id;

    SELECT d.OfferId, d.SectionId, d.Type, d.Content
    FROM dbo.AllegroOfferDescriptions d
    JOIN #Offers t ON t.OfferId = d.OfferId
    ORDER BY d.OfferId, d.SectionId, d.Id;

    SELECT a.OfferId, cp.Name, a.Type, a.ValuesJson
    FROM dbo.AllegroOfferAttributes a
    JOIN #Offers t ON t.OfferId = a.OfferId
    JOIN dbo.CategoryParameters cp
        ON cp.ParameterId = TRY_CAST(a.AttributeId AS INT)
       AND cp.CategoryId = t.CategoryId;

    SELECT pp.ProductId, cp.Name, pp.Value
    FROM dbo.RolmarProductParameters pp
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = pp.ProductId
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    WHERE NULLIF(LTRIM(RTRIM(pp.Value)), '') IS NOT NULL;

    SELECT s.ProductId, s.Name, s.Value, s.UnitName
    FROM dbo.ProductSpecifications s
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = s.ProductId;

    SELECT pc.ProductId, sc.SourceKey
    FROM dbo.ProductCategories pc
    JOIN (SELECT DISTINCT ProductId FROM #Offers) t ON t.ProductId = pc.ProductId
    JOIN dbo.SupplierCategories sc ON sc.Id = pc.CategoryId;
END
GO
