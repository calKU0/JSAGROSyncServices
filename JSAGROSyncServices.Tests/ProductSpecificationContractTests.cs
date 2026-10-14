using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.DTOs.Gaska;
using JSAGROSyncServices.Products.Services.Suppliers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Zapis specyfikacji jest podmianą: pusta lista czyści specyfikacje produktu w bazie.
    /// Dlatego "nie znamy specyfikacji w tej operacji" musi być reprezentowane przez <c>null</c>.
    /// Pusta lista domyślnie sprawiała, że lista produktów Gąski - która parametrów nie niesie -
    /// czyściła specyfikacje pobrane wcześniej ze szczegółów.
    /// </summary>
    public class ProductSpecificationContractTests
    {
        [Fact]
        public void NewProductDoesNotClaimToKnowItsSpecifications()
        {
            Assert.Null(new RolmarProduct().Specifications);
        }

        [Fact]
        public void GaskaProductListLeavesSpecificationsUnknown()
        {
            var mapped = GaskaApiService.MapToRolmarProduct(new ApiProducts { CodeGaska = "1.1114.300.520.01.02" });

            Assert.Null(mapped.Specifications);
            Assert.Null(mapped.CategoryKeys);
        }
    }
}
