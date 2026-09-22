-- Oferty wystawione recznie na Allegro nie maja naszego kodu produktu (ExternalId).
-- Erli wymaga sku, wiec kazda taka oferta wracala z bledem "sku is not allowed to be empty" -
-- na koncie JSAGRO to ponad 800 ofert odrzucanych w kazdym cyklu.
-- Bez kodu produktu nie da sie ich zidentyfikowac w Erli, wiec nie wchodza do synchronizacji.

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetForErliCreation]
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroOffers o
    JOIN dbo.AllegroOfferDescriptions d ON o.Id = d.OfferId
    LEFT JOIN dbo.AllegroOfferAttributes a ON a.OfferId = o.Id
        AND a.Type IN ('dictionary', 'string', 'number', 'float', 'int')
    WHERE o.Account = @Account
      AND o.ExistsInErli = 0
      AND o.Status IN ('ACTIVE', 'ENDED')
      AND o.Price > 0
      AND o.Stock > 0
      AND o.CategoryId <> 0
      AND o.CategoryId IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(o.ExternalId)), '') IS NOT NULL;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetForErliUpdate]
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroOffers o
    JOIN dbo.AllegroOfferDescriptions d ON o.Id = d.OfferId
    LEFT JOIN dbo.AllegroOfferAttributes a ON a.OfferId = o.Id
        AND a.Type IN ('dictionary', 'string', 'number', 'float', 'int')
    WHERE o.Account = @Account
      AND o.ExistsInErli = 1
      AND o.Price > 0
      AND o.CategoryId <> 0
      AND o.CategoryId IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(o.ExternalId)), '') IS NOT NULL;
END
GO
