-- Jeden produkt z katalogu Allegro = jeden nasz produkt.
--
-- Allegro pozwala mieć tylko jedną aktywną ofertę danego produktu z katalogu. Wyszukiwanie podpinało
-- ten sam produkt pod dwa nasze (W-073609 i W-0736090, T-W-PNR003 i T-W-NR005...), więc druga oferta
-- wracała w każdym cyklu z błędem "Przedmiot, który chcesz wystawić znajduje się już na liście Twoich
-- aktywnych ofert". Wyszukiwanie pomija teraz produkty z katalogu podpięte już pod inny nasz produkt -
-- dowolnego dostawcy, bo Gąskę i Rolmar sprzedajemy na tych samych kontach.
--
-- Do tego zmiana kategorii produktu (oferty przenosimy do kategorii produktu z katalogu) nie kasuje już
-- parametrów, tylko zleca ich przeliczenie dla nowej kategorii.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RolmarProducts') AND name = 'IX_RolmarProducts_AllegroId')
    CREATE INDEX IX_RolmarProducts_AllegroId ON dbo.RolmarProducts (AllegroId);
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_IsAllegroIdTaken
    @AllegroId NVARCHAR(255),
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1 FROM dbo.RolmarProducts
        WHERE AllegroId = @AllegroId AND Id <> @ProductId
    ) THEN 1 ELSE 0 END AS BIT);
END
GO

-- ============================================================ zmiana kategorii produktu

-- Oferty produktu na wszystkich kontach przenosimy do jego kategorii (kategorii produktu z katalogu).
-- Parametry są liczone osobno dla każdej kategorii (migracja 30), więc zmiana kategorii nie może ich
-- kasować - zestaw kategorii, w której oferta jeszcze stoi, jest potrzebny do czasu przeniesienia.
-- Zerujemy za to ParametersCheckedAt: krok parametrów przeliczy produkt dla nowej kategorii od razu,
-- a nie dopiero po zmianie danych produktu.
CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_UpdateDefaultCategoryById]
    @ProductId INT,
    @CategoryId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RolmarProducts
    SET DefaultAllegroCategory = @CategoryId,
        ParametersCheckedAt = NULL,
        UpdatedDate = SYSUTCDATETIME()
    WHERE Id = @ProductId
      AND (DefaultAllegroCategory IS NULL OR DefaultAllegroCategory <> @CategoryId);
END
GO

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
        ParametersCheckedAt = CASE WHEN DefaultAllegroCategory = @CategoryId THEN ParametersCheckedAt ELSE NULL END,
        DefaultAllegroCategory = @CategoryId,
        AllegroName = ISNULL(@AllegroName, AllegroName)
    WHERE Id = @ProductId;
END
GO
