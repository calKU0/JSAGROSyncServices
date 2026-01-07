IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE t.name = 'RolmarCategory'
      AND s.name = 'dbo'
)
BEGIN
    CREATE TABLE dbo.RolmarCategory
    (
        Id INT IDENTITY(1,1) NOT NULL,
        ProductId INT NOT NULL,
        Name NVARCHAR(255) NOT NULL,

        CONSTRAINT PK_RolmarCategory PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RolmarCategory_RolmarProduct
            FOREIGN KEY (ProductId)
            REFERENCES dbo.RolmarProducts (Id)
            ON DELETE CASCADE
    );
END