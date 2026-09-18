-- Kategorie synchronizacji konfigurowane per konto Allegro.
-- Rolmar (IntegrationCompany = 1) trzyma tu sciezki kategorii dostawcy, Gaska (IntegrationCompany = 2) id kategorii z API dostawcy.
IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'SyncCategories' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE dbo.SyncCategories
    (
        Id INT IDENTITY(1,1) NOT NULL,
        Account INT NOT NULL,
        IntegrationCompany INT NOT NULL,
        Category NVARCHAR(400) NOT NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_SyncCategories_CreatedDate DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_SyncCategories PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_SyncCategories UNIQUE (Account, IntegrationCompany, Category)
    );
END
GO

-- Powiazanie produktu z kategoria dostawcy, dla dostawcow ktorzy nie zwracaja kategorii razem z produktem (Gaska).
IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'ProductSupplierCategories' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE dbo.ProductSupplierCategories
    (
        Id INT IDENTITY(1,1) NOT NULL,
        ProductId INT NOT NULL,
        CategoryId INT NOT NULL,

        CONSTRAINT PK_ProductSupplierCategories PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ProductSupplierCategories UNIQUE (ProductId, CategoryId),
        CONSTRAINT FK_ProductSupplierCategories_RolmarProducts
            FOREIGN KEY (ProductId)
            REFERENCES dbo.RolmarProducts (Id)
            ON DELETE CASCADE
    );
END
GO

IF TYPE_ID(N'dbo.SyncCategoryType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.SyncCategoryType AS TABLE
    (
        Category NVARCHAR(400) NOT NULL
    );');
END
GO

IF TYPE_ID(N'dbo.ProductSupplierCategoryType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.ProductSupplierCategoryType AS TABLE
    (
        Code NVARCHAR(255) NOT NULL,
        CategoryId INT NOT NULL
    );');
END
GO

IF TYPE_ID(N'dbo.SupplierCategoryIdType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.SupplierCategoryIdType AS TABLE
    (
        CategoryId INT NOT NULL
    );');
END
GO

CREATE OR ALTER PROCEDURE dbo.SyncCategories_ReplaceByAccount
    @Account INT,
    @IntegrationCompany INT,
    @Categories dbo.SyncCategoryType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    WITH source AS
    (
        SELECT DISTINCT LTRIM(RTRIM(Category)) AS Category
        FROM @Categories
        WHERE LTRIM(RTRIM(Category)) <> ''
    )
    MERGE dbo.SyncCategories AS target
    USING (SELECT @Account AS Account, @IntegrationCompany AS IntegrationCompany, Category FROM source) AS src
    ON target.Account = src.Account
       AND target.IntegrationCompany = src.IntegrationCompany
       AND target.Category = src.Category
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (Account, IntegrationCompany, Category)
        VALUES (src.Account, src.IntegrationCompany, src.Category)
    WHEN NOT MATCHED BY SOURCE
         AND target.Account = @Account
         AND target.IntegrationCompany = @IntegrationCompany THEN
        DELETE;
END
GO

-- Suma kategorii wszystkich kont dla danego dostawcy - tyle produktow musi pobrac serwis pobierajacy dane od dostawcy.
CREATE OR ALTER PROCEDURE dbo.SyncCategories_GetByCompany
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DISTINCT Category
    FROM dbo.SyncCategories
    WHERE IntegrationCompany = @IntegrationCompany;
END
GO

CREATE OR ALTER PROCEDURE dbo.ProductSupplierCategories_ReplaceByCodes
    @IntegrationCompany INT,
    @Items dbo.ProductSupplierCategoryType READONLY,
    @FetchedCategories dbo.SupplierCategoryIdType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Mapped TABLE
    (
        ProductId INT NOT NULL,
        CategoryId INT NOT NULL,
        PRIMARY KEY (ProductId, CategoryId)
    );

    INSERT INTO @Mapped (ProductId, CategoryId)
    SELECT DISTINCT p.Id, i.CategoryId
    FROM @Items i
    JOIN dbo.RolmarProducts p
        ON p.Code = i.Code
       AND p.IntegrationCompany = @IntegrationCompany;

    -- Kasujemy tylko przypisania do kategorii pobranych w calosci w tym przebiegu.
    -- Kategoria, ktorej nie udalo sie pobrac, zostaje z dotychczasowymi przypisaniami.
    DELETE psc
    FROM dbo.ProductSupplierCategories psc
    WHERE psc.ProductId IN (SELECT ProductId FROM @Mapped)
      AND psc.CategoryId IN (SELECT CategoryId FROM @FetchedCategories)
      AND NOT EXISTS
      (
          SELECT 1 FROM @Mapped m
          WHERE m.ProductId = psc.ProductId AND m.CategoryId = psc.CategoryId
      );

    INSERT INTO dbo.ProductSupplierCategories (ProductId, CategoryId)
    SELECT m.ProductId, m.CategoryId
    FROM @Mapped m
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.ProductSupplierCategories psc
        WHERE psc.ProductId = m.ProductId AND psc.CategoryId = m.CategoryId
    );
END
GO

IF TYPE_ID(N'dbo.ProductStockType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.ProductStockType AS TABLE
    (
        Code NVARCHAR(255) NOT NULL,
        Stock INT NOT NULL
    );');
END
GO

-- Wsadowa aktualizacja stanow - dotad kazdy produkt oznaczal osobne wywolanie procedury.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UpdateStockBatch
    @IntegrationCompany INT,
    @Items dbo.ProductStockType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET p.InStock = s.Stock,
        p.UpdatedDate = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    JOIN
    (
        SELECT Code, MAX(Stock) AS Stock
        FROM @Items
        GROUP BY Code
    ) s ON s.Code = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.InStock <> s.Stock;

    SELECT @@ROWCOUNT;
END
GO
