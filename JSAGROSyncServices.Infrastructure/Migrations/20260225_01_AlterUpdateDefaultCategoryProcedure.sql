CREATE OR ALTER PROCEDURE [dbo].[Products_UpdateDefaultCategoryByCode]
    @ProductCode NVARCHAR(255),
    @CategoryId INT
AS
BEGIN
    SET NOCOUNT ON;

    declare @ProductId int = (Select Id from dbo.RolmarProducts where code = @ProductCode)

    UPDATE RolmarProducts
    SET DefaultAllegroCategory = @CategoryId,
        UpdatedDate = SYSUTCDATETIME()
    WHERE Code = @ProductCode;

    DELETE FROM dbo.ProductParameters where ProductId = @ProductId
END
GO

CREATE OR ALTER PROCEDURE [dbo].[Products_UpdateDefaultCategoryById]
    @ProductId INT,
    @CategoryId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE RolmarProducts
    SET DefaultAllegroCategory = @CategoryId,
        UpdatedDate = SYSUTCDATETIME()
    WHERE Id = @ProductId;

    DELETE FROM dbo.ProductParameters where ProductId = @ProductId
END
GO