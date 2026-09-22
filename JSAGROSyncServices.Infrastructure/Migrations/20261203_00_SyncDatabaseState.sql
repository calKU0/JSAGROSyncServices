-- Doprowadzenie migracji do stanu faktycznej bazy.
--
-- 1) Tabele ponizej powstaly poza DbUp (pierwotnie przez Entity Framework - w bazie zostal slad
--    w postaci tabeli __MigrationHistory), przez co zestaw migracji nie odtwarzal dzialajacej bazy
--    od zera. Skrypty sa napisane defensywnie (IF NOT EXISTS), wiec na istniejacej bazie nic nie robia.
-- 2) Procedury ponizej byly poprawiane recznie na bazie i ich tresc rozjechala sie z migracjami.
--    Wersje w tym pliku to definicje pobrane z dzialajacej bazy (stan 2026-09-17).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AllegroOffers' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AllegroOffers
    (
        Id NVARCHAR(128) NOT NULL,
        ExternalId NVARCHAR(MAX) NULL,
        Name NVARCHAR(MAX) NULL,
        CategoryId INT NOT NULL,
        Price DECIMAL(18,2) NOT NULL,
        Stock INT NOT NULL,
        WatchersCount INT NOT NULL,
        VisitsCount INT NOT NULL,
        Status NVARCHAR(MAX) NULL,
        DeliveryName NVARCHAR(MAX) NULL,
        ProductId NVARCHAR(500) NULL,
        StartingAt DATETIME2(7) NOT NULL,
        ExistsInErli BIT NOT NULL CONSTRAINT DF_AllegroOffers_ExistsInErli DEFAULT ((0)),
        Images NVARCHAR(MAX) NULL,
        Weight DECIMAL(18,2) NOT NULL CONSTRAINT DF_AllegroOffers_Weight DEFAULT ((0)),
        HandlingTime NVARCHAR(MAX) NULL,
        ResponsibleProducer NVARCHAR(MAX) NULL,
        ResponsiblePerson NVARCHAR(MAX) NULL,
        Account INT NULL,

        CONSTRAINT [PK_dbo.AllegroOffers] PRIMARY KEY CLUSTERED (Id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AllegroOfferDescriptions' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AllegroOfferDescriptions
    (
        Id INT IDENTITY(1,1) NOT NULL,
        OfferId NVARCHAR(128) NULL,
        Type NVARCHAR(MAX) NULL,
        Content NVARCHAR(MAX) NULL,
        SectionId INT NOT NULL CONSTRAINT DF_AllegroOfferDescriptions_SectionId DEFAULT ((0)),

        CONSTRAINT [PK_dbo.AllegroOfferDescriptions] PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT [FK_dbo.AllegroOfferDescriptions_dbo.AllegroOffers_OfferId]
            FOREIGN KEY (OfferId) REFERENCES dbo.AllegroOffers (Id)
    );

    CREATE NONCLUSTERED INDEX IX_OfferId ON dbo.AllegroOfferDescriptions (OfferId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AllegroOfferAttributes' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AllegroOfferAttributes
    (
        Id INT IDENTITY(1,1) NOT NULL,
        OfferId NVARCHAR(128) NULL,
        AttributeId NVARCHAR(MAX) NULL,
        Type NVARCHAR(MAX) NULL,
        ValuesJson NVARCHAR(MAX) NULL,
        ValuesIdsJson NVARCHAR(MAX) NULL,

        CONSTRAINT [PK_dbo.AllegroOfferAttributes] PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT [FK_dbo.AllegroOfferAttributes_dbo.AllegroOffers_OfferId]
            FOREIGN KEY (OfferId) REFERENCES dbo.AllegroOffers (Id)
    );

    CREATE NONCLUSTERED INDEX IX_OfferId ON dbo.AllegroOfferAttributes (OfferId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AllegroCategories' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AllegroCategories
    (
        Id INT IDENTITY(1,1) NOT NULL,
        CategoryId NVARCHAR(MAX) NULL,
        Name NVARCHAR(MAX) NULL,
        ParentId INT NULL,

        CONSTRAINT [PK_dbo.AllegroCategories] PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT [FK_dbo.AllegroCategories_dbo.AllegroCategories_Parent_Id]
            FOREIGN KEY (ParentId) REFERENCES dbo.AllegroCategories (Id)
    );

    CREATE NONCLUSTERED INDEX IX_ParentId ON dbo.AllegroCategories (ParentId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CategoryParameters' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.CategoryParameters
    (
        Id INT IDENTITY(1,1) NOT NULL,
        CategoryId INT NOT NULL,
        ParameterId INT NOT NULL,
        Name NVARCHAR(MAX) NULL,
        Type NVARCHAR(MAX) NULL,
        Required BIT NOT NULL,
        Min INT NULL,
        Max INT NULL,
        RequiredForProduct BIT NOT NULL CONSTRAINT DF_CategoryParameters_RequiredForProduct DEFAULT ((0)),
        DescribesProduct BIT NOT NULL CONSTRAINT DF_CategoryParameters_DescribesProduct DEFAULT ((0)),
        CustomValuesEnabled BIT NOT NULL CONSTRAINT DF_CategoryParameters_CustomValuesEnabled DEFAULT ((0)),
        AmbiguousValueId NVARCHAR(MAX) NULL,

        CONSTRAINT [PK_dbo.CategoryParameters] PRIMARY KEY CLUSTERED (Id)
    );

    CREATE UNIQUE NONCLUSTERED INDEX IX_ParameterId_CategoryId ON dbo.CategoryParameters (ParameterId, CategoryId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CategoryParameterValues' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.CategoryParameterValues
    (
        Id INT IDENTITY(1,1) NOT NULL,
        CategoryParameterId INT NOT NULL,
        Value NVARCHAR(MAX) NULL,

        CONSTRAINT [PK_dbo.CategoryParameterValues] PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT [FK_dbo.CategoryParameterValues_dbo.CategoryParameters_CategoryParameterId]
            FOREIGN KEY (CategoryParameterId) REFERENCES dbo.CategoryParameters (Id) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_CategoryParameterId ON dbo.CategoryParameterValues (CategoryParameterId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AllegroTokenEntities' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AllegroTokenEntities
    (
        Id INT IDENTITY(1,1) NOT NULL,
        AccessToken NVARCHAR(MAX) NULL,
        RefreshToken NVARCHAR(MAX) NULL,
        ExpiryDateUtc DATETIME NOT NULL,
        TokenName NVARCHAR(MAX) NULL,

        CONSTRAINT [PK_dbo.AllegroTokenEntities] PRIMARY KEY CLUSTERED (Id)
    );
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_Upsert]
    @Offers dbo.AllegroOfferType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. UPDATE only changed rows
    UPDATE target
    SET
        Name = source.Name,
        Account = source.Account,
        CategoryId = source.CategoryId,
        Price = source.Price,
        Stock = source.Stock,
        WatchersCount = source.WatchersCount,
        VisitsCount = source.VisitsCount,
        Status = source.Status,
        DeliveryName = source.DeliveryName,
        StartingAt = source.StartingAt,
        ExternalId = source.ExternalId
    FROM AllegroOffers target
    JOIN @Offers source ON target.Id = source.Id
    WHERE
        -- NULL-safe comparisons
        ISNULL(target.Name, '') <> ISNULL(source.Name, '') OR
        ISNULL(target.Price, -1) <> ISNULL(source.Price, -1) OR
        ISNULL(target.Stock, -1) <> ISNULL(source.Stock, -1) OR
        ISNULL(target.Status, -1) <> ISNULL(source.Status, -1) OR
        ISNULL(target.WatchersCount, -1) <> ISNULL(source.WatchersCount, -1) OR
        ISNULL(target.VisitsCount, -1) <> ISNULL(source.VisitsCount, -1) OR
        ISNULL(target.StartingAt, '19000101') <> ISNULL(source.StartingAt, '19000101') OR
        ISNULL(target.Account, '') <> ISNULL(source.Account, '') OR
        ISNULL(target.CategoryId, -1) <> ISNULL(source.CategoryId, -1) OR
        ISNULL(target.DeliveryName, '') <> ISNULL(source.DeliveryName, '') OR
        ISNULL(target.ExternalId, '') <> ISNULL(source.ExternalId, '');

    -- 2. INSERT new rows
    INSERT INTO AllegroOffers (
        Id,
        Account,
        Name,
        ProductId,
        CategoryId,
        Price,
        Stock,
        WatchersCount,
        VisitsCount,
        Status,
        DeliveryName,
        StartingAt,
        ExternalId
    )
    SELECT
        source.Id,
        source.Account,
        source.Name,
        null,
        source.CategoryId,
        source.Price,
        source.Stock,
        source.WatchersCount,
        source.VisitsCount,
        source.Status,
        source.DeliveryName,
        source.StartingAt,
        source.ExternalId
    FROM @Offers source
    WHERE NOT EXISTS (
        SELECT 1
        FROM AllegroOffers target
        WHERE target.Id = source.Id
    );
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetOffersToUpdate]
    @DeliveryNames NVARCHAR(MAX),
    @IntegrationCompany INT,
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #OffersWithProducts
    (
        OfferId NVARCHAR(255) NOT NULL,
        ExternalId NVARCHAR(255) NULL,
        OfferName NVARCHAR(255) NOT NULL,
        CategoryId INT NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        StartingAt DATETIME2 NOT NULL,
        DeliveryName NVARCHAR(255) NULL,
        ProductId INT NOT NULL,
        AllegroId NVARCHAR(255) NULL,
        Code NVARCHAR(255) NOT NULL,
        ProductName NVARCHAR(255) NOT NULL,
        Description NVARCHAR(MAX) NULL,
        Ean NVARCHAR(50) NULL,
        Weight FLOAT NOT NULL,
        Fits NVARCHAR(MAX) NULL,
        SupplierName NVARCHAR(255) NULL,
        Substitutes NVARCHAR(MAX) NULL,
        InStock FLOAT NOT NULL,
        Unit NVARCHAR(50) NULL,
        CurrencyPrice NVARCHAR(50) NULL,
        PriceNet DECIMAL(18, 2) NOT NULL,
        PriceGross DECIMAL(18, 2) NOT NULL,
        DefaultAllegroCategory INT NOT NULL,
        Package DECIMAL(18, 2) NOT NULL,
        DeliveryType INT NULL,
        CreatedDate DATETIME2 NOT NULL,
        UpdatedDate DATETIME2 NOT NULL,
        OfferPrice DECIMAL(18, 2) NOT NULL
    );

    INSERT INTO #OffersWithProducts
    (
        OfferId, ExternalId, OfferName, CategoryId, Status, StartingAt, DeliveryName,
        ProductId, AllegroId, Code, ProductName, Description, Ean, Weight, Fits, SupplierName,
        Substitutes, InStock, Unit, CurrencyPrice, PriceNet, PriceGross, DefaultAllegroCategory,
        Package, DeliveryType, CreatedDate, UpdatedDate, OfferPrice
    )
    SELECT
        ao.Id, ao.ExternalId, ao.Name, ao.CategoryId, ao.Status, ao.StartingAt, ao.DeliveryName,
        p.Id, p.AllegroId, p.Code, p.Name, p.Description,
        p.Ean, p.Weight, p.Fits, p.SupplierName, p.Substitutes, p.InStock, p.Unit,
        p.CurrencyPrice, p.PriceNet, p.PriceGross, p.DefaultAllegroCategory, p.Package, p.DeliveryType,
        p.CreatedDate, p.UpdatedDate, ao.Price
    FROM AllegroOffers ao
    INNER JOIN RolmarProducts p ON p.Code = ao.ExternalId AND p.IntegrationCompany = @IntegrationCompany
    WHERE ao.Status IN ('ACTIVE', 'ENDED', 'INACTIVE') and Account = @Account 
        AND ao.DeliveryName IN (SELECT value FROM STRING_SPLIT(@DeliveryNames, ','))
        AND p.PriceNet > 0;

    SELECT
        OfferId AS Id,
        ExternalId,
        OfferName AS Name,
        CategoryId,
        Status,
        StartingAt,
        DeliveryName,
        OfferPrice as Price,
        ProductId as Id,
        AllegroId,
        Code,
        ProductName AS Name,
        Description,
        Ean,
        Weight,
        Fits,
        SupplierName,
        Substitutes,
        InStock,
        Unit,
        CurrencyPrice,
        PriceNet,
        PriceGross,
        DefaultAllegroCategory,
        Package,
        DeliveryType,
        CreatedDate,
        UpdatedDate
    FROM #OffersWithProducts;

    SELECT ai.*
    FROM AllegroImages ai
    WHERE ai.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts)
      AND ai.Connected = 1 AND Account = @Account;

    SELECT ps.*
    FROM ProductSpecifications ps
    WHERE ps.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT pa.*
    FROM ProductApplications pa
    WHERE pa.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT pack.*
    FROM ProductPackages pack
    WHERE pack.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT param.Id,
    param.ProductId,
    param.CategoryParameterId,
    param.Value,
    param.IsForProduct,
    catParam.Name
    FROM RolmarProductParameters param
    join dbo.CategoryParameters catParam on param.CategoryParameterId = catParam.Id
    WHERE param.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetForErliCreation]
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM AllegroOffers o
    JOIN AllegroOfferDescriptions d ON o.Id = d.OfferId
    LEFT JOIN AllegroOfferAttributes a on a.OfferId = o.Id and a.type in ('dictionary', 'string', 'number', 'float', 'int')
    WHERE o.Account = @Account AND ExistsInErli = 0
      AND o.Status in ('ACTIVE', 'ENDED')
      AND Price > 0 AND Stock > 0
      AND CategoryId != 0 AND CategoryId is not null
      AND Account = @Account;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOrders_GetToUpdateExternalInfo]
    @IntegrationCompany INT,
    @Account INT,
    @NotWithExternalOrderStatus VARCHAR(100) = 'Zrealizowane',
    @ShippingRates dbo.ShippingRateList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM AllegroOrders o
    LEFT JOIN AllegroOrderItems i ON o.Id = i.AllegroOrderId
    WHERE
        o.SentToExternalCompany = 1
        AND o.IntegrationCompany = @IntegrationCompany
        AND o.Account = @Account
        AND ISNULL(o.ExternalOrderStatus,'') <> @NotWithExternalOrderStatus
        AND o.ExternalOrderId IS NOT NULL
        AND CreatedAt >= GETDATE()-7
        AND EXISTS (
            SELECT 1
            FROM @ShippingRates sr
            WHERE sr.ShippingRate = i.ShippingRate
        );
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_GetForDetailUpdate]
    @Limit INT,
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Distinct p.IntegrationId
    FROM RolmarProducts p
    WHERE NOT EXISTS (SELECT 1 FROM RolmarCategory pc WHERE pc.ProductId = p.Id)
      AND IntegrationCompany = @IntegrationCompany
      order by p.IntegrationId
    OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_GetByIntegrationId]
    @IntegrationId INT,
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1 *
    FROM RolmarProducts p
    WHERE IntegrationId = @IntegrationId AND IntegrationCompany = @IntegrationCompany
    ORDER BY p.CreatedDate DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UpsertAllegroIdByCode
    @Code NVARCHAR(255),
    @AllegroId NVARCHAR(255),
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @Code IS NULL OR LTRIM(RTRIM(@Code)) = ''
        RETURN;

    IF @AllegroId IS NULL OR LTRIM(RTRIM(@AllegroId)) = ''
        RETURN;

    UPDATE dbo.RolmarProducts
    SET AllegroId = @AllegroId,
        UpdatedDate = SYSUTCDATETIME()
    WHERE Code = @Code
      AND IntegrationCompany = @IntegrationCompany
      AND ISNULL(AllegroId, '') <> @AllegroId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_UpdateDefaultCategoryById]
    @ProductId INT,
    @CategoryId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE RolmarProducts
    SET DefaultAllegroCategory = @CategoryId,
        UpdatedDate = SYSUTCDATETIME()
    WHERE Id = @ProductId
      AND (
            DefaultAllegroCategory IS NULL
            OR DefaultAllegroCategory <> @CategoryId
          );

    IF @@ROWCOUNT > 0
    BEGIN
        DELETE FROM dbo.RolmarProductParameters
        WHERE ProductId = @ProductId;
    END
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_UpdateDefaultCategoryByCode]
    @ProductCode NVARCHAR(255),
    @CategoryId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UpdatedProducts TABLE (ProductId INT);

    UPDATE dbo.RolmarProducts
    SET DefaultAllegroCategory = @CategoryId,
        UpdatedDate = SYSUTCDATETIME()
    OUTPUT INSERTED.Id INTO @UpdatedProducts(ProductId)
    WHERE Code = @ProductCode
      AND (
            DefaultAllegroCategory IS NULL
            OR DefaultAllegroCategory <> @CategoryId
          );

    -- Delete parameters only for actually updated products
    DELETE pp
    FROM dbo.RolmarProductParameters pp
    INNER JOIN @UpdatedProducts u ON pp.ProductId = u.ProductId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_Upsert]
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
    @Package DECIMAL(18, 2)
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
            @DeliveryType AS DeliveryType,
            @PriceNet AS PriceNet,
            @PriceGross AS PriceGross,
            @Package AS Package
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
        ISNULL(target.DeliveryType, 0) <> ISNULL(case when source.Name like ('Spirala%') and source.DeliveryType = 0 then 1 else source.DeliveryType end, 0) OR
        ISNULL(target.PriceNet, 0) <> ISNULL(source.PriceNet, 0) OR
        ISNULL(target.PriceGross, 0) <> ISNULL(source.PriceGross, 0) OR
        ISNULL(target.Package, 0) <> ISNULL(source.Package, 0)
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
            DeliveryType = case when source.Name like ('Spirala%') and source.DeliveryType = 0 then 1 else source.DeliveryType end,
            PriceNet = source.PriceNet,
            PriceGross = source.PriceGross,
            Package = source.Package,
            UpdatedDate = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            Code,
            Name,
            SupplierLogo,
            SupplierName,
            Description,
            CustomerCode,
            Ean,
            InStock,
            Weight,
            Fits,
            Unit,
            CurrencyPrice,
            Substitutes,
            IntegrationCompany,
            IntegrationId,
            DeliveryType,
            PriceNet,
            PriceGross,
            Package,
            CreatedDate,
            UpdatedDate
        )
        VALUES
        (
            source.Code,
            source.Name,
            source.SupplierLogo,
            source.SupplierName,
            source.Description,
            source.CustomerCode,
            source.Ean,
            source.InStock,
            source.Weight,
            source.Fits,
            source.Unit,
            source.CurrencyPrice,
            source.Substitutes,
            source.IntegrationCompany,
            source.IntegrationId,
            case when source.Name like ('Spirala%') and source.DeliveryType = 0 then 1 else source.DeliveryType end,
            source.PriceNet,
            source.PriceGross,
            source.Package,
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        )
    OUTPUT inserted.Id INTO @Result(Id);

    IF NOT EXISTS (SELECT 1 FROM @Result)
    BEGIN
        INSERT INTO @Result(Id)
        SELECT TOP (1) Id
        FROM dbo.RolmarProducts
        WHERE Code = @Code
          AND IntegrationCompany = @IntegrationCompany
        ORDER BY Id DESC;
    END

    SELECT TOP (1) Id FROM @Result;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_UpsertBatch]
    @Products dbo.RolmarProductUpsertType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET p.InStock = 0,
        p.UpdatedDate = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    INNER JOIN @Products s
        ON p.IntegrationCompany = s.IntegrationCompany
       AND p.IntegrationId = s.IntegrationId
       AND p.Code <> s.Code;

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
            p.DeliveryType,
            p.PriceNet,
            p.PriceGross,
            p.Package
        FROM @Products p
    ) AS source
    ON target.Code = source.Code
       AND target.IntegrationCompany = source.IntegrationCompany

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
        ISNULL(target.DeliveryType, 0) <> ISNULL(case when source.Name like ('SPIRALA%') and source.DeliveryType = 0 then 1 else source.DeliveryType end, 0) OR
        ISNULL(target.PriceNet, 0) <> ISNULL(source.PriceNet, 0) OR
        ISNULL(target.PriceGross, 0) <> ISNULL(source.PriceGross, 0) OR
        ISNULL(target.Package, 0) <> ISNULL(source.Package, 0)
    )
    THEN
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
            DeliveryType = case when source.Name like ('SPIRALA%') and source.DeliveryType = 0 then 1 else source.DeliveryType end,
            PriceNet =  source.PriceNet,
            PriceGross = source.PriceGross,
            Package = source.Package,
            UpdatedDate = SYSUTCDATETIME()

    WHEN NOT MATCHED THEN
        INSERT
        (
            Code, Name, SupplierLogo, SupplierName, Description, CustomerCode, Ean,
            InStock, Weight, Fits, Unit, CurrencyPrice, Substitutes,
            IntegrationCompany, IntegrationId, DeliveryType,
            PriceNet, PriceGross, Package, CreatedDate, UpdatedDate
        )
        VALUES
        (
            source.Code, source.Name, source.SupplierLogo, source.SupplierName,
            source.Description, source.CustomerCode, source.Ean,
            source.InStock, source.Weight, source.Fits, source.Unit,
            source.CurrencyPrice, source.Substitutes,
            source.IntegrationCompany, source.IntegrationId, case when source.Name like ('SPIRALA%') and source.DeliveryType = 0 then 1 else source.DeliveryType end,
            source.PriceNet, source.PriceGross, source.Package,
            SYSUTCDATETIME(), SYSUTCDATETIME()
        );

END
GO
