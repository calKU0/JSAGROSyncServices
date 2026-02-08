CREATE OR ALTER PROCEDURE dbo.AllegroImages_Add
    @ProductId INT,
    @Url NVARCHAR(2048)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.AllegroImages
    WHERE ProductId = @ProductId
      AND Url = @Url;

    INSERT INTO dbo.AllegroImages (ProductId, Url, Connected)
    VALUES (@ProductId, @Url, 0);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroImages_DeleteNotConnectedByProductId
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.AllegroImages
    WHERE ProductId = @ProductId
      AND Connected = 0;
END
GO

CREATE OR ALTER PROCEDURE dbo.AllegroImages_MarkConnectedByProductId
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AllegroImages
    SET Connected = 1
    WHERE ProductId = @ProductId;
END
GO
