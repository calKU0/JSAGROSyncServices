IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME = 'AllegroDeliveryMethodDetails'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'dbo'
          AND TABLE_NAME = 'AllegroDeliveryMethodDetails'
          AND COLUMN_NAME = 'MaxPackageWeight'
          AND DATA_TYPE <> 'decimal'
    )
    BEGIN
        ALTER TABLE [dbo].[AllegroDeliveryMethodDetails]
        ALTER COLUMN [MaxPackageWeight] DECIMAL(18,2) NOT NULL;
    END

    IF EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'dbo'
          AND TABLE_NAME = 'AllegroDeliveryMethodDetails'
          AND COLUMN_NAME = 'MaxPackageWeightUnit'
          AND DATA_TYPE <> 'nvarchar'
    )
    BEGIN
        ALTER TABLE [dbo].[AllegroDeliveryMethodDetails]
        ALTER COLUMN [MaxPackageWeightUnit] NVARCHAR(50) NOT NULL;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroResponsibleProducers_Upsert
    @AllegroId NVARCHAR(255),
    @Account INT,
    @Name NVARCHAR(255),
    @TradeName NVARCHAR(255),
    @CountryCode NVARCHAR(10),
    @Street NVARCHAR(255),
    @PostalCode NVARCHAR(20),
    @City NVARCHAR(255),
    @Email NVARCHAR(255) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @FormUrl NVARCHAR(2048) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.AllegroResponsibleProducers AS target
    USING (SELECT @AllegroId AS AllegroId, @Account AS Account) AS source
    ON target.AllegroId = source.AllegroId AND target.Account = source.Account
    WHEN MATCHED THEN
        UPDATE SET
            Name = @Name,
            TradeName = @TradeName,
            CountryCode = @CountryCode,
            Street = @Street,
            PostalCode = @PostalCode,
            City = @City,
            Email = @Email,
            Phone = @Phone,
            FormUrl = @FormUrl
    WHEN NOT MATCHED THEN
        INSERT (AllegroId, Account, Name, TradeName, CountryCode, Street, PostalCode, City, Email, Phone, FormUrl)
        VALUES (@AllegroId, @Account, @Name, @TradeName, @CountryCode, @Street, @PostalCode, @City, @Email, @Phone, @FormUrl);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroResponsibleProducers_GetAll
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroResponsibleProducers
    WHERE Account = @Account;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroResponsiblePersons_Upsert
    @AllegroId NVARCHAR(255),
    @Account INT,
    @Name NVARCHAR(255),
    @PersonName NVARCHAR(255),
    @CountryCode NVARCHAR(10),
    @Street NVARCHAR(255),
    @PostalCode NVARCHAR(20),
    @City NVARCHAR(255),
    @Email NVARCHAR(255) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @FormUrl NVARCHAR(2048) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.AllegroResponsiblePersons AS target
    USING (SELECT @AllegroId AS AllegroId, @Account AS Account) AS source
    ON target.AllegroId = source.AllegroId AND target.Account = source.Account
    WHEN MATCHED THEN
        UPDATE SET
            Name = @Name,
            PersonName = @PersonName,
            CountryCode = @CountryCode,
            Street = @Street,
            PostalCode = @PostalCode,
            City = @City,
            Email = @Email,
            Phone = @Phone,
            FormUrl = @FormUrl
    WHEN NOT MATCHED THEN
        INSERT (AllegroId, Account, Name, PersonName, CountryCode, Street, PostalCode, City, Email, Phone, FormUrl)
        VALUES (@AllegroId, @Account, @Name, @PersonName, @CountryCode, @Street, @PostalCode, @City, @Email, @Phone, @FormUrl);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroResponsiblePersons_GetAll
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroResponsiblePersons
    WHERE Account = @Account;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroDeliveryMethods_Upsert
    @AllegroId NVARCHAR(255),
    @Account INT,
    @Name NVARCHAR(255),
    @ManagedByAllegro BIT,
    @IsFulfillment BIT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Id INT;

    SELECT @Id = Id
    FROM dbo.AllegroDeliveryMethods
    WHERE AllegroId = @AllegroId
      AND Account = @Account;

    IF @Id IS NULL
    BEGIN
        INSERT INTO dbo.AllegroDeliveryMethods (AllegroId, Account, Name, ManagedByAllegro, IsFulfillment)
        VALUES (@AllegroId, @Account, @Name, @ManagedByAllegro, @IsFulfillment);

        SET @Id = CAST(SCOPE_IDENTITY() AS INT);
    END
    ELSE
    BEGIN
        UPDATE dbo.AllegroDeliveryMethods
        SET Name = @Name,
            ManagedByAllegro = @ManagedByAllegro,
            IsFulfillment = @IsFulfillment
        WHERE Id = @Id;
    END

    SELECT @Id AS Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroDeliveryMethodDetails_DeleteByDeliveryMethodId
    @AllegroDeliveryMethodId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.AllegroDeliveryMethodDetails
    WHERE AllegroDeliveryMethodId = @AllegroDeliveryMethodId;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroDeliveryMethodDetails_Upsert
    @AllegroDeliveryMethodId INT,
    @Name NVARCHAR(255),
    @PaymentPolicy INT,
    @MaxPackageQuantity INT,
    @MaxPackageWeight DECIMAL(18,2),
    @MaxPackageWeightUnit NVARCHAR(50),
    @FirstItemAmount DECIMAL(18,2),
    @FirstItemCurrency NVARCHAR(10),
    @NextItemAmount DECIMAL(18,2),
    @NextItemCurrency NVARCHAR(10),
    @ShippingTimeFrom NVARCHAR(100),
    @ShippingTimeTo NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.AllegroDeliveryMethodDetails AS target
    USING (
        SELECT
            @AllegroDeliveryMethodId AS AllegroDeliveryMethodId,
            @Name AS Name
    ) AS source
    ON target.AllegroDeliveryMethodId = source.AllegroDeliveryMethodId
       AND target.Name = source.Name
    WHEN MATCHED THEN
        UPDATE SET
            MaxPackageQuantity = @MaxPackageQuantity,
            MaxPackageWeight = @MaxPackageWeight,
            PaymentPolicy = @PaymentPolicy,
            MaxPackageWeightUnit = @MaxPackageWeightUnit,
            FirstItemAmount = @FirstItemAmount,
            FirstItemCurrency = @FirstItemCurrency,
            NextItemAmount = @NextItemAmount,
            NextItemCurrency = @NextItemCurrency,
            ShippingTimeFrom = @ShippingTimeFrom,
            ShippingTimeTo = @ShippingTimeTo
    WHEN NOT MATCHED THEN
        INSERT
        (
            AllegroDeliveryMethodId,
            Name,
            PaymentPolicy,
            MaxPackageQuantity,
            MaxPackageWeight,
            MaxPackageWeightUnit,
            FirstItemAmount,
            FirstItemCurrency,
            NextItemAmount,
            NextItemCurrency,
            ShippingTimeFrom,
            ShippingTimeTo
        )
        VALUES
        (
            @AllegroDeliveryMethodId,
            @Name,
            @PaymentPolicy,
            @MaxPackageQuantity,
            @MaxPackageWeight,
            @MaxPackageWeightUnit,
            @FirstItemAmount,
            @FirstItemCurrency,
            @NextItemAmount,
            @NextItemCurrency,
            @ShippingTimeFrom,
            @ShippingTimeTo
        );
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroDeliveryMethods_GetAll
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroDeliveryMethods
    WHERE Account = @Account;

    SELECT d.*
    FROM dbo.AllegroDeliveryMethodDetails d
    INNER JOIN dbo.AllegroDeliveryMethods m ON m.Id = d.AllegroDeliveryMethodId
    WHERE m.Account = @Account;
END
GO
