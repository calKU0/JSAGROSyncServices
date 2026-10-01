-- Kolejka szczegolow produktow byla nieuzywalna. Migracja InterCarsIntegration nadpisala
-- wersje z ProductArchiving wariantem bez @RefreshAfterDays, a kod wola procedure z tym
-- parametrem. Poniewaz DbUp stosuje skrypty w kolejnosci nazw, "20261203_15" jest ostatnim
-- slowem w kazdej bazie, w ktorej integracja Inter Cars zostala wdrozona - wiec kazde
-- wywolanie konczylo sie bledem "has too many arguments specified". Krok szczegolow
-- przerywal sie na czytaniu kolejki i zaden produkt nie dostawal wagi ani wymiarow,
-- a bez nich nie wolno wystawic oferty.
--
-- Kolejnosc jest czescia kontraktu: najpierw produkty bez szczegolow, potem najdawniej
-- odswiezane. Porcje na jeden przebieg wyznacza @Limit (ProductDetailsPerRun).

-- Kolumna pochodzi z ProductArchiving, ale ta migracja moze byc w danej bazie jeszcze
-- niezastosowana - zakladamy ja warunkowo, zeby procedura powstala niezaleznie od stanu bazy.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.RolmarProducts') AND name = 'ArchivedAt'
)
BEGIN
    ALTER TABLE dbo.RolmarProducts ADD ArchivedAt DATETIME2 NULL;
END
GO

CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetCodesForDetailUpdate
    @Limit INT,
    @IntegrationCompany INT,
    @RefreshAfterDays INT = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Take INT = CASE WHEN @Limit > 0 THEN @Limit ELSE 2147483647 END;
    DECLARE @Cutoff DATETIME2 = DATEADD(DAY, -ABS(@RefreshAfterDays), SYSUTCDATETIME());

    SELECT TOP (@Take) p.Code
    FROM dbo.RolmarProducts p
    WHERE p.IntegrationCompany = @IntegrationCompany
      AND p.ArchivedAt IS NULL
      -- Produkt ze swiezymi szczegolami pomijamy, inaczej porcja krecilaby sie po tych samych
      -- kodach i produkty z konca kolejki nigdy by na nie trafily.
      AND (p.DetailsFetchedAt IS NULL OR p.DetailsFetchedAt < @Cutoff)
    ORDER BY
        CASE WHEN p.DetailsFetchedAt IS NULL THEN 0 ELSE 1 END,
        p.DetailsFetchedAt,
        p.Code;
END
GO
