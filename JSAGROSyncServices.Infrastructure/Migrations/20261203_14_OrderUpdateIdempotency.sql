-- Koniec wysylania w kolko tych samych statusow i numerow przesylek do Allegro.
--
-- Dotad serwis co cykl wysylal status i wszystkie numery przesylek kazdego zamowienia
-- z ostatnich 7 dni: ~120 PUT-ow statusu i ~230 POST-ow przesylek na cykl, 62 cykle dziennie.
-- Status wracal, bo RealizeStatus w bazie pokazywal stan sprzed naszej wysylki,
-- a numerow przesylek nie pamietalismy w ogole.
--
--   AllegroOrderShipments - numery przesylek, o ktorych wiemy, ze sa juz w Allegro,
--   ShipmentsCheckedAt    - kiedy porownalismy przesylki zamowienia z tym, co ma Allegro.

IF OBJECT_ID(N'dbo.AllegroOrderShipments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllegroOrderShipments
    (
        Id             INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_AllegroOrderShipments PRIMARY KEY,
        AllegroOrderId INT           NOT NULL CONSTRAINT FK_AllegroOrderShipments_Order REFERENCES dbo.AllegroOrders (Id) ON DELETE CASCADE,
        CarrierId      NVARCHAR(50)  NOT NULL,
        Waybill        NVARCHAR(100) NOT NULL,
        SentAt         DATETIME2     NOT NULL CONSTRAINT DF_AllegroOrderShipments_SentAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AllegroOrderShipments UNIQUE (AllegroOrderId, CarrierId, Waybill)
    );
END
GO

IF COL_LENGTH('dbo.AllegroOrders', 'ShipmentsCheckedAt') IS NULL
    ALTER TABLE dbo.AllegroOrders ADD ShipmentsCheckedAt DATETIME2 NULL;
GO

CREATE OR ALTER PROCEDURE dbo.AllegroOrderShipments_GetByOrder
    @AllegroOrderId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CarrierId, Waybill
    FROM dbo.AllegroOrderShipments
    WHERE AllegroOrderId = @AllegroOrderId;
END
GO

-- Idempotentne: ten sam numer mozna zglosic wielokrotnie (np. po odczycie z Allegro).
CREATE OR ALTER PROCEDURE dbo.AllegroOrderShipments_Add
    @AllegroOrderId INT,
    @CarrierId NVARCHAR(50),
    @Waybill NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    IF @Waybill IS NULL OR LTRIM(RTRIM(@Waybill)) = N''
        RETURN;

    INSERT INTO dbo.AllegroOrderShipments (AllegroOrderId, CarrierId, Waybill)
    SELECT @AllegroOrderId, ISNULL(@CarrierId, N''), @Waybill
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.AllegroOrderShipments s
        WHERE s.AllegroOrderId = @AllegroOrderId
          AND s.CarrierId = ISNULL(@CarrierId, N'')
          AND s.Waybill = @Waybill);
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroOrders_MarkShipmentsChecked
    @AllegroOrderId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AllegroOrders
    SET ShipmentsCheckedAt = SYSUTCDATETIME()
    WHERE Id = @AllegroOrderId;
END
GO

-- Po udanej zmianie statusu w Allegro zapisujemy go u siebie - inaczej kolejny cykl
-- porownuje nowy status ze starym i wysyla to samo jeszcze raz.
CREATE OR ALTER PROCEDURE dbo.AllegroOrders_UpdateRealizeStatus
    @AllegroOrderId INT,
    @RealizeStatus INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AllegroOrders
    SET RealizeStatus = @RealizeStatus
    WHERE Id = @AllegroOrderId;
END
GO

-- Oferty poza skonfigurowanymi kategoriami: zwracamy takze te juz zakonczone.
-- Bez tego wpadaly z powrotem do aktualizacji, a patch ustawia publikacje na ACTIVE,
-- wiec co drugi cykl ozywaly i byly konczone od nowa.
CREATE OR ALTER PROCEDURE dbo.AllegroOffers_GetOffersToEnd
    @IntegrationCompany INT,
    @Account INT,
    @DeliveryNames NVARCHAR(MAX),
    @Categories NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM OPENJSON(ISNULL(NULLIF(LTRIM(RTRIM(@Categories)), N''), N'[]')))
        RETURN;

    IF @DeliveryNames IS NULL OR LTRIM(RTRIM(@DeliveryNames)) = N''
        RETURN;

    CREATE TABLE #Allowed (ProductId INT NOT NULL PRIMARY KEY);

    EXEC dbo.ProductCategories_FillAllowed @IntegrationCompany, @Categories;

    SELECT
        ao.Id AS OfferId,
        ao.Status,
        ao.DeliveryName,
        p.Id AS ProductId,
        p.Code,
        p.Name
    FROM dbo.AllegroOffers ao
    JOIN dbo.RolmarProducts p
        ON p.Code = ao.ExternalId
       AND p.IntegrationCompany = @IntegrationCompany
    WHERE ao.Account = @Account
      AND ao.Status IN ('ACTIVE', 'INACTIVE', 'ENDED')
      AND ao.DeliveryName IN (SELECT TRIM(NCHAR(9) + NCHAR(10) + NCHAR(13) + NCHAR(160) + N' ' FROM [value]) FROM OPENJSON(@DeliveryNames))
      AND NOT EXISTS (SELECT 1 FROM #Allowed a WHERE a.ProductId = p.Id)
      -- Konczymy tylko przy pewnosci, ze produkt jest poza kategoriami. Produkt bez
      -- przypisanej kategorii zostawiamy - to brak danych, a nie decyzja o wycofaniu.
      AND EXISTS (SELECT 1 FROM dbo.ProductCategories pc WHERE pc.ProductId = p.Id);

    DROP TABLE #Allowed;
END
GO
