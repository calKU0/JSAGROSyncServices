-- Dopisuje jedno id produktu z katalogu do odrzuconych, bez odpinania bieżącego powiązania.
-- Używane, gdy produkt z katalogu ma zdjęcie zablokowane przez Allegro: oferta przechodzi na inny
-- produkt, a ten nie wraca w wyszukiwaniu. Drugie konto, którego oferta wciąż stoi na odrzuconym
-- produkcie, przepina ją na AllegroId produktu (ten sam productId na obu kontach).

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_AddRejectedAllegroId
    @ProductId INT,
    @AllegroId NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(@AllegroId)), '') IS NULL
        RETURN;

    UPDATE p
    SET RejectedAllegroIds = (
            SELECT STRING_AGG(x.Id, ',')
            FROM (
                SELECT LTRIM(RTRIM([value])) AS Id FROM STRING_SPLIT(ISNULL(p.RejectedAllegroIds, ''), ',') WHERE LTRIM(RTRIM([value])) <> ''
                UNION
                SELECT LTRIM(RTRIM(@AllegroId))
            ) x)
    FROM dbo.RolmarProducts p
    WHERE p.Id = @ProductId;
END
GO
