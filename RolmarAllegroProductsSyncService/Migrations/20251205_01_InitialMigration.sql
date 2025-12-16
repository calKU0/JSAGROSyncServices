CREATE TABLE [dbo].[RolmarProducts]
(
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Code] NVARCHAR(255) NOT NULL,
    [Name] NVARCHAR(255) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    [Ean] NVARCHAR(50) NULL,
    [Weight] FLOAT NOT NULL,
    [Fits] NVARCHAR(MAX) NULL,
    [SupplierName] NVARCHAR(255) NULL,
    [InStock] FLOAT NOT NULL,
    [Unit] NVARCHAR(50) NULL,
    [CurrencyPrice] NVARCHAR(50) NULL,
    [PriceNet] DECIMAL(18,2) NOT NULL,
    [PriceGross] DECIMAL(18,2) NOT NULL,
    [DefaultAllegroCategory] INT NOT NULL,
    [Package] DECIMAL(18,2) NOT NULL,

    [CreatedDate] DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [UpdatedDate] DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE [dbo].[ProductSpecifications] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ProductId] INT NOT NULL,
    [Name] NVARCHAR(255) NOT NULL,
    [Value] NVARCHAR(MAX) NULL,
    [UnitName] NVARCHAR(255) NULL,

    CONSTRAINT FK_ProductSpecifications_Products
        FOREIGN KEY ([ProductId]) REFERENCES [dbo].[RolmarProducts]([Id])
        ON DELETE CASCADE
);

CREATE TABLE [dbo].[RolmarProductParameters]
(
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ProductId] INT NOT NULL,
    [CategoryParameterId] INT NOT NULL,
    [Value] NVARCHAR(MAX) NULL,
    [IsForProduct] BIT NOT NULL DEFAULT(0),

    CONSTRAINT FK_RolmarProductParameters_Products
        FOREIGN KEY ([ProductId]) REFERENCES [dbo].[RolmarProducts]([Id]) 
        ON DELETE CASCADE,

    CONSTRAINT FK_RolmarProductParameters_CategoryParameter
        FOREIGN KEY ([CategoryParameterId]) REFERENCES [dbo].[CategoryParameters]([Id])
);

CREATE TABLE [dbo].[ProductImages]
(
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ProductId] INT NOT NULL,
    [ImageData] VARBINARY(MAX) NOT NULL,

    CONSTRAINT FK_ProductImages_Products
        FOREIGN KEY ([ProductId]) REFERENCES [dbo].[RolmarProducts]([Id])
        ON DELETE CASCADE
);

CREATE NONCLUSTERED INDEX IX_ProductImages_ProductId
ON [dbo].[ProductImages] ([ProductId]);