-- Spirale, sita i tłumiki to towary gabarytowe (dłużyce, duże elementy), nawet gdy dostawca podaje
-- dla nich zwykły typ dostawy. Zapis produktu podnosi wtedy DeliveryType z 0 na 1 - taki produkt dostaje
-- na Allegro największy cennik (i dopłatę gabarytową), a do InPost nie trafia (tam tylko DeliveryType = 0).
-- Treść procedur jak w migracjach 27 (UpsertBatch) i 15 (Upsert), zmieniona tylko kolumna DeliveryType.

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UpsertBatch
    @Products dbo.RolmarProductUpsertType READONLY,
    @MaxPriceDropPercent DECIMAL(5, 2) = 0
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.RolmarProducts AS target
    USING
    (
        SELECT
            p.Code,
            LEFT(p.Name,
                CASE
                    WHEN LEN(p.Name) <= 75 THEN LEN(p.Name)
                    ELSE 75 - CHARINDEX(' ', REVERSE(LEFT(p.Name, 75))) + 1
                END) AS Name,
            p.SupplierLogo,
            p.SupplierName,
            p.Description,
            p.CustomerCode,
            p.Ean,
            p.InStock,
            p.Weight,
            NULLIF(p.Fits, '') AS Fits,
            p.Unit,
            p.CurrencyPrice,
            NULLIF(p.Substitutes, '') AS Substitutes,
            p.IntegrationCompany,
            p.IntegrationId,
            CASE
                WHEN (p.Name LIKE N'%spirala%' OR p.Name LIKE N'%sito%' OR p.Name LIKE N'%tłumik%')
                     AND ISNULL(p.DeliveryType, 0) = 0
                THEN 1
                ELSE p.DeliveryType
            END AS DeliveryType,
            p.PriceNet,
            p.PriceGross,
            p.Package,
            p.BlockedReturn,
            NULLIF(LTRIM(RTRIM(p.ShippingClass)), '') AS ShippingClass
        FROM @Products p
    ) AS source
    ON target.Code = source.Code AND target.IntegrationCompany = source.IntegrationCompany
    WHEN MATCHED AND
    (
        ISNULL(target.Name, '') <> ISNULL(source.Name, '') OR
        ISNULL(target.SupplierLogo, '') <> ISNULL(source.SupplierLogo, '') OR
        ISNULL(target.SupplierName, '') <> ISNULL(source.SupplierName, '') OR
        ISNULL(target.Description, '') <> ISNULL(source.Description, '') OR
        ISNULL(target.CustomerCode, '') <> ISNULL(source.CustomerCode, '') OR
        ISNULL(target.Ean, '') <> ISNULL(source.Ean, '') OR
        (source.InStock IS NOT NULL AND ISNULL(target.InStock, 0) <> source.InStock) OR
        ISNULL(target.Weight, 0) <> ISNULL(source.Weight, 0) OR
        ISNULL(target.Fits, '') <> ISNULL(source.Fits, '') OR
        ISNULL(target.Unit, '') <> ISNULL(source.Unit, '') OR
        ISNULL(target.CurrencyPrice, '') <> ISNULL(source.CurrencyPrice, '') OR
        ISNULL(target.Substitutes, '') <> ISNULL(source.Substitutes, '') OR
        ISNULL(target.IntegrationId, 0) <> ISNULL(source.IntegrationId, 0) OR
        ISNULL(target.DeliveryType, 0) <> ISNULL(source.DeliveryType, 0) OR
        ISNULL(target.PriceNet, 0) <> ISNULL(source.PriceNet, 0) OR
        ISNULL(target.PriceGross, 0) <> ISNULL(source.PriceGross, 0) OR
        ISNULL(target.Package, 0) <> ISNULL(source.Package, 0) OR
        target.BlockedReturn <> source.BlockedReturn OR
        ISNULL(target.ShippingClass, '') <> ISNULL(source.ShippingClass, '') OR
        target.AcceptedPriceGross IS NULL
    ) THEN
        UPDATE SET
            Name = source.Name,
            SupplierLogo = source.SupplierLogo,
            SupplierName = source.SupplierName,
            Description = source.Description,
            CustomerCode = source.CustomerCode,
            Ean = source.Ean,
            InStock = COALESCE(source.InStock, target.InStock),
            Weight = source.Weight,
            Fits = source.Fits,
            Unit = source.Unit,
            CurrencyPrice = source.CurrencyPrice,
            Substitutes = source.Substitutes,
            IntegrationId = source.IntegrationId,
            DeliveryType = source.DeliveryType,
            PriceNet = source.PriceNet,
            PriceGross = source.PriceGross,
            Package = source.Package,
            BlockedReturn = source.BlockedReturn,
            ShippingClass = source.ShippingClass,
            AcceptedPriceGross = CASE
                WHEN @MaxPriceDropPercent <= 0
                  OR target.AcceptedPriceGross IS NULL
                  OR target.AcceptedPriceGross <= 0
                  OR source.PriceGross >= target.AcceptedPriceGross * (1 - @MaxPriceDropPercent / 100.0)
                THEN source.PriceGross
                ELSE target.AcceptedPriceGross
            END,
            PriceDropDetectedAt = CASE
                WHEN @MaxPriceDropPercent <= 0
                  OR target.AcceptedPriceGross IS NULL
                  OR target.AcceptedPriceGross <= 0
                  OR source.PriceGross >= target.AcceptedPriceGross * (1 - @MaxPriceDropPercent / 100.0)
                THEN NULL
                ELSE ISNULL(target.PriceDropDetectedAt, SYSUTCDATETIME())
            END,
            UpdatedDate = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            Code, Name, SupplierLogo, SupplierName, Description, CustomerCode, Ean,
            InStock, Weight, Fits, Unit, CurrencyPrice, Substitutes,
            IntegrationCompany, IntegrationId, DeliveryType,
            PriceNet, PriceGross, AcceptedPriceGross, Package, BlockedReturn, ShippingClass, CreatedDate, UpdatedDate
        )
        VALUES
        (
            source.Code, source.Name, source.SupplierLogo, source.SupplierName, source.Description, source.CustomerCode, source.Ean,
            ISNULL(source.InStock, 0), source.Weight, source.Fits, source.Unit, source.CurrencyPrice, source.Substitutes,
            source.IntegrationCompany, source.IntegrationId, source.DeliveryType,
            source.PriceNet, source.PriceGross, source.PriceGross, source.Package, source.BlockedReturn, source.ShippingClass, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_Upsert
    @Code NVARCHAR(255),
    @Name NVARCHAR(255),
    @SupplierLogo NVARCHAR(255) = NULL,
    @SupplierName NVARCHAR(255) = NULL,
    @Description NVARCHAR(MAX) = NULL,
    @CustomerCode NVARCHAR(255) = NULL,
    @Ean NVARCHAR(50) = NULL,
    @InStock FLOAT = 0,
    @Weight FLOAT,
    @Fits NVARCHAR(MAX) = NULL,
    @Unit NVARCHAR(50),
    @Currency NVARCHAR(50) = NULL,
    @Substitutes NVARCHAR(MAX) = NULL,
    @IntegrationCompany INT,
    @IntegrationId INT = NULL,
    @DeliveryType INT = 0,
    @PriceNet DECIMAL(18, 2),
    @PriceGross DECIMAL(18, 2),
    @Package DECIMAL(18, 2),
    @MaxPriceDropPercent DECIMAL(5, 2) = 0,
    @BlockedReturn BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Result TABLE (Id INT NOT NULL);

    MERGE dbo.RolmarProducts AS target
    USING
    (
        SELECT
            @Code AS Code,
            LEFT(@Name,
                CASE
                    WHEN LEN(@Name) <= 75 THEN LEN(@Name)
                    ELSE 75 - CHARINDEX(' ', REVERSE(LEFT(@Name, 75))) + 1
                END) AS Name,
            @SupplierLogo AS SupplierLogo,
            @SupplierName AS SupplierName,
            @Description AS Description,
            @CustomerCode AS CustomerCode,
            @Ean AS Ean,
            @InStock AS InStock,
            @Weight AS Weight,
            NULLIF(@Fits, '') AS Fits,
            @Unit AS Unit,
            @Currency AS CurrencyPrice,
            NULLIF(@Substitutes, '') AS Substitutes,
            @IntegrationCompany AS IntegrationCompany,
            @IntegrationId AS IntegrationId,
            CASE
                WHEN (@Name LIKE N'%spirala%' OR @Name LIKE N'%sito%' OR @Name LIKE N'%tłumik%')
                     AND ISNULL(@DeliveryType, 0) = 0
                THEN 1
                ELSE @DeliveryType
            END AS DeliveryType,
            @PriceNet AS PriceNet,
            @PriceGross AS PriceGross,
            @Package AS Package,
            @BlockedReturn AS BlockedReturn
    ) AS source
    ON target.Code = source.Code AND target.IntegrationCompany = source.IntegrationCompany
    WHEN MATCHED AND
    (
        ISNULL(target.Name, '') <> ISNULL(source.Name, '') OR
        ISNULL(target.SupplierLogo, '') <> ISNULL(source.SupplierLogo, '') OR
        ISNULL(target.SupplierName, '') <> ISNULL(source.SupplierName, '') OR
        ISNULL(target.Description, '') <> ISNULL(source.Description, '') OR
        ISNULL(target.CustomerCode, '') <> ISNULL(source.CustomerCode, '') OR
        ISNULL(target.Ean, '') <> ISNULL(source.Ean, '') OR
        ISNULL(target.InStock, 0) <> ISNULL(source.InStock, 0) OR
        ISNULL(target.Weight, 0) <> ISNULL(source.Weight, 0) OR
        ISNULL(target.Fits, '') <> ISNULL(source.Fits, '') OR
        ISNULL(target.Unit, '') <> ISNULL(source.Unit, '') OR
        ISNULL(target.CurrencyPrice, '') <> ISNULL(source.CurrencyPrice, '') OR
        ISNULL(target.Substitutes, '') <> ISNULL(source.Substitutes, '') OR
        ISNULL(target.IntegrationId, 0) <> ISNULL(source.IntegrationId, 0) OR
        ISNULL(target.DeliveryType, 0) <> ISNULL(source.DeliveryType, 0) OR
        ISNULL(target.PriceNet, 0) <> ISNULL(source.PriceNet, 0) OR
        ISNULL(target.PriceGross, 0) <> ISNULL(source.PriceGross, 0) OR
        ISNULL(target.Package, 0) <> ISNULL(source.Package, 0) OR
        target.BlockedReturn <> source.BlockedReturn OR
        target.AcceptedPriceGross IS NULL
    ) THEN
        UPDATE SET
            Name = source.Name,
            SupplierLogo = source.SupplierLogo,
            SupplierName = source.SupplierName,
            Description = source.Description,
            CustomerCode = source.CustomerCode,
            Ean = source.Ean,
            InStock = source.InStock,
            Weight = source.Weight,
            Fits = source.Fits,
            Unit = source.Unit,
            CurrencyPrice = source.CurrencyPrice,
            Substitutes = source.Substitutes,
            IntegrationId = source.IntegrationId,
            DeliveryType = source.DeliveryType,
            PriceNet = source.PriceNet,
            PriceGross = source.PriceGross,
            Package = source.Package,
            BlockedReturn = source.BlockedReturn,
            AcceptedPriceGross = CASE
                WHEN @MaxPriceDropPercent <= 0
                  OR target.AcceptedPriceGross IS NULL
                  OR target.AcceptedPriceGross <= 0
                  OR source.PriceGross >= target.AcceptedPriceGross * (1 - @MaxPriceDropPercent / 100.0)
                THEN source.PriceGross
                ELSE target.AcceptedPriceGross
            END,
            PriceDropDetectedAt = CASE
                WHEN @MaxPriceDropPercent <= 0
                  OR target.AcceptedPriceGross IS NULL
                  OR target.AcceptedPriceGross <= 0
                  OR source.PriceGross >= target.AcceptedPriceGross * (1 - @MaxPriceDropPercent / 100.0)
                THEN NULL
                ELSE ISNULL(target.PriceDropDetectedAt, SYSUTCDATETIME())
            END,
            UpdatedDate = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            Code, Name, SupplierLogo, SupplierName, Description, CustomerCode, Ean,
            InStock, Weight, Fits, Unit, CurrencyPrice, Substitutes,
            IntegrationCompany, IntegrationId, DeliveryType,
            PriceNet, PriceGross, AcceptedPriceGross, Package, BlockedReturn, CreatedDate, UpdatedDate
        )
        VALUES
        (
            source.Code, source.Name, source.SupplierLogo, source.SupplierName, source.Description, source.CustomerCode, source.Ean,
            source.InStock, source.Weight, source.Fits, source.Unit, source.CurrencyPrice, source.Substitutes,
            source.IntegrationCompany, source.IntegrationId, source.DeliveryType,
            source.PriceNet, source.PriceGross, source.PriceGross, source.Package, source.BlockedReturn, SYSUTCDATETIME(), SYSUTCDATETIME()
        )
    OUTPUT inserted.Id INTO @Result (Id);

    -- Gdy nic sie nie zmienilo, MERGE nie wykonuje zadnej akcji - wtedy zwracamy istniejacy wiersz.
    IF NOT EXISTS (SELECT 1 FROM @Result)
    BEGIN
        INSERT INTO @Result (Id)
        SELECT TOP (1) Id
        FROM dbo.RolmarProducts
        WHERE Code = @Code
          AND IntegrationCompany = @IntegrationCompany
        ORDER BY Id DESC;
    END

    SELECT TOP (1) Id FROM @Result;
END
GO

-- Istniejące produkty od razu, bez czekania na zmianę danych u dostawcy.
UPDATE dbo.RolmarProducts
SET DeliveryType = 1,
    UpdatedDate = SYSUTCDATETIME()
WHERE ISNULL(DeliveryType, 0) = 0
  AND (Name LIKE N'%spirala%' OR Name LIKE N'%sito%' OR Name LIKE N'%tłumik%');
GO

-- ============================================================ InPost: tylko DeliveryType = 0
-- Treść jak w migracji 27, dopisany warunek na DeliveryType. Oferty InPost produktów, które przestały
-- spełniać warunek, serwis zamyka (z bezpiecznikiem masowego zamykania).

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
      -- Do InPost tylko zwykłe paczki: gabaryty i towary niestandardowe (DeliveryType <> 0) zostają na Allegro.
      AND ISNULL(p.DeliveryType, 0) = 0
      AND (@OfferIds IS NULL OR o.Id IN (SELECT [value] FROM OPENJSON(@OfferIds)));

    SELECT
        o.Id, o.ExternalId, o.Name, o.CategoryId, o.Price, o.Stock, o.Status, o.DeliveryName,
        o.ProductId AS AllegroProductId, o.Images, o.Weight, o.HandlingTime,
        o.ResponsibleProducer, o.ResponsiblePerson, o.VisitsCount,
        p.Id AS ProductId, p.Code AS ProductCode, p.Name AS ProductName, p.Description AS ProductDescription,
        p.Ean, p.Weight AS ProductWeight, p.Fits, p.Substitutes, p.InStock, p.Unit,
        p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DeliveryType, p.ArchivedAt, p.Package, p.ShippingClass, p.SupplierName,
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
