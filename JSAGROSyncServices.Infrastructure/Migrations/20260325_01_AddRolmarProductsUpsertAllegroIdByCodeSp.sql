CREATE OR ALTER PROCEDURE dbo.AllegroOffers_UpsertAllegroId
    @OfferId NVARCHAR(255),
    @ProductId NVARCHAR(255),
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @AllegroId IS NULL OR LTRIM(RTRIM(@AllegroId)) = ''
        RETURN;

    UPDATE dbo.AllegroOffers
    SET ProductId = @AllegroId,
        UpdatedDate = SYSUTCDATETIME()
    WHERE Id = @OfferId
      AND Account = @Account
      AND ISNULL(ProductId, '') <> @ProductId;
END
GO
