namespace Allegro.JSAGRO.Erli.ProductsService.Settings
{
    public class AppSettings
    {
        public int LogsExpirationDays { get; set; } = 14;

        public int FetchIntervalMinutes { get; set; } = 120;

        /// <summary>Ile produktów wysyłać do Erli równolegle.</summary>
        public int UploadParallelism { get; set; } = 8;

        /// <summary>
        /// Dopłata doliczana do ceny oferty wysyłanej kurierem (w PLN brutto).
        /// Wcześniej zaszyta w kodzie jako 3 zł.
        /// </summary>
        public decimal CourierPriceSurcharge { get; set; } = 3m;
    }
}
