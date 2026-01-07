IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Weight' AND Object_ID = Object_ID(N'dbo.RolmarProducts'))
BEGIN
    -- Weight
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_Weight;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_Weight
        DEFAULT 0 FOR Weight;
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'PriceNet' AND Object_ID = Object_ID(N'dbo.RolmarProducts'))
BEGIN
    -- PriceNet
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_PriceNet;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_PriceNet
        DEFAULT 0 FOR PriceNet;
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'PriceGross' AND Object_ID = Object_ID(N'dbo.RolmarProducts'))
BEGIN
    -- PriceGross
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_PriceGross;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_PriceGross
        DEFAULT 0 FOR PriceGross;
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Package' AND Object_ID = Object_ID(N'dbo.RolmarProducts'))
BEGIN
    -- Package
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_Package;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_Package
        DEFAULT 1 FOR Package;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'DefaultAllegroCategory' AND Object_ID = Object_ID(N'dbo.RolmarProducts'))
BEGIN
    -- Package
    ALTER TABLE dbo.RolmarProducts
    DROP CONSTRAINT IF EXISTS DF_RolmarProducts_DefaultAllegroCategory;

    ALTER TABLE dbo.RolmarProducts
    ADD CONSTRAINT DF_RolmarProducts_DefaultAllegroCategory
        DEFAULT 0 FOR DefaultAllegroCategory;
END
GO