-- Oferty do zakończenia muszą obejmować także oferty już zakończone (ENDED).
--
-- Migracja 20260930_01 (wgrana po migracji 14, choć ma wcześniejszą nazwę) zawęziła procedurę do
-- ofert ACTIVE / INACTIVE. Lista ofert do zakończenia jest zarazem listą wykluczeń z aktualizacji,
-- a aktualizacja obejmuje oferty ENDED i nie patrzy na kategorie. Zakończona oferta produktu spoza
-- kategorii (albo wycofanego) wypadała więc z listy, trafiała do aktualizacji i patch ustawiał jej
-- z powrotem ACTIVE - w kolejnym cyklu była kończona ponownie. W logu: na przemian ~1900 i ~1000
-- ofert "outside the configured categories" w każdym cyklu, na obu kontach.
--
-- Komendę END serwis i tak wysyła tylko do ofert opublikowanych, więc zakończone tylko wykluczamy
-- z aktualizacji. Treść jak w 20260930_01, ze statusem ENDED w filtrze.

CREATE OR ALTER PROCEDURE dbo.AllegroOffers_GetOffersToEnd
    @IntegrationCompany INT,
    @Account INT,
    @DeliveryNames NVARCHAR(MAX),
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @DeliveryNames IS NULL OR LTRIM(RTRIM(@DeliveryNames)) = N''
        RETURN;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    DECLARE @HasCategoryFilter BIT =
        CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]'))) THEN 1 ELSE 0 END;

    IF @HasCategoryFilter = 1
        EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

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
      AND ao.Status IN ('ACTIVE', 'INACTIVE', 'ENDED')
      AND ao.DeliveryName IN (SELECT TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM [value]) FROM OPENJSON(@DeliveryNames))
      AND
      (
          -- Produkt wycofany u dostawcy - nie mamy juz czego sprzedac.
          p.ArchivedAt IS NOT NULL
          OR
          (
              @HasCategoryFilter = 1
              AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id)
              -- Konczymy tylko przy pewnosci, ze produkt jest poza kategoriami. Produkt bez
              -- przypisanej kategorii zostawiamy - to brak danych, a nie decyzja o wycofaniu.
              AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id)
          )
      );

    DROP TABLE #Allowed;
END
GO
