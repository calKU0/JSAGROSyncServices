-- Zmiana zestawu zdjec u dostawcy musi uniewazniac adresy wyslane do Allegro na kazdym koncie.
--
-- Pliki zdjec leza w jednym katalogu i pobiera je tylko to konto, ktore ciagnie dane od dostawcy.
-- Adresy wgranych zdjec sa juz per konto, bo kazde konto wgrywa pliki na swoje. Dotad sprzatanie
-- po zmianie plikow tez bylo per konto - czyli tylko na koncie pobierajacym. Drugie konto
-- zostawalo ze starymi adresami, a wysylamy tylko te zdjecia, ktorych jeszcze nie ma, wiec jego
-- oferty trzymaly stara galerie bez konca.
--
-- @Account = NULL znaczy "wszystkie konta". Parametr zostaje obowiazkowy, zeby pominiecie go
-- nie kasowalo przez przypadek adresow obu kont.

CREATE OR ALTER PROCEDURE dbo.AllegroImages_DeleteByProductId
    @ProductId INT,
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.AllegroImages
    WHERE ProductId = @ProductId
      AND (@Account IS NULL OR Account = @Account);

    SELECT @@ROWCOUNT AS DeletedCount;
END
GO
