IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.name = N'IntegrationCompany'
      AND c.object_id = OBJECT_ID(N'dbo.RolmarProducts')
      AND t.name IN (N'nvarchar', N'varchar')
)
BEGIN
    EXEC('ALTER TABLE dbo.RolmarProducts ADD IntegrationCompanyTemp INT NULL');

    EXEC('
        UPDATE dbo.RolmarProducts
        SET IntegrationCompanyTemp = CASE IntegrationCompany
            WHEN N''Rolmar'' THEN 1
            WHEN N''Gaska'' THEN 2
            ELSE TRY_CONVERT(INT, IntegrationCompany)
        END
    ');

    EXEC('ALTER TABLE dbo.RolmarProducts DROP COLUMN IntegrationCompany');

    EXEC('EXEC sp_rename
        ''dbo.RolmarProducts.IntegrationCompanyTemp'',
        ''IntegrationCompany'',
        ''COLUMN''
    ');
END

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.name = N'Account'
      AND c.object_id = OBJECT_ID(N'dbo.AllegroOffers')
      AND t.name IN (N'nvarchar', N'varchar')
)
BEGIN
    EXEC('ALTER TABLE dbo.AllegroOffers ADD AccountTemp INT NULL');

    EXEC('
        UPDATE dbo.AllegroOffers
        SET AccountTemp = CASE Account
            WHEN N''JSAGRO'' THEN 1
            WHEN N''JSAGRO2'' THEN 2
            ELSE TRY_CONVERT(INT, Account)
        END
    ');

    EXEC('ALTER TABLE dbo.AllegroOffers DROP COLUMN Account');

    EXEC('EXEC sp_rename
        ''dbo.AllegroOffers.AccountTemp'',
        ''Account'',
        ''COLUMN''
    ');
END

