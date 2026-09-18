-- Parametr "Producent czesci" ma w Allegro dwa rozne ParameterId (127415 i 247835),
-- zaleznie od kategorii. Zaden produkt nie ma obu naraz, wiec dotychczasowa procedura
-- dla jednego z nich nie znajdowala CategoryParameterId, aktualizowala 0 wierszy,
-- a kod zglaszal to jako wyjatek. Efekt: co cykl ten sam produkt byl "naprawiany"
-- i zaraz potem lądowal w bledach.
--
-- Procedura zwraca teraz liczbe zaktualizowanych wierszy, zeby wywolujacy wiedzial,
-- czy poprawka faktycznie weszla.

CREATE OR ALTER PROCEDURE [dbo].[RolmarProductParameters_Update]
    @ProductId INT,
    @ParameterId INT,
    @Value NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE pp
    SET pp.Value = @Value
    FROM dbo.RolmarProductParameters pp
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    JOIN dbo.RolmarProducts p ON p.Id = pp.ProductId
    WHERE pp.ProductId = @ProductId
      AND cp.ParameterId = @ParameterId
      AND cp.CategoryId = p.DefaultAllegroCategory
      AND pp.Value <> @Value;   -- bez zmiany wartosci nie ruszamy wiersza

    SELECT @@ROWCOUNT AS UpdatedRows;
END
GO
