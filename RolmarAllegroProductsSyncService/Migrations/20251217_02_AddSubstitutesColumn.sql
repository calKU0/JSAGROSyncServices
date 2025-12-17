IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'Substitutes' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD Substitutes NVARCHAR(MAX) NULL;
END
