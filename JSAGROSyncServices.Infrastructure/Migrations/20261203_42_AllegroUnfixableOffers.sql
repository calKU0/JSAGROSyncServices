-- Oferty, których serwis nie umie naprawić przez API (zablokowane zdjęcie w produkcie z katalogu, brak
-- wariantu albo wariant odrzucony). Lista przeżywa cykle i restarty - podsumowanie na końcu każdego cyklu
-- wypisuje wszystkie, także z poprzednich cykli. Wpis znika, gdy aktualizacja oferty przejdzie albo
-- oferty nie ma już na koncie.

IF OBJECT_ID('dbo.AllegroUnfixableOffers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllegroUnfixableOffers
    (
        OfferId NVARCHAR(128) NOT NULL,
        Account INT NOT NULL,
        Code NVARCHAR(255) NOT NULL,
        CatalogProductId NVARCHAR(500) NULL,
        Reason NVARCHAR(1000) NOT NULL,
        FirstSeenAt DATETIME2(7) NOT NULL CONSTRAINT DF_AllegroUnfixableOffers_FirstSeenAt DEFAULT (SYSUTCDATETIME()),
        LastSeenAt DATETIME2(7) NOT NULL CONSTRAINT DF_AllegroUnfixableOffers_LastSeenAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AllegroUnfixableOffers PRIMARY KEY (OfferId)
    );

    CREATE INDEX IX_AllegroUnfixableOffers_Account ON dbo.AllegroUnfixableOffers (Account);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroUnfixableOffers_Get
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT OfferId, Code, CatalogProductId, Reason, FirstSeenAt, LastSeenAt
    FROM dbo.AllegroUnfixableOffers
    WHERE Account = @Account
    ORDER BY Code, OfferId;
END
GO

-- @Items: [{"OfferId": "...", "Code": "...", "CatalogProductId": "...", "Reason": "..."}] - Reason = null usuwa wpis
-- (oferta naprawiona). Przy okazji czyścimy wpisy ofert, których już nie ma na koncie.
CREATE OR ALTER PROCEDURE dbo.AllegroUnfixableOffers_Save
    @Account INT,
    @Items NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Items
    (
        OfferId NVARCHAR(128) NOT NULL PRIMARY KEY,
        Code NVARCHAR(255) NULL,
        CatalogProductId NVARCHAR(500) NULL,
        Reason NVARCHAR(1000) NULL
    );

    INSERT INTO #Items (OfferId, Code, CatalogProductId, Reason)
    SELECT OfferId, MAX(Code), MAX(CatalogProductId), MAX(Reason)
    FROM OPENJSON(@Items) WITH (
        OfferId NVARCHAR(128) '$.OfferId',
        Code NVARCHAR(255) '$.Code',
        CatalogProductId NVARCHAR(500) '$.CatalogProductId',
        Reason NVARCHAR(1000) '$.Reason')
    WHERE OfferId IS NOT NULL
    GROUP BY OfferId;

    DELETE u
    FROM dbo.AllegroUnfixableOffers u
    JOIN #Items i ON i.OfferId = u.OfferId
    WHERE i.Reason IS NULL;

    UPDATE u
    SET Code = ISNULL(i.Code, u.Code),
        CatalogProductId = i.CatalogProductId,
        Reason = i.Reason,
        Account = @Account,
        LastSeenAt = SYSUTCDATETIME()
    FROM dbo.AllegroUnfixableOffers u
    JOIN #Items i ON i.OfferId = u.OfferId
    WHERE i.Reason IS NOT NULL;

    INSERT INTO dbo.AllegroUnfixableOffers (OfferId, Account, Code, CatalogProductId, Reason)
    SELECT i.OfferId, @Account, ISNULL(i.Code, '-'), i.CatalogProductId, i.Reason
    FROM #Items i
    WHERE i.Reason IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.AllegroUnfixableOffers u WHERE u.OfferId = i.OfferId);

    DELETE u
    FROM dbo.AllegroUnfixableOffers u
    WHERE u.Account = @Account
      AND NOT EXISTS (SELECT 1 FROM dbo.AllegroOffers o WHERE o.Id = u.OfferId);
END
GO
