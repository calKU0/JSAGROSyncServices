-- Rolmar (oba konta): parametry produktu bierzemy wyłącznie z produktu z katalogu Allegro.
--
-- Wyliczane przez nas parametry produktu (zgadywane "Marka maszyny", "Szerokość głowicy" itd.)
-- i tak nie usuwały błędu "Uzupełnij parametry obowiązkowe" - brakowało ich w samym produkcie
-- z katalogu. Teraz w bazie Rolmar trzyma tylko obowiązkowe parametry oferty, a produkt
-- z katalogu bez kompletu parametrów jest podmieniany albo odpinany (oferta kończona).
--
-- Przy okazji (Gąska):
--  * poprawka wartości parametru wskazanej przez Allegro dokłada brakujący wiersz w kategorii
--    oferty - "Producent części" `John Deere` zamiast `JAG` wracał w każdym cyklu,
--  * "Marka" z wartością niejednoznaczną `Inna` (Allegro jej nie przyjmuje) albo uciętą `bez`
--    dostaje "bez marki".

-- ============================================================ sprzątanie parametrów Rolmaru

DELETE pp
FROM dbo.RolmarProductParameters pp
JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
JOIN dbo.RolmarProducts p ON p.Id = pp.ProductId
WHERE p.IntegrationCompany = 1
  AND NOT (cp.Required = 1 AND cp.DescribesProduct = 0);
GO

UPDATE pp
SET pp.IsForProduct = 0
FROM dbo.RolmarProductParameters pp
JOIN dbo.RolmarProducts p ON p.Id = pp.ProductId
WHERE p.IntegrationCompany = 1 AND pp.IsForProduct = 1;
GO

UPDATE dbo.RolmarProducts
SET SendCatalogParameters = 0
WHERE IntegrationCompany = 1 AND SendCatalogParameters = 1;
GO

-- ============================================================ "Marka" spoza słownika

UPDATE pp
SET pp.Value = N'bez marki'
FROM dbo.RolmarProductParameters pp
JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
WHERE cp.Name = N'Marka'
  AND pp.Value IN (N'Inna', N'bez')
  AND EXISTS (
      SELECT 1 FROM dbo.CategoryParameterValues v
      WHERE v.CategoryParameterId = cp.Id AND v.Value = N'bez marki');
GO

-- ============================================================ odpięcie produktu z katalogu

-- Produkt z katalogu bez kompletu parametrów obowiązkowych i bez zastępcy: czyścimy id przy
-- produkcie i ofercie oraz znacznik wyszukiwania, żeby kolejny cykl od razu szukał innego.
-- @Reject = 1 zapamiętuje id jako odrzucone (produkt sprzeczny z ofertą, np. inny EAN).
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UnlinkAllegroProduct
    @ProductId INT,
    @OfferId NVARCHAR(128) = NULL,
    @Reject BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @Reject = 1
        EXEC dbo.RolmarProducts_RejectAllegroProduct @ProductId = @ProductId, @OfferId = @OfferId;

    UPDATE dbo.RolmarProducts
    SET AllegroId = NULL,
        SendCatalogParameters = 0,
        AllegroSearchedAt = NULL
    WHERE Id = @ProductId;

    IF @OfferId IS NOT NULL
        UPDATE dbo.AllegroOffers SET ProductId = NULL WHERE Id = @OfferId;
END
GO

-- ============================================================ poprawka parametru po nazwie

-- Wartość wskazana przez Allegro ("change the value to `JAG`"): poprawia istniejące wiersze i dokłada
-- brakujące w kategorii domyślnej produktu i w kategorii oferty - inaczej parametr z kategorii oferty
-- nigdy nie szedł i błąd wracał w każdym cyklu. Parametr produktu przy produkcie z katalogu włącza
-- wysyłanie parametrów produktu razem z id. Rolmar (IntegrationCompany = 1) trzyma wyłącznie
-- obowiązkowe parametry oferty, więc dla niego innych nie zapisujemy.
-- Zwraca liczbę zmian (0 = nic do poprawienia).
CREATE OR ALTER PROCEDURE dbo.RolmarProductParameters_SetByName
    @ProductId INT,
    @OfferCategoryId INT,
    @ParameterName NVARCHAR(255),
    @Value NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Changed INT = 0;
    DECLARE @Company INT = (SELECT IntegrationCompany FROM dbo.RolmarProducts WHERE Id = @ProductId);

    UPDATE pp
    SET pp.Value = @Value
    FROM dbo.RolmarProductParameters pp
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    WHERE pp.ProductId = @ProductId
      AND cp.Name = @ParameterName
      AND pp.Value <> @Value;

    SET @Changed += @@ROWCOUNT;

    INSERT INTO dbo.RolmarProductParameters (ProductId, CategoryParameterId, Value, IsForProduct)
    SELECT @ProductId, cp.Id, @Value, cp.DescribesProduct
    FROM dbo.CategoryParameters cp
    JOIN dbo.RolmarProducts p ON p.Id = @ProductId
    WHERE cp.Name = @ParameterName
      AND cp.CategoryId IN (p.DefaultAllegroCategory, @OfferCategoryId)
      AND (@Company <> 1 OR (cp.Required = 1 AND cp.DescribesProduct = 0))
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolmarProductParameters x
          WHERE x.ProductId = @ProductId AND x.CategoryParameterId = cp.Id);

    SET @Changed += @@ROWCOUNT;

    IF @Company <> 1
    BEGIN
        UPDATE p
        SET SendCatalogParameters = 1
        FROM dbo.RolmarProducts p
        WHERE p.Id = @ProductId
          AND p.SendCatalogParameters = 0
          AND EXISTS (
              SELECT 1
              FROM dbo.RolmarProductParameters pp
              JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
              WHERE pp.ProductId = @ProductId AND cp.Name = @ParameterName AND pp.IsForProduct = 1);

        SET @Changed += @@ROWCOUNT;
    END

    SELECT @Changed;
END
GO

-- ============================================================ produkty do wystawienia

-- Jak w migracji 30, z @RequireParameters: przy parametrach z katalogu (Rolmar) produkt nie musi mieć
-- żadnego wiersza parametrów - kategoria może nie mieć obowiązkowych parametrów oferty.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetToUpload
    @MinProductStock INT,
    @MinProductPrice DECIMAL(15,4),
    @IntegrationCompany INT,
    @Account INT,
    @Categories NVARCHAR(MAX) = NULL,
    @RequireDetails BIT = 0,
    @RequireParameters BIT = 1
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
    LEFT JOIN (dbo.RolmarProductParameters pp
               JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId)
           ON pp.ProductId = p.Id
    LEFT JOIN dbo.ProductApplications ap ON ap.ProductId = p.Id
    LEFT JOIN dbo.ProductPackages pack ON pack.ProductId = p.Id
    LEFT JOIN dbo.AllegroOffers ao ON ao.ExternalId = p.Code AND ao.Account = @Account
    WHERE p.InStock >= @MinProductStock
      AND p.PriceNet >= @MinProductPrice
      AND NULLIF(p.DefaultAllegroCategory, 0) IS NOT NULL
      AND ao.Id IS NULL
      AND p.IntegrationCompany = @IntegrationCompany
      AND p.BlockedReturn = 0
      AND (@RequireParameters = 0 OR pp.Id IS NOT NULL)
      AND (@RequireDetails = 0 OR p.DetailsFetchedAt IS NOT NULL)
      AND (@HasCategoryFilter = 0 OR EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id))
      -- Kategorie zapisujemy razem ze szczegolami, wiec ich brak znaczy "szczegoly niepobrane":
      -- produkt bez opisu i zdjec nie moze trafic do wystawienia.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
    ORDER BY p.Id;

    DROP TABLE #Allowed;
END
GO
