-- Szczegoly ofert pobieralismy tylko dla ofert ACTIVE, wiec oferta INACTIVE nigdy nie
-- dostawala zapisanego ProductId. Takie oferty i tak aktualizujemy, a patch bez id produktu
-- Allegro odrzuca bledem "ProductNotFoundException - Product with ID: ... not found",
-- podajac id produktu, do ktorego oferta jest u nich nadal podpieta. Bez pobranych szczegolow
-- nie mielismy tego id u siebie, wiec nie bylo czego wyczyscic i ta sama oferta wracala
-- z tym samym bledem w kazdym cyklu.

CREATE OR ALTER PROCEDURE [dbo].[AllegroOffers_GetWithoutDetails]
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.AllegroOffers o
    WHERE o.DetailsFetchedAt IS NULL
      -- ENDED pomijamy: zakonczonej oferty nie aktualizujemy, wiec jej szczegoly sa nam zbedne.
      AND o.Status IN ('ACTIVE', 'ACTIVATING', 'INACTIVE')
      AND o.Account = @Account
    ORDER BY o.StartingAt DESC;
END
GO
