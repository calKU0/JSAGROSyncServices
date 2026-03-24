IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'AllegroResponsibleProducers'
)
BEGIN
    CREATE TABLE [dbo].[AllegroResponsibleProducers]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [AllegroId] NVARCHAR(255) NOT NULL,
        [Account] INT NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [TradeName] NVARCHAR(255) NOT NULL,
        [CountryCode] NVARCHAR(10) NOT NULL,
        [Street] NVARCHAR(255) NOT NULL,
        [PostalCode] NVARCHAR(20) NOT NULL,
        [City] NVARCHAR(255) NOT NULL,
        [Email] NVARCHAR(255) NULL,
        [Phone] NVARCHAR(50) NULL,
        [FormUrl] NVARCHAR(2048) NULL
    );

    CREATE INDEX IX_AllegroResponsibleProducers_AllegroId
        ON [dbo].[AllegroResponsibleProducers] ([AllegroId]);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'AllegroResponsiblePersons'
)
BEGIN
    CREATE TABLE [dbo].[AllegroResponsiblePersons]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [AllegroId] NVARCHAR(255) NOT NULL,
        [Account] INT NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [PersonName] NVARCHAR(255) NOT NULL,
        [CountryCode] NVARCHAR(10) NOT NULL,
        [Street] NVARCHAR(255) NOT NULL,
        [PostalCode] NVARCHAR(20) NOT NULL,
        [City] NVARCHAR(255) NOT NULL,
        [Email] NVARCHAR(255) NULL,
        [Phone] NVARCHAR(50) NULL,
        [FormUrl] NVARCHAR(2048) NULL
    );

    CREATE INDEX IX_AllegroResponsiblePersons_AllegroId
        ON [dbo].[AllegroResponsiblePersons] ([AllegroId]);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'AllegroDeliveryMethods'
)
BEGIN
    CREATE TABLE [dbo].[AllegroDeliveryMethods]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [AllegroId] NVARCHAR(255) NOT NULL,
        [Account] INT NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [ManagedByAllegro] BIT NOT NULL,
        [IsFulfillment] BIT NOT NULL
    );

    CREATE INDEX IX_AllegroDeliveryMethods_AllegroId
        ON [dbo].[AllegroDeliveryMethods] ([AllegroId]);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'AllegroDeliveryMethodDetails'
)
BEGIN
    CREATE TABLE [dbo].[AllegroDeliveryMethodDetails]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [AllegroDeliveryMethodId] INT NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [PaymentPolicy] INT NOT NULL,
        [MaxPackageQuantity] INT NULL,
        [MaxPackageWeight] INT NULL,
        [MaxPackageWeightUnit] INT NULL,
        [FirstItemAmount] DECIMAL(18,2) NOT NULL,
        [FirstItemCurrency] NVARCHAR(10) NOT NULL,
        [NextItemAmount] DECIMAL(18,2) NULL,
        [NextItemCurrency] NVARCHAR(10) NULL,
        [ShippingTimeFrom] NVARCHAR(100) NULL,
        [ShippingTimeTo] NVARCHAR(100) NULL,

        CONSTRAINT FK_AllegroDeliveryMethodDetails_AllegroDeliveryMethods
            FOREIGN KEY ([AllegroDeliveryMethodId])
            REFERENCES [dbo].[AllegroDeliveryMethods] ([Id])
            ON DELETE CASCADE
    );

    CREATE INDEX IX_AllegroDeliveryMethodDetails_AllegroDeliveryMethodId
        ON [dbo].[AllegroDeliveryMethodDetails] ([AllegroDeliveryMethodId]);

    CREATE INDEX IX_AllegroDeliveryMethodDetails_Name
        ON [dbo].[AllegroDeliveryMethodDetails] ([Name]);
END
GO
