using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Scalanie produktu z danymi już zapisanymi w bazie.
    ///
    /// Zapis produktu nadpisuje kolumny wartościami z bieżącej odpowiedzi dostawcy, a lista
    /// produktów nie niesie wszystkiego, co niosą szczegóły. Pola, których bieżąca odpowiedź
    /// nie zna, muszą więc zostać przepisane ze stanu w bazie - inaczej lista co cykl
    /// czyściłaby dane pobrane wcześniej ze szczegółów.
    /// </summary>
    internal static class SupplierProductMerge
    {
        /// <summary>
        /// Przepisuje pola, które u Gąski przychodzą wyłącznie w <c>/product</c>. Celowo nie dotyka
        /// jednostki: tę podaje też lista produktów, więc jej wartość nie ginie.
        /// </summary>
        /// <param name="product">Produkt zmapowany z bieżącej odpowiedzi dostawcy.</param>
        /// <param name="substitutes">Numery zamienne zapisane w bazie.</param>
        /// <param name="package">Wielokrotność opakowania zapisana w bazie.</param>
        /// <param name="fits">Zastosowania zapisane w bazie.</param>
        internal static void CarryOverDetailOnlyFields(RolmarProduct product, string? substitutes, decimal package, string? fits)
        {
            // Pusty łańcuch to jawne "dostawca już nie podaje zamienników" - taką wartość zostawiamy,
            // bo tylko ona pozwala wyczyścić kolumnę. Dopisujemy wyłącznie to, czego nie znamy.
            product.Substitutes ??= substitutes;
            product.Fits ??= fits;

            if (product.Package <= 0)
                product.Package = package;
        }
    }
}
