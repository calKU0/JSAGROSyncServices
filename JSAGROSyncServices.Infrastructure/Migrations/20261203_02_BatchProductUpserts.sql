-- Wsadowa podmiana specyfikacji i kategorii produktow.
-- Dotad kazdy produkt oznaczal dwa osobne wywolania procedury, przez co zapis 20 tys. produktow
-- Rolmara trwal kilkanascie minut na samych round-tripach do bazy.

IF TYPE_ID(N'dbo.ProductCodeType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.ProductCodeType AS TABLE
    (
        Code NVARCHAR(255) NOT NULL
    );');
END
GO

IF TYPE_ID(N'dbo.ProductSpecificationBatchType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.ProductSpecificationBatchType AS TABLE
    (
        Code NVARCHAR(255) NOT NULL,
        Name NVARCHAR(255) NOT NULL,
        Value NVARCHAR(MAX) NULL,
        UnitName NVARCHAR(255) NULL
    );');
END
GO

IF TYPE_ID(N'dbo.ProductCategoryBatchType') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.ProductCategoryBatchType AS TABLE
    (
        Code NVARCHAR(255) NOT NULL,
        Name NVARCHAR(255) NOT NULL
    );');
END
GO

CREATE OR ALTER PROCEDURE dbo.ProductSpecifications_ReplaceBatch
    @IntegrationCompany INT,
    @Codes dbo.ProductCodeType READONLY,
    @Items dbo.ProductSpecificationBatchType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Products TABLE (Id INT NOT NULL PRIMARY KEY, Code NVARCHAR(255) NOT NULL);

    INSERT INTO @Products (Id, Code)
    SELECT p.Id, p.Code
    FROM dbo.RolmarProducts p
    JOIN (SELECT DISTINCT Code FROM @Codes) c ON c.Code = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany;

    DELETE ps
    FROM dbo.ProductSpecifications ps
    JOIN @Products p ON p.Id = ps.ProductId;

    INSERT INTO dbo.ProductSpecifications (ProductId, Name, Value, UnitName)
    SELECT p.Id, i.Name, i.Value, i.UnitName
    FROM @Items i
    JOIN @Products p ON p.Code = i.Code;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarCategory_ReplaceBatch
    @IntegrationCompany INT,
    @Codes dbo.ProductCodeType READONLY,
    @Items dbo.ProductCategoryBatchType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Products TABLE (Id INT NOT NULL PRIMARY KEY, Code NVARCHAR(255) NOT NULL);

    INSERT INTO @Products (Id, Code)
    SELECT p.Id, p.Code
    FROM dbo.RolmarProducts p
    JOIN (SELECT DISTINCT Code FROM @Codes) c ON c.Code = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany;

    DELETE rc
    FROM dbo.RolmarCategory rc
    JOIN @Products p ON p.Id = rc.ProductId;

    INSERT INTO dbo.RolmarCategory (ProductId, Name)
    SELECT DISTINCT p.Id, i.Name
    FROM @Items i
    JOIN @Products p ON p.Code = i.Code;
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

    DECLARE @Cat TABLE
    (
        Value NVARCHAR(400) NOT NULL,
        ValueId INT NULL
    );

    IF @Categories IS NOT NULL AND LTRIM(RTRIM(@Categories)) <> ''
    BEGIN
        INSERT INTO @Cat (Value, ValueId)
        SELECT DISTINCT LTRIM(RTRIM([value])), TRY_CAST(LTRIM(RTRIM([value])) AS INT)
        FROM OPENJSON(@Categories)
        WHERE LTRIM(RTRIM([value])) <> '';
    END

    DECLARE @HasCategoryFilter BIT = CASE WHEN EXISTS (SELECT 1 FROM @Cat) THEN 1 ELSE 0 END;

    SELECT
        p.Id,
        p.Code,
        p.Name,
        p.Description,
        p.Ean,
        p.Weight,
        p.Fits,
        p.SupplierName,
        p.InStock,
        p.Unit,
        p.CurrencyPrice,
        p.PriceNet,
        p.PriceGross,
        p.DefaultAllegroCategory,
        p.Package,
        p.CreatedDate,
        p.UpdatedDate,
        p.Substitutes,
        p.AllegroId,
        p.DeliveryType,
        ps.Id,
        ps.ProductId,
        ps.Name,
        ps.Value,
        ps.UnitName,
        pp.Id,
        pp.ProductId,
        pp.CategoryParameterId,
        cp.Name,
        pp.Value,
        pp.IsForProduct,
        ap.Id,
        ap.ApplicationId,
        ap.Name,
        ap.ParentID,
        ap.ProductId,
        pack.Id,
        pack.PackEan,
        pack.PackGrossWeight,
        pack.PackNettWeight,
        pack.PackQty,
        pack.PackRequired,
        pack.PackUnit,
        pack.ProductId
    FROM RolmarProducts p
    LEFT JOIN ProductSpecifications ps ON ps.ProductId = p.Id
    JOIN RolmarProductParameters pp ON pp.ProductId = p.Id
    JOIN CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    JOIN RolmarCategory rc ON rc.ProductId = p.Id
    LEFT JOIN ProductApplications ap ON ap.ProductId = p.Id
    LEFT JOIN ProductPackages pack ON pack.ProductId = p.Id
    LEFT JOIN AllegroOffers ao ON ao.ExternalId = p.Code AND ao.Account = @Account
    WHERE p.InStock >= @MinProductStock AND p.PriceNet >= @MinProductPrice
      AND NULLIF(p.DefaultAllegroCategory, 0) IS NOT NULL
      AND ao.Id IS NULL
      AND IntegrationCompany = @IntegrationCompany
      AND
      (
          @HasCategoryFilter = 0
          OR
          (
              @IntegrationCompany = 1 -- Rolmar
              AND
              (
                  EXISTS
                  (
                      SELECT 1
                      FROM dbo.RolmarCategory rcf
                      JOIN @Cat c ON LEFT(rcf.Name, LEN(c.Value)) = c.Value
                      WHERE rcf.ProductId = p.Id
                  )
                  OR NOT EXISTS (SELECT 1 FROM dbo.RolmarCategory rcf WHERE rcf.ProductId = p.Id)
              )
          )
          OR
          (
              @IntegrationCompany <> 1 -- Gaska
              AND
              (
                  EXISTS
                  (
                      SELECT 1
                      FROM dbo.ProductSupplierCategories psc
                      JOIN @Cat c ON c.ValueId = psc.CategoryId
                      WHERE psc.ProductId = p.Id
                  )
                  OR NOT EXISTS (SELECT 1 FROM dbo.ProductSupplierCategories psc WHERE psc.ProductId = p.Id)
              )
          )
      )
    ORDER BY p.Id;
END
GO

-- Oferty do zakonczenia: produkt wypadl ze skonfigurowanych kategorii konta.
-- Ograniczone do cennikow obslugiwanych przez serwis - ofert wystawionych recznie nie ruszamy.
CREATE OR ALTER PROCEDURE dbo.AllegroOffers_GetOffersToEnd
    @IntegrationCompany INT,
    @Account INT,
    @DeliveryNames NVARCHAR(MAX),
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Cat TABLE
    (
        Value NVARCHAR(400) NOT NULL,
        ValueId INT NULL
    );

    IF @Categories IS NOT NULL AND LTRIM(RTRIM(@Categories)) <> ''
    BEGIN
        INSERT INTO @Cat (Value, ValueId)
        SELECT DISTINCT LTRIM(RTRIM([value])), TRY_CAST(LTRIM(RTRIM([value])) AS INT)
        FROM OPENJSON(@Categories)
        WHERE LTRIM(RTRIM([value])) <> '';
    END

    IF NOT EXISTS (SELECT 1 FROM @Cat)
        RETURN;

    IF @DeliveryNames IS NULL OR LTRIM(RTRIM(@DeliveryNames)) = ''
        RETURN;

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
      AND ao.DeliveryName IN (SELECT LTRIM(RTRIM([value])) FROM OPENJSON(@DeliveryNames))
      AND
      (
          (
              @IntegrationCompany = 1
              AND EXISTS (SELECT 1 FROM dbo.RolmarCategory rc WHERE rc.ProductId = p.Id)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.RolmarCategory rc
                  JOIN @Cat c ON LEFT(rc.Name, LEN(c.Value)) = c.Value
                  WHERE rc.ProductId = p.Id
              )
          )
          OR
          (
              @IntegrationCompany <> 1
              AND EXISTS (SELECT 1 FROM dbo.ProductSupplierCategories psc WHERE psc.ProductId = p.Id)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.ProductSupplierCategories psc
                  JOIN @Cat c ON c.ValueId = psc.CategoryId
                  WHERE psc.ProductId = p.Id
              )
          )
      );
END
GO
