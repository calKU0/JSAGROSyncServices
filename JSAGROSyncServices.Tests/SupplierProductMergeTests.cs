using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Lista produktów Gąski nie niesie zamienników, wielokrotności opakowania ani zastosowań -
    /// te dane przychodzą dopiero w szczegółach. Zapis produktu nadpisuje kolumny, więc bez
    /// przepisania zapisanych wartości lista czyściłaby je w każdym cyklu.
    /// </summary>
    public class SupplierProductMergeTests
    {
        [Fact]
        public void UnknownFieldsAreTakenFromTheSavedProduct()
        {
            var product = new RolmarProduct { Code = "TEST" };

            SupplierProductMerge.CarryOverDetailOnlyFields(product, "279-V,480", 6m, "C330");

            Assert.Equal("279-V,480", product.Substitutes);
            Assert.Equal(6m, product.Package);
            Assert.Equal("C330", product.Fits);
        }

        [Fact]
        public void FieldsFromTheCurrentResponseWin()
        {
            var product = new RolmarProduct { Code = "TEST", Substitutes = "111", Package = 2m, Fits = "C360" };

            SupplierProductMerge.CarryOverDetailOnlyFields(product, "279-V", 6m, "C330");

            Assert.Equal("111", product.Substitutes);
            Assert.Equal(2m, product.Package);
            Assert.Equal("C360", product.Fits);
        }

        /// <summary>Pusty łańcuch to jedyny sposób, w jaki szczegóły mogą wyczyścić zamienniki.</summary>
        [Fact]
        public void ExplicitlyEmptySubstitutesStayEmpty()
        {
            var product = new RolmarProduct { Code = "TEST", Substitutes = string.Empty };

            SupplierProductMerge.CarryOverDetailOnlyFields(product, "279-V", 0m, null);

            Assert.Equal(string.Empty, product.Substitutes);
        }
    }
}
