IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'IntegrationId' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD IntegrationId INT NULL;
END


IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE Name = N'IntegrationCompany' 
      AND Object_ID = Object_ID(N'dbo.RolmarProducts')
)
BEGIN
    ALTER TABLE dbo.RolmarProducts
    ADD IntegrationCompany NVARCHAR(50) NULL;
END
