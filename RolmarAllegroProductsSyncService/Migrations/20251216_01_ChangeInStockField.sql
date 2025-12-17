IF EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'InStock' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    -- Set existing NULLs to 0 to avoid constraint issues
    UPDATE dbo.RolmarProducts
    SET InStock = 0
    WHERE InStock IS NULL;

    -- Alter column to allow NULL and set default
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_InStock;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_InStock
        DEFAULT 0 FOR InStock;

    ALTER TABLE dbo.RolmarProducts
    ALTER COLUMN InStock FLOAT NULL;
END
GO