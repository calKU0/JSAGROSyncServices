IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'BuildCompatibilitySet' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD BuildCompatibilitySet BIT NOT NULL
        CONSTRAINT DF_RolmarProducts_BuildCompatibilitySet DEFAULT (1);
END

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'SupplierLogo' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD SupplierLogo NVARCHAR(255) NULL;
END

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'DeliveryType' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD DeliveryType INT DEFAULT(0);
END

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'CustomerCode' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD CustomerCode NVARCHAR(255) NULL;
END

IF EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'Package' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_Package;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_Package
        DEFAULT 1 FOR Package;
END

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'ProductPackages'
)
BEGIN
    CREATE TABLE [dbo].[ProductPackages]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [PackUnit] NVARCHAR(50) NULL,
        [PackQty] FLOAT NOT NULL,
        [PackNettWeight] FLOAT NOT NULL,
        [PackGrossWeight] FLOAT NOT NULL,
        [PackEan] NVARCHAR(50) NULL,
        [PackRequired] INT NOT NULL,
        [ProductId] INT NOT NULL,
        CONSTRAINT FK_ProductPackages_RolmarProducts
            FOREIGN KEY ([ProductId])
            REFERENCES [dbo].[RolmarProducts] ([Id])
            ON DELETE CASCADE
    );

    CREATE INDEX IX_ProductPackages_ProductId
        ON [dbo].[ProductPackages] ([ProductId]);
END

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'ProductApplications'
)
BEGIN
    CREATE TABLE [dbo].[ProductApplications]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ApplicationId] INT NOT NULL,
        [ParentID] INT NOT NULL,
        [Name] NVARCHAR(255) NULL,
        [ProductId] INT NOT NULL,
        CONSTRAINT FK_ProductApplications_RolmarProducts
            FOREIGN KEY ([ProductId])
            REFERENCES [dbo].[RolmarProducts] ([Id])
            ON DELETE CASCADE
    );

    CREATE INDEX IX_ProductApplications_ProductId
        ON [dbo].[ProductApplications] ([ProductId]);
END
