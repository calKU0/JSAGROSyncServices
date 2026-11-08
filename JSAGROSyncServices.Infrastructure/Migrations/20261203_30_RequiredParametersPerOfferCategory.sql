-- Parametry obowiązkowe Allegro ("Uzupełnij parametry obowiązkowe: ...").
--
-- Dotąd parametry produktu były wyliczane raz - tylko dla produktu bez żadnych parametrów - i tylko
-- dla jego domyślnej kategorii. Oferta stojąca w innej kategorii (przeniesiona ręcznie albo przez
-- Allegro) i nowy parametr obowiązkowy dodany przez Allegro nie były nigdy uzupełniane, więc ta
-- sama oferta wracała z błędem w każdym cyklu. Teraz:
--  * parametry są liczone dla każdej kategorii, w której produkt faktycznie ma oferty, i ponownie,
--    gdy brakuje któregoś obowiązkowego (po zmianie produktu albo odświeżeniu parametrów kategorii),
--  * parametry kategorii są odświeżane co tydzień (Allegro zmienia listę obowiązkowych),
--  * zapamiętujemy etykietę wartości niejednoznacznej ("Inna", "nieznany producent") ze słownika,
--  * produkt z katalogu, któremu brakuje parametrów, może je dostać razem z id produktu,
--  * id produktu z katalogu, który Allegro odłączyło od oferty, nie jest podpinane ponownie.

IF COL_LENGTH('dbo.CategoryParameters', 'AmbiguousValue') IS NULL
    ALTER TABLE dbo.CategoryParameters ADD AmbiguousValue NVARCHAR(400) NULL;
GO

IF COL_LENGTH('dbo.CategoryParameters', 'FetchedAt') IS NULL
    ALTER TABLE dbo.CategoryParameters ADD FetchedAt DATETIME2(7) NULL;
GO

IF COL_LENGTH('dbo.RolmarProducts', 'ParametersCheckedAt') IS NULL
    ALTER TABLE dbo.RolmarProducts ADD ParametersCheckedAt DATETIME2(7) NULL;
GO

IF COL_LENGTH('dbo.RolmarProducts', 'SendCatalogParameters') IS NULL
    ALTER TABLE dbo.RolmarProducts ADD SendCatalogParameters BIT NOT NULL CONSTRAINT DF_RolmarProducts_SendCatalogParameters DEFAULT (0);
GO

IF COL_LENGTH('dbo.RolmarProducts', 'RejectedAllegroIds') IS NULL
    ALTER TABLE dbo.RolmarProducts ADD RejectedAllegroIds NVARCHAR(MAX) NULL;
GO

-- ============================================================ parametry kategorii

CREATE OR ALTER PROCEDURE dbo.CategoryParameters_UpsertWithValues
    @CategoryId INT,
    @ParameterId INT,
    @Name NVARCHAR(255),
    @Type NVARCHAR(100),
    @Required BIT,
    @Min INT = NULL,
    @Max INT = NULL,
    @RequiredForProduct BIT,
    @DescribesProduct BIT,
    @CustomValuesEnabled BIT,
    @AmbiguousValueId NVARCHAR(100) = NULL,
    @AmbiguousValue NVARCHAR(400) = NULL,
    @ValuesJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Id INT;

    SELECT @Id = Id
    FROM dbo.CategoryParameters
    WHERE CategoryId = @CategoryId AND ParameterId = @ParameterId;

    IF @Id IS NULL
    BEGIN
        INSERT INTO dbo.CategoryParameters
            (CategoryId, ParameterId, Name, Type, Required, Min, Max, RequiredForProduct, DescribesProduct,
             CustomValuesEnabled, AmbiguousValueId, AmbiguousValue, FetchedAt)
        VALUES
            (@CategoryId, @ParameterId, @Name, @Type, @Required, @Min, @Max, @RequiredForProduct, @DescribesProduct,
             @CustomValuesEnabled, @AmbiguousValueId, NULLIF(@AmbiguousValue, ''), SYSUTCDATETIME());

        SET @Id = CAST(SCOPE_IDENTITY() AS INT);
    END
    ELSE
    BEGIN
        UPDATE dbo.CategoryParameters
        SET Name = @Name,
            Type = @Type,
            Required = @Required,
            Min = @Min,
            Max = @Max,
            RequiredForProduct = @RequiredForProduct,
            DescribesProduct = @DescribesProduct,
            CustomValuesEnabled = @CustomValuesEnabled,
            AmbiguousValueId = @AmbiguousValueId,
            AmbiguousValue = NULLIF(@AmbiguousValue, ''),
            FetchedAt = SYSUTCDATETIME()
        WHERE Id = @Id;
    END

    DELETE FROM dbo.CategoryParameterValues
    WHERE CategoryParameterId = @Id;

    IF @ValuesJson IS NOT NULL
    BEGIN
        INSERT INTO dbo.CategoryParameterValues (CategoryParameterId, Value)
        SELECT @Id, value
        FROM OPENJSON(@ValuesJson)
        WHERE value IS NOT NULL AND LTRIM(RTRIM(value)) <> '';
    END
END
GO

-- Kategorie do pobrania / odświeżenia parametrów: domyślne kategorie produktów i kategorie, w których
-- stoją ich oferty - te, których jeszcze nie znamy, i te pobrane dawniej niż @RefreshDays dni temu.
-- @Top ogranicza liczbę zapytań do Allegro w jednym cyklu (reszta pójdzie w kolejnych).
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetDefaultAllegroCategoriesWithoutParameters
    @RefreshDays INT = 7,
    @Top INT = 200
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Used AS
    (
        SELECT DefaultAllegroCategory AS CategoryId
        FROM dbo.RolmarProducts
        WHERE DefaultAllegroCategory <> 0
        UNION
        SELECT o.CategoryId
        FROM dbo.AllegroOffers o
        WHERE o.CategoryId IS NOT NULL AND o.CategoryId <> 0 AND o.Status <> 'ENDED'
          AND EXISTS (SELECT 1 FROM dbo.RolmarProducts p WHERE p.Code = o.ExternalId)
    ),
    State AS
    (
        SELECT u.CategoryId, MAX(cp.FetchedAt) AS FetchedAt, COUNT(cp.Id) AS Parameters
        FROM Used u
        LEFT JOIN dbo.CategoryParameters cp ON cp.CategoryId = u.CategoryId
        GROUP BY u.CategoryId
    )
    SELECT TOP (@Top) CategoryId
    FROM State
    WHERE Parameters = 0
       OR FetchedAt IS NULL
       OR FetchedAt < DATEADD(DAY, -@RefreshDays, SYSUTCDATETIME())
    ORDER BY CASE WHEN Parameters = 0 THEN 0 ELSE 1 END, FetchedAt;
END
GO

-- ============================================================ parametry produktów

-- Kategorie, dla których liczymy parametry produktu: domyślna i każda, w której produkt ma ofertę.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetParameterTargetCategories
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.Id AS ProductId, p.DefaultAllegroCategory AS CategoryId
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany AND p.DefaultAllegroCategory <> 0
    UNION
    SELECT p.Id, o.CategoryId
    FROM dbo.RolmarProducts p
    JOIN dbo.AllegroOffers o ON o.ExternalId = p.Code
    WHERE p.IntegrationCompany = @IntegrationCompany AND o.CategoryId IS NOT NULL AND o.CategoryId <> 0 AND o.Status <> 'ENDED';
END
GO

-- Produkty do (ponownego) wyliczenia parametrów: bez żadnych parametrów albo z brakującym parametrem
-- obowiązkowym w którejś z kategorii docelowych - o ile od ostatniej próby zmienił się produkt
-- albo parametry kategorii (inaczej parametr, którego nie umiemy wyliczyć, wracałby co cykl).
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpdateParameters
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Targets (ProductId INT NOT NULL, CategoryId INT NOT NULL, PRIMARY KEY (ProductId, CategoryId));

    INSERT INTO #Targets (ProductId, CategoryId)
    EXEC dbo.RolmarProducts_GetParameterTargetCategories @IntegrationCompany = @IntegrationCompany;

    CREATE TABLE #ToUpdate (ProductId INT NOT NULL PRIMARY KEY);

    INSERT INTO #ToUpdate (ProductId)
    SELECT p.Id
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND (p.DefaultAllegroCategory <> 0 OR p.AllegroId IS NOT NULL)
      AND (
            NOT EXISTS (SELECT 1 FROM dbo.RolmarProductParameters pp WHERE pp.ProductId = p.Id)
            OR (
                EXISTS (
                    SELECT 1
                    FROM #Targets t
                    JOIN dbo.CategoryParameters cp ON cp.CategoryId = t.CategoryId
                    WHERE t.ProductId = p.Id
                      AND (cp.Required = 1 OR cp.RequiredForProduct = 1)
                      AND NOT EXISTS (
                          SELECT 1 FROM dbo.RolmarProductParameters pp
                          WHERE pp.ProductId = p.Id AND pp.CategoryParameterId = cp.Id)
                      AND (p.ParametersCheckedAt IS NULL
                           OR p.ParametersCheckedAt < p.UpdatedDate
                           OR p.ParametersCheckedAt < cp.FetchedAt)
                )
            )
          );

    SELECT p.*, pa.*, ps.*
    FROM #ToUpdate u
    JOIN dbo.RolmarProducts p ON p.Id = u.ProductId
    LEFT JOIN dbo.ProductApplications pa ON pa.ProductId = p.Id
    LEFT JOIN dbo.ProductSpecifications ps ON ps.ProductId = p.Id
    ORDER BY p.Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_SetParametersChecked
    @ProductIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET ParametersCheckedAt = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    JOIN OPENJSON(@ProductIds) ids ON CAST(ids.[value] AS INT) = p.Id;
END
GO

-- Wstawia tylko brakujące parametry. Istniejącej wartości nie nadpisujemy - mogła zostać
-- poprawiona na podstawie odpowiedzi Allegro ("oczekiwana wartość ...").
CREATE OR ALTER PROCEDURE dbo.RolmarProductParameters_Insert
    @ProductId INT,
    @CategoryParameterId INT,
    @Value NVARCHAR(MAX),
    @IsForProduct BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.RolmarProductParameters
        WHERE ProductId = @ProductId AND CategoryParameterId = @CategoryParameterId)
    BEGIN
        INSERT INTO dbo.RolmarProductParameters (ProductId, CategoryParameterId, Value, IsForProduct)
        VALUES (@ProductId, @CategoryParameterId, @Value, @IsForProduct);
    END
END
GO

-- Poprawka wartości po nazwie (z komunikatu Allegro) - we wszystkich kategoriach produktu,
-- bo oferta może stać w innej kategorii niż domyślna.
CREATE OR ALTER PROCEDURE dbo.RolmarProductParameters_UpdateByName
    @ProductId INT,
    @ParameterName NVARCHAR(255),
    @Value NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE pp
    SET pp.Value = @Value
    FROM dbo.RolmarProductParameters pp
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    WHERE pp.ProductId = @ProductId
      AND cp.Name = @ParameterName
      AND pp.Value <> @Value;

    SELECT @@ROWCOUNT AS UpdatedRows;
END
GO

-- ============================================================ produkt z katalogu Allegro

-- Allegro odrzuciło ofertę za brak parametrów obowiązkowych produktu: przy kolejnej aktualizacji
-- wysyłamy je razem z id produktu z katalogu i wymuszamy ponowne wyliczenie parametrów.
-- Zwraca 1, gdy produkt już wcześniej miał ten tryb (czyli samo przełączenie nie pomoże).
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_RequestCatalogParameters
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Already BIT = (SELECT SendCatalogParameters FROM dbo.RolmarProducts WHERE Id = @ProductId);

    UPDATE dbo.RolmarProducts
    SET SendCatalogParameters = 1,
        ParametersCheckedAt = NULL
    WHERE Id = @ProductId;

    SELECT ISNULL(@Already, 0);
END
GO

-- Allegro odłączyło produkt z katalogu od oferty ("Produkt nie pasuje do oferty"). Zapamiętujemy jego
-- id, żeby wyszukiwanie nie podpięło go ponownie, i czyścimy id przy produkcie i przy ofercie.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_RejectAllegroProduct
    @ProductId INT,
    @OfferId NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Rejected TABLE (Id NVARCHAR(500));

    INSERT INTO @Rejected (Id)
    SELECT AllegroId FROM dbo.RolmarProducts WHERE Id = @ProductId AND NULLIF(AllegroId, '') IS NOT NULL
    UNION
    SELECT ProductId FROM dbo.AllegroOffers WHERE Id = @OfferId AND NULLIF(ProductId, '') IS NOT NULL;

    UPDATE p
    SET RejectedAllegroIds = (
            SELECT STRING_AGG(x.Id, ',')
            FROM (
                SELECT LTRIM(RTRIM([value])) AS Id FROM STRING_SPLIT(ISNULL(p.RejectedAllegroIds, ''), ',') WHERE LTRIM(RTRIM([value])) <> ''
                UNION
                SELECT Id FROM @Rejected
            ) x),
        AllegroId = NULL
    FROM dbo.RolmarProducts p
    WHERE p.Id = @ProductId;

    IF @OfferId IS NOT NULL
        UPDATE dbo.AllegroOffers SET ProductId = NULL WHERE Id = @OfferId;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetRejectedAllegroIds
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT LTRIM(RTRIM([value]))
    FROM dbo.RolmarProducts p
    CROSS APPLY STRING_SPLIT(ISNULL(p.RejectedAllegroIds, ''), ',')
    WHERE p.Id = @ProductId AND LTRIM(RTRIM([value])) <> '';
END
GO

-- ============================================================ parametry z kategorią
-- Produkt ma teraz parametry w kilku kategoriach (domyślna + kategorie ofert). Procedury zwracają
-- kategorię parametru, a kod wybiera zestaw pasujący do kategorii oferty - inaczej Allegro dostałoby
-- parametry z cudzej kategorii, a brakujące obowiązkowe dalej by nie doszły.
-- Treść procedur jak w migracji 27, z dopisanym CategoryId parametru (i SendCatalogParameters).

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
        OfferProductId NVARCHAR(500) NULL,
        ExternalId NVARCHAR(255) NULL,
        OfferName NVARCHAR(255) NOT NULL,
        CategoryId INT NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        StartingAt DATETIME2 NOT NULL,
        DeliveryName NVARCHAR(255) NULL,
        ProductId INT NOT NULL,
        AllegroId NVARCHAR(255) NULL,
        AllegroName NVARCHAR(255) NULL,
        Code NVARCHAR(255) NOT NULL,
        CustomerCode NVARCHAR(255) NULL,
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
        AcceptedPriceGross DECIMAL(18, 2) NULL,
        PriceDropDetectedAt DATETIME2 NULL,
        DefaultAllegroCategory INT NOT NULL,
        Package DECIMAL(18, 2) NOT NULL,
        DeliveryType INT NULL,
        ShippingClass NVARCHAR(50) NULL,
        CreatedDate DATETIME2 NOT NULL,
        UpdatedDate DATETIME2 NOT NULL,
        OfferPrice DECIMAL(18, 2) NOT NULL,
        SendCatalogParameters BIT NOT NULL
    );

    INSERT INTO #OffersWithProducts
    (
        OfferId, OfferProductId, ExternalId, OfferName, CategoryId, Status, StartingAt, DeliveryName,
        ProductId, AllegroId, AllegroName, Code, CustomerCode, ProductName, Description, Ean, Weight, Fits, SupplierName,
        Substitutes, InStock, Unit, CurrencyPrice, PriceNet, PriceGross, AcceptedPriceGross,
        PriceDropDetectedAt, DefaultAllegroCategory, Package, DeliveryType, ShippingClass, CreatedDate, UpdatedDate, OfferPrice,
        SendCatalogParameters
    )
    SELECT
        ao.Id, ao.ProductId, ao.ExternalId, ao.Name, ao.CategoryId, ao.Status, ao.StartingAt, ao.DeliveryName,
        p.Id, p.AllegroId, p.AllegroName, p.Code, p.CustomerCode, p.Name, p.Description,
        p.Ean, p.Weight, p.Fits, p.SupplierName, p.Substitutes, p.InStock, p.Unit,
        p.CurrencyPrice, p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DefaultAllegroCategory, p.Package, p.DeliveryType, p.ShippingClass,
        p.CreatedDate, p.UpdatedDate, ao.Price, p.SendCatalogParameters
    FROM dbo.AllegroOffers ao
    INNER JOIN dbo.RolmarProducts p ON p.Code = ao.ExternalId AND p.IntegrationCompany = @IntegrationCompany
    WHERE ao.Status IN ('ACTIVE', 'ENDED', 'INACTIVE') AND ao.Account = @Account
        AND ao.DeliveryName IN (SELECT value FROM STRING_SPLIT(@DeliveryNames, ','))
        AND p.PriceNet > 0
        AND p.BlockedReturn = 0;

    SELECT
        OfferId AS Id,
        OfferProductId AS ProductId,
        ExternalId,
        OfferName AS Name,
        CategoryId,
        Status,
        StartingAt,
        DeliveryName,
        OfferPrice as Price,
        ProductId as Id,
        AllegroId,
        AllegroName,
        Code,
        CustomerCode,
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
        AcceptedPriceGross,
        PriceDropDetectedAt,
        DefaultAllegroCategory,
        Package,
        DeliveryType,
        ShippingClass,
        CreatedDate,
        UpdatedDate,
        SendCatalogParameters
    FROM #OffersWithProducts;

    SELECT ai.*
    FROM dbo.AllegroImages ai
    WHERE ai.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts)
      AND ai.Connected = 1 AND ai.Account = @Account;

    SELECT ps.*
    FROM dbo.ProductSpecifications ps
    WHERE ps.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT pa.*
    FROM dbo.ProductApplications pa
    WHERE pa.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT pack.*
    FROM dbo.ProductPackages pack
    WHERE pack.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);

    SELECT param.Id,
           param.ProductId,
           param.CategoryParameterId,
           param.Value,
           param.IsForProduct,
           catParam.Name,
           catParam.CategoryId
    FROM dbo.RolmarProductParameters param
    JOIN dbo.CategoryParameters catParam ON param.CategoryParameterId = catParam.Id
    WHERE param.ProductId IN (SELECT DISTINCT ProductId FROM #OffersWithProducts);
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpload
    @MinProductStock INT,
    @MinProductPrice DECIMAL(15,4),
    @IntegrationCompany INT,
    @Account INT,
    @Categories NVARCHAR(MAX) = NULL,
    @RequireDetails BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    DECLARE @HasCategoryFilter BIT =
        CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]'))) THEN 1 ELSE 0 END;

    IF @HasCategoryFilter = 1
        EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

    SELECT
        p.Id, p.Code, p.CustomerCode, p.Name, p.Description, p.Ean, p.Weight, p.Fits, p.SupplierName, p.InStock,
        p.Unit, p.CurrencyPrice, p.PriceNet, p.PriceGross, p.AcceptedPriceGross, p.PriceDropDetectedAt,
        p.DefaultAllegroCategory, p.Package,
        p.CreatedDate, p.UpdatedDate, p.Substitutes, p.AllegroId, p.AllegroName, p.DeliveryType, p.ShippingClass, p.BlockedReturn,
        ps.Id, ps.ProductId, ps.Name, ps.Value, ps.UnitName,
        pp.Id, pp.ProductId, pp.CategoryParameterId, cp.Name, pp.Value, pp.IsForProduct, cp.CategoryId,
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
      AND p.BlockedReturn = 0
      AND (@RequireDetails = 0 OR p.DetailsFetchedAt IS NOT NULL)
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id))
      -- Kategorie zapisujemy razem ze szczegolami, wiec ich brak znaczy "szczegoly niepobrane":
      -- produkt bez opisu i zdjec nie moze trafic do wystawienia.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
    ORDER BY p.Id;

    DROP TABLE #Allowed;
END
GO
