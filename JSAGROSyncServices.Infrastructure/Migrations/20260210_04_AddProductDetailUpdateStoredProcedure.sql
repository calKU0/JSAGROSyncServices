CREATE OR ALTER PROCEDURE dbo.RolmarProducts_GetForDetailUpdate
    @Limit INT,
    @IntegrationCompany INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM RolmarProducts p
    WHERE NOT EXISTS (SELECT 1 FROM RolmarCategory pc WHERE pc.ProductId = p.Id)
      AND IntegrationCompany = @IntegrationCompany
    ORDER BY p.Id
    OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
