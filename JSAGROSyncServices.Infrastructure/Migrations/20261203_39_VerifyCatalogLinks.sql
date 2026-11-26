-- Weryfikacja istniejących powiązań z katalogiem Allegro.
--
-- Powiązania sprzed reguł potwierdzania trafienia (październik) nigdy nie były sprawdzane - KR10 (odstraszacz
-- na krety) stał pod radiem Blaupunkt "KR 10 WH". Krok wyszukiwania sprawdza teraz powiązania niezweryfikowane
-- albo sprawdzone dawniej niż @RecheckAfterDays dni tymi samymi regułami co nowe dopasowania; błędne odpina,
-- a produkt od razu szuka ponownie.

IF COL_LENGTH('dbo.RolmarProducts', 'AllegroIdVerifiedAt') IS NULL
    ALTER TABLE dbo.RolmarProducts ADD AllegroIdVerifiedAt DATETIME2(7) NULL;
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetLinksToVerify
    @IntegrationCompany INT,
    @RecheckAfterDays INT = 30,
    @Top INT = 50000
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
        Id, Code, CustomerCode, Name, Ean, SupplierName, Substitutes, AllegroId, DefaultAllegroCategory
    FROM dbo.RolmarProducts
    WHERE IntegrationCompany = @IntegrationCompany
      AND NULLIF(AllegroId, '') IS NOT NULL
      AND (AllegroIdVerifiedAt IS NULL
           OR AllegroIdVerifiedAt < DATEADD(DAY, -@RecheckAfterDays, SYSUTCDATETIME()))
    ORDER BY CASE WHEN AllegroIdVerifiedAt IS NULL THEN 0 ELSE 1 END, AllegroIdVerifiedAt, Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_MarkLinksVerified
    @ProductIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET AllegroIdVerifiedAt = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    JOIN OPENJSON(@ProductIds) ids ON CAST(ids.[value] AS INT) = p.Id;
END
GO

-- Jak w migracji 36: nowe powiązanie z wyszukiwania jest już zweryfikowane, wyczyszczone - nie.
CREATE OR ALTER PROCEDURE dbo.RolmarProducts_UpdateAllegroId
    @ProductId INT,
    @AllegroId NVARCHAR(255),
    @CategoryId INT,
    @AllegroName NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RolmarProducts
    SET AllegroId = @AllegroId,
        AllegroIdVerifiedAt = CASE WHEN NULLIF(@AllegroId, '') IS NULL THEN NULL ELSE SYSUTCDATETIME() END,
        ParametersCheckedAt = CASE WHEN DefaultAllegroCategory = @CategoryId THEN ParametersCheckedAt ELSE NULL END,
        DefaultAllegroCategory = @CategoryId,
        AllegroName = ISNULL(@AllegroName, AllegroName)
    WHERE Id = @ProductId;
END
GO

-- Jak w migracji 33, z wyczyszczeniem znacznika weryfikacji.
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
        AllegroIdVerifiedAt = NULL,
        SendCatalogParameters = 0,
        AllegroSearchedAt = NULL
    WHERE Id = @ProductId;

    IF @OfferId IS NOT NULL
        UPDATE dbo.AllegroOffers SET ProductId = NULL WHERE Id = @OfferId;
END
GO
