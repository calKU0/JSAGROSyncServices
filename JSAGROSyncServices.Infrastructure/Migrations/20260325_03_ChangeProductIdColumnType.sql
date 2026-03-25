DROP INDEX [IX_ProductId] ON [dbo].[AllegroOffers]
GO

ALTER TABLE dbo.AllegroOffers
ALTER COLUMN ProductId NVARCHAR(500) NULL;
GO

ALTER   PROCEDURE [dbo].[AllegroOffers_GetWithoutDetails]
@Account INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT * FROM AllegroOffers o
    WHERE NOT EXISTS (SELECT 1 FROM AllegroOfferDescriptions d WHERE d.OfferId = o.Id)
    AND Status='ACTIVE' AND Account = @Account
    ORDER BY StartingAt DESC;
END