IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'AllegroId' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD AllegroId NVARCHAR(100) NULL;
END