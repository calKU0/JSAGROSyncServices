-- Klucze główne AllegroBlockedImages i AllegroCatalogProductImages mieściły się w NVARCHAR(500), czyli do 1000 bajtów,
-- a klucz indeksu klastrowanego w SQL Server może mieć najwyżej 900 bajtów - dłuższa wartość nie dałaby się zapisać.
-- Zmniejszamy kolumny: adresy zdjęć Allegro mają ok. 70 znaków, id produktu z katalogu to GUID (36 znaków).
-- Baza, która dostała już poprawioną migrację 43, nie wymaga zmian - wtedy ten skrypt nic nie robi.

IF COL_LENGTH('dbo.AllegroBlockedImages', 'Url') > 900
BEGIN
    ALTER TABLE dbo.AllegroBlockedImages DROP CONSTRAINT PK_AllegroBlockedImages;
    ALTER TABLE dbo.AllegroBlockedImages ALTER COLUMN Url NVARCHAR(450) NOT NULL;
    ALTER TABLE dbo.AllegroBlockedImages ADD CONSTRAINT PK_AllegroBlockedImages PRIMARY KEY (Url);
END
GO

IF COL_LENGTH('dbo.AllegroCatalogProductImages', 'CatalogProductId') > 200
BEGIN
    ALTER TABLE dbo.AllegroCatalogProductImages DROP CONSTRAINT PK_AllegroCatalogProductImages;
    ALTER TABLE dbo.AllegroCatalogProductImages ALTER COLUMN CatalogProductId NVARCHAR(100) NOT NULL;
    ALTER TABLE dbo.AllegroCatalogProductImages ADD CONSTRAINT PK_AllegroCatalogProductImages PRIMARY KEY (CatalogProductId);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroBlockedImages_Add
    @Url NVARCHAR(450),
    @Reason NVARCHAR(500) = NULL,
    @OfferId NVARCHAR(128) = NULL,
    @Code NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AllegroBlockedImages WHERE Url = @Url)
        INSERT INTO dbo.AllegroBlockedImages (Url, Reason, OfferId, Code) VALUES (@Url, @Reason, @OfferId, @Code);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroCatalogProductImages_Upsert
    @CatalogProductId NVARCHAR(100),
    @Images NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AllegroCatalogProductImages SET Images = @Images, FetchedAt = SYSUTCDATETIME() WHERE CatalogProductId = @CatalogProductId;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.AllegroCatalogProductImages (CatalogProductId, Images) VALUES (@CatalogProductId, @Images);
END
GO
