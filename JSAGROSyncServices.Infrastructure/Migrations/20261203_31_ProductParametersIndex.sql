-- Parametry produktów nie miały żadnego indeksu poza kluczem - każde "czy produkt ma już ten parametr"
-- skanowało całą tabelę (ok. 375 tys. wierszy). Przy przeliczaniu parametrów dla kategorii ofert
-- zapis wiersz po wierszu trwał kilkanaście minut. Indeks + zapis jednym poleceniem.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RolmarProductParameters_Product_CategoryParameter'
               AND object_id = OBJECT_ID('dbo.RolmarProductParameters'))
    CREATE INDEX IX_RolmarProductParameters_Product_CategoryParameter
        ON dbo.RolmarProductParameters (ProductId, CategoryParameterId)
        INCLUDE (IsForProduct);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CategoryParameters_Category'
               AND object_id = OBJECT_ID('dbo.CategoryParameters'))
    CREATE INDEX IX_CategoryParameters_Category
        ON dbo.CategoryParameters (CategoryId)
        INCLUDE (Required, RequiredForProduct, FetchedAt, Name);
GO

-- Wstawia brakujące parametry z listy JSON [{ProductId, CategoryParameterId, Value, IsForProduct}].
-- Istniejących wartości nie nadpisuje (mogły zostać poprawione według komunikatu Allegro).
CREATE OR ALTER PROCEDURE dbo.RolmarProductParameters_InsertMissing
    @ParametersJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Source AS
    (
        SELECT ProductId, CategoryParameterId, Value, IsForProduct,
               ROW_NUMBER() OVER (PARTITION BY ProductId, CategoryParameterId ORDER BY (SELECT 0)) AS rn
        FROM OPENJSON(@ParametersJson)
        WITH (
            ProductId INT '$.ProductId',
            CategoryParameterId INT '$.CategoryParameterId',
            Value NVARCHAR(MAX) '$.Value',
            IsForProduct BIT '$.IsForProduct'
        )
    )
    INSERT INTO dbo.RolmarProductParameters (ProductId, CategoryParameterId, Value, IsForProduct)
    SELECT s.ProductId, s.CategoryParameterId, s.Value, s.IsForProduct
    FROM Source s
    WHERE s.rn = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolmarProductParameters pp
          WHERE pp.ProductId = s.ProductId AND pp.CategoryParameterId = s.CategoryParameterId);

    SELECT @@ROWCOUNT;
END
GO
