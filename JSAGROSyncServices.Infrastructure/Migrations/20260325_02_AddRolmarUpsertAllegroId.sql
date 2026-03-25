CREATE OR ALTER PROCEDURE dbo.AllegroOffers_UpsertAllegroId
    @OfferId NVARCHAR(255),
    @ProductId NVARCHAR(255),
    @Account INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @OfferId IS NULL OR LTRIM(RTRIM(@OfferId)) = ''
        RETURN;

    UPDATE dbo.AllegroOffers
    SET ProductId = @ProductId
    WHERE Id = @OfferId
      AND Account = @Account
      AND ISNULL(ProductId, '') <> @ProductId;
END
GO
