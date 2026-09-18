-- Krok "Allegro products search" pytal katalog Allegro o KAZDY produkt bez AllegroId,
-- po dwa zapytania (EAN i kod), w kazdym cyklu. Produkty, ktorych w katalogu po prostu nie ma,
-- byly odpytywane w kolko - kilkaset zapytan na cykl bez zadnego efektu.
-- Zapamietujemy date ostatniego szukania i ponawiamy dopiero po ustalonym czasie.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.RolmarProducts') AND name = 'AllegroSearchedAt'
)
BEGIN
    ALTER TABLE dbo.RolmarProducts ADD AllegroSearchedAt DATETIME2 NULL;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_GetWithoutAllegroId]
    @IntegrationCompany INT,
    @RetryAfterDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.RolmarProducts
    WHERE AllegroId IS NULL
      AND IntegrationCompany = @IntegrationCompany
      AND (AllegroSearchedAt IS NULL
           OR AllegroSearchedAt < DATEADD(DAY, -@RetryAfterDays, SYSUTCDATETIME()));
END
GO

IF TYPE_ID('dbo.ProductIdList') IS NULL
BEGIN
    CREATE TYPE dbo.ProductIdList AS TABLE (ProductId INT NOT NULL PRIMARY KEY);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RolmarProducts_MarkAllegroSearched]
    @ProductIds dbo.ProductIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET AllegroSearchedAt = SYSUTCDATETIME()
    FROM dbo.RolmarProducts p
    JOIN @ProductIds ids ON ids.ProductId = p.Id;
END
GO
