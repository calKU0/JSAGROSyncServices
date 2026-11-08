-- Skrót treści oferty Allegro (opis, zdjęcia, parametry, dostawa, warunki...) bez stanu, ceny
-- i statusu - jak w serwisie InPost. Gdy treść się nie zmieniła od ostatniego udanego pełnego
-- patcha, serwis wysyła sam stan, cenę i status. Osobna tabela, bo AllegroOffers jest co cykl
-- nadpisywana listą ofert z Allegro.

IF OBJECT_ID('dbo.AllegroOfferContentHashes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllegroOfferContentHashes
    (
        OfferId NVARCHAR(128) NOT NULL,
        Account INT NOT NULL,
        ContentHash NVARCHAR(64) NOT NULL,
        SentAt DATETIME2(7) NOT NULL CONSTRAINT DF_AllegroOfferContentHashes_SentAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AllegroOfferContentHashes PRIMARY KEY (OfferId)
    );

    CREATE INDEX IX_AllegroOfferContentHashes_Account ON dbo.AllegroOfferContentHashes (Account);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroOfferContentHashes_Get
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT OfferId, ContentHash, SentAt
    FROM dbo.AllegroOfferContentHashes
    WHERE Account = @Account;
END
GO

-- @Items: [{"OfferId": "...", "Hash": "..."}]. Hash = null usuwa wpis (po nieudanym patchu
-- kolejny cykl wysyła pełną strukturę). Przy okazji czyścimy wpisy ofert, których już nie ma.
CREATE OR ALTER PROCEDURE dbo.AllegroOfferContentHashes_Save
    @Account INT,
    @Items NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #Items (OfferId NVARCHAR(128) NOT NULL PRIMARY KEY, Hash NVARCHAR(64) NULL);

    INSERT INTO #Items (OfferId, Hash)
    SELECT OfferId, MAX(Hash)
    FROM OPENJSON(@Items) WITH (OfferId NVARCHAR(128) '$.OfferId', Hash NVARCHAR(64) '$.Hash')
    WHERE OfferId IS NOT NULL
    GROUP BY OfferId;

    DELETE h
    FROM dbo.AllegroOfferContentHashes h
    JOIN #Items i ON i.OfferId = h.OfferId
    WHERE i.Hash IS NULL;

    UPDATE h
    SET ContentHash = i.Hash, Account = @Account, SentAt = SYSUTCDATETIME()
    FROM dbo.AllegroOfferContentHashes h
    JOIN #Items i ON i.OfferId = h.OfferId
    WHERE i.Hash IS NOT NULL;

    INSERT INTO dbo.AllegroOfferContentHashes (OfferId, Account, ContentHash, SentAt)
    SELECT i.OfferId, @Account, i.Hash, SYSUTCDATETIME()
    FROM #Items i
    WHERE i.Hash IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.AllegroOfferContentHashes h WHERE h.OfferId = i.OfferId);

    DELETE h
    FROM dbo.AllegroOfferContentHashes h
    WHERE h.Account = @Account
      AND NOT EXISTS (SELECT 1 FROM dbo.AllegroOffers o WHERE o.Id = h.OfferId);
END
GO
