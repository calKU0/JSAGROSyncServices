-- Zdjęcia zablokowane przez Allegro ("Zdjęcie zostało zablokowane"). Allegro nadaje identycznym plikom ten sam
-- adres, więc jeden wpis obejmuje wszystkie produkty i oba konta. Patch oferty pomija te adresy w galerii, opisie
-- i w zdjęciach produktu z katalogu (productSet[].product.images) - odpinamy tylko to, co zablokowane.

IF OBJECT_ID('dbo.AllegroBlockedImages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllegroBlockedImages
    (
        Url NVARCHAR(450) NOT NULL,
        Reason NVARCHAR(500) NULL,
        OfferId NVARCHAR(128) NULL,
        Code NVARCHAR(255) NULL,
        DetectedAt DATETIME2(7) NOT NULL CONSTRAINT DF_AllegroBlockedImages_DetectedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AllegroBlockedImages PRIMARY KEY (Url)
    );
END
GO

-- Logo JS AGRO doklejane dawniej do galerii - zablokowane przez Allegro 2026-11-25.
MERGE dbo.AllegroBlockedImages AS t
USING (VALUES
    (N'https://a.allegroimg.com/original/11359c/1f5a470d495ab870bf81592181df'),
    (N'https://a.allegroimg.com/original/1109e9/c42164dd477780e2b604d1415e65')
) AS s (Url)
ON t.Url = s.Url
WHEN NOT MATCHED THEN INSERT (Url, Reason) VALUES (s.Url, N'Logo JS AGRO');
GO

-- Zdjęcia produktów z katalogu, w których wykryto zablokowane zdjęcie - patch wysyła wtedy listę bez niego.
IF OBJECT_ID('dbo.AllegroCatalogProductImages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllegroCatalogProductImages
    (
        CatalogProductId NVARCHAR(100) NOT NULL,
        Images NVARCHAR(MAX) NOT NULL,
        FetchedAt DATETIME2(7) NOT NULL CONSTRAINT DF_AllegroCatalogProductImages_FetchedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AllegroCatalogProductImages PRIMARY KEY (CatalogProductId)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroBlockedImages_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Url FROM dbo.AllegroBlockedImages;
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

CREATE OR ALTER PROCEDURE dbo.AllegroCatalogProductImages_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CatalogProductId, Images FROM dbo.AllegroCatalogProductImages;
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
