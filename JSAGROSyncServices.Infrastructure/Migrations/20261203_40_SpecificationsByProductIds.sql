-- Specyfikacja (napięcie, prąd, moc) potwierdza dopasowanie z katalogiem Allegro. Alternator Rolmaru
-- EX-260000EX (14 V, 44 A) stał pod produktem z katalogu z tym samym EAN-em, ale 70 A w opisie.
-- Wyszukiwanie i weryfikacja powiązań dostają produkty bez specyfikacji - dociągamy ją porcjami.

CREATE OR ALTER PROCEDURE dbo.ProductSpecifications_GetByProductIds
    @ProductIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.Id, s.ProductId, s.Name, s.Value, s.UnitName
    FROM dbo.ProductSpecifications s
    JOIN OPENJSON(@ProductIds) ids ON CAST(ids.[value] AS INT) = s.ProductId;
END
GO
