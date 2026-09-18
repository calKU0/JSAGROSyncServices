-- Allegro w bledzie PARAMETER_MISMATCH podaje nazwe parametru i wymagana wartosc,
-- ale nie podaje jego id. Do tej pory kod probowal wyciagnac id z komunikatu, nie znajdowal go
-- i nie stosowal poprawki - oferta wracala z tym samym bledem w kazdym cyklu.

CREATE OR ALTER PROCEDURE [dbo].[RolmarProductParameters_UpdateByName]
    @ProductId INT,
    @ParameterName NVARCHAR(255),
    @Value NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE pp
    SET pp.Value = @Value
    FROM dbo.RolmarProductParameters pp
    JOIN dbo.CategoryParameters cp ON cp.Id = pp.CategoryParameterId
    JOIN dbo.RolmarProducts p ON p.Id = pp.ProductId
    WHERE pp.ProductId = @ProductId
      AND cp.CategoryId = p.DefaultAllegroCategory
      AND cp.Name = @ParameterName
      AND pp.Value <> @Value;

    SELECT @@ROWCOUNT AS UpdatedRows;
END
GO
