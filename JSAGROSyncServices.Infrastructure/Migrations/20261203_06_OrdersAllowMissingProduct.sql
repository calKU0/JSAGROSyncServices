-- Pozycja zamowienia moze dotyczyc produktu, ktorego nie ma jeszcze w naszej bazie.
-- Wczesniej zapis takiego zamowienia rzucal wyjatkiem i cale zamowienie nie bylo zapisywane,
-- wiec proba powtarzala sie w kazdym cyklu. Teraz zamowienie zapisuje sie w calosci,
-- a serwis po prostu nie sklada go u dostawcy i zglasza brakujace kody.

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AllegroOrderItems')
      AND name = 'ProductId'
      AND is_nullable = 0
)
BEGIN
    DECLARE @fk NVARCHAR(200);

    SELECT @fk = fk.name
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
    WHERE fk.parent_object_id = OBJECT_ID('dbo.AllegroOrderItems')
      AND c.name = 'ProductId';

    IF @fk IS NOT NULL
        EXEC('ALTER TABLE dbo.AllegroOrderItems DROP CONSTRAINT [' + @fk + ']');

    ALTER TABLE dbo.AllegroOrderItems ALTER COLUMN ProductId INT NULL;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroOrders_MarkAsOrderedInExternalCompany
    @OrderId INT,
    @ExternalOrderId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AllegroOrders
    SET
        SentToExternalCompany = 1,
        -- Przy timeoucie nie znamy numeru zamowienia dostawcy - nie nadpisujemy go zerem.
        ExternalOrderId = COALESCE(NULLIF(@ExternalOrderId, 0), ExternalOrderId)
    WHERE Id = @OrderId;
END
GO
