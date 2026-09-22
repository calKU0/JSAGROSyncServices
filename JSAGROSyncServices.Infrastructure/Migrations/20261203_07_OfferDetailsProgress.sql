-- Oferta bez opisu byla traktowana jako "szczegoly niepobrane". Oferty produktowe
-- biora opis z produktu Allegro i wlasnego nie maja, wiec wracaly do kolejki w kazdym
-- cyklu - serwis pobieral w kolko te same 3000 ofert i nigdy nie szedl dalej.
-- Znacznik pobrania trzymamy teraz wprost na ofercie.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AllegroOffers') AND name = 'DetailsFetchedAt'
)
BEGIN
    ALTER TABLE dbo.AllegroOffers ADD DetailsFetchedAt DATETIME2 NULL;
END
GO

-- Oferty, ktore maja juz opis albo parametry, zostaly pobrane wczesniej - nie pobieramy ich ponownie.
UPDATE o
SET DetailsFetchedAt = SYSUTCDATETIME()
FROM dbo.AllegroOffers o
WHERE o.DetailsFetchedAt IS NULL
  AND (EXISTS (SELECT 1 FROM dbo.AllegroOfferDescriptions d WHERE d.OfferId = o.Id)
       OR EXISTS (SELECT 1 FROM dbo.AllegroOfferAttributes a WHERE a.OfferId = o.Id));
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetWithoutDetails]
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroOffers o
    WHERE o.DetailsFetchedAt IS NULL
      AND o.Status = 'ACTIVE'
      AND o.Account = @Account
    ORDER BY o.StartingAt DESC;
END
GO

IF TYPE_ID('dbo.OfferIdList') IS NULL
BEGIN
    CREATE TYPE dbo.OfferIdList AS TABLE (OfferId NVARCHAR(50) NOT NULL PRIMARY KEY);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_MarkDetailsFetched]
    @OfferIds dbo.OfferIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE o
    SET DetailsFetchedAt = SYSUTCDATETIME()
    FROM dbo.AllegroOffers o
    JOIN @OfferIds ids ON ids.OfferId = o.Id;
END
GO

-- Aktualizacja oferty moze zmienic opis, wiec przy zmianie danych kasujemy znacznik,
-- zeby szczegoly zostaly pobrane ponownie.
CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_ClearDetailsFetched]
    @OfferId NVARCHAR(50),
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AllegroOffers
    SET DetailsFetchedAt = NULL
    WHERE Id = @OfferId AND Account = @Account;
END
GO
