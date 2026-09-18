using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Models;
using System.Globalization;
using System.Net;
using System.Text;

namespace JSAGROSyncServices.Orders.Helpers
{
    /// <summary>
    /// Treść maila o zamówieniu. Nazwy ofert i adresy pochodzą od klientów, więc wszystko,
    /// co trafia do HTML, jest escapowane; kwoty formatujemy po polsku niezależnie od kultury procesu.
    /// </summary>
    public static class OrderEmailBuilder
    {
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pl-PL");

        public static string Build(AllegroOrder order, AllegroAccount account, string? errorMessage = null)
        {
            var isError = !string.IsNullOrEmpty(errorMessage);
            var color = isError ? "#dc3545" : "#28a745";

            var header = isError
                ? $"Wystąpił błąd przy składaniu automatycznego zamówienia w Gąsce z konta {account}"
                : $"Złożono automatyczne zamówienie w Gąsce z konta {account}";

            var sb = new StringBuilder();

            sb.Append($@"
                <html>
                <body style='font-family:Arial,sans-serif; background-color:#f9f9f9; padding:20px;'>
                    <div style='max-width:900px; margin:0 auto; background-color:#ffffff; border-radius:10px; box-shadow:0 4px 10px rgba(0,0,0,0.1); padding:20px;'>
                        <h2 style='color:{color}; text-align:center;'>{E(header)}</h2>

                        <p><strong>ID zamówienia Allegro:</strong> {E(order.AllegroId)}</p>
                        <p><strong>Klient:</strong> {E(order.ClientNickname)}</p>
                        <p><strong>Imię i Nazwisko:</strong> {E(order.RecipientFirstName)} {E(order.RecipientLastName)}</p>
                        {Row("Firma", order.RecipientCompanyName)}
                        <p><strong>Email:</strong> {E(order.RecipientEmail)}</p>
                        <p><strong>Telefon:</strong> {E(order.RecipientPhoneNumber ?? "BRAK")}</p>
                        <p><strong>Adres:</strong> {E(order.RecipientStreet)}, {E(order.RecipientCity)}, {E(order.RecipientPostalCode)}, {E(order.RecipientCountry)}</p>
                        <p><strong>Metoda dostawy:</strong> {E(order.DeliveryMethodName)}</p>
                        {Row("Numer zamówienia Gąski", order.ExternalOrderNumber)}
                        {Row("Metoda dostawy Gąski", order.ExternalDeliveryName)}
                        {(isError ? $"<p style='color:#dc3545; font-weight:bold;'>Błąd: {E(errorMessage)}</p>" : string.Empty)}

                        <h3 style='border-bottom:2px solid #eee; padding-bottom:5px;'>Produkty:</h3>
                        <table style='width:100%; border-collapse:collapse; margin-top:10px;'>
                            <thead style='background-color:#f2f2f2;'>
                                <tr>
                                    <th style='padding:10px; text-align:left; border-bottom:1px solid #ddd;'>Kod</th>
                                    <th style='padding:10px; text-align:left; border-bottom:1px solid #ddd;'>Nazwa</th>
                                    <th style='padding:10px; text-align:center; border-bottom:1px solid #ddd;'>Ilość</th>
                                    <th style='padding:10px; text-align:right; border-bottom:1px solid #ddd;'>Cena jedn.</th>
                                    <th style='padding:10px; text-align:right; border-bottom:1px solid #ddd;'>Razem</th>
                                </tr>
                            </thead>
                            <tbody>");

            foreach (var item in order.Items ?? new List<AllegroOrderItem>())
            {
                var unitPrice = ParsePrice(item.PriceGross);

                sb.Append($@"
                                <tr>
                                    <td style='padding:10px; border-bottom:1px solid #eee;'>{E(item.ExternalId)}</td>
                                    <td style='padding:10px; border-bottom:1px solid #eee;'>{E(item.OfferName)}</td>
                                    <td style='padding:10px; text-align:center; border-bottom:1px solid #eee;'>{item.Quantity}</td>
                                    <td style='padding:10px; text-align:right; border-bottom:1px solid #eee;'>{Money(unitPrice)}</td>
                                    <td style='padding:10px; text-align:right; border-bottom:1px solid #eee;'>{Money(unitPrice * item.Quantity)}</td>
                                </tr>");
            }

            sb.Append($@"
                            </tbody>
                        </table>
                        <p style='text-align:right; font-weight:bold; margin-top:10px;'>Suma zamówienia: {Money(order.Amount)}</p>
                        <p style='text-align:center; color:#888; margin-top:20px; font-size:12px;'>Email wysłany automatycznie przez usługę synchronizacji zamówień</p>
                    </div>
                </body>
                </html>");

            return sb.ToString();
        }

        private static string Row(string label, string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : $"<p><strong>{E(label)}:</strong> {E(value)}</p>";

        private static decimal ParsePrice(string? value) =>
            decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m;

        private static string Money(decimal value) => E(value.ToString("C", Culture));

        private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
