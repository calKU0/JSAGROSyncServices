using ServiceManager.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ServiceManager.Validation
{
    public static class DeliveryValidator
    {
        public static (List<Delivery> Deliveries, List<string> Errors) Validate(IEnumerable<(string Length, string Width, string Height, string Weight, string Name, bool IsSmart)> inputs)
        {
            var deliveries = new List<Delivery>();
            var errors = new List<string>();

            foreach (var input in inputs)
            {
                if (!TryParseDecimal(input.Length, out var length) ||
                    !TryParseDecimal(input.Width, out var width) ||
                    !TryParseDecimal(input.Height, out var height) ||
                    !TryParseDecimal(input.Weight, out var weight))
                {
                    errors.Add(ValidationMessages.DeliveryInvalidNumbers);
                    continue;
                }

                if (length <= 0 || width <= 0 || height <= 0 || weight <= 0)
                    errors.Add(ValidationMessages.DeliveryNonPositive);

                if (string.IsNullOrWhiteSpace(input.Name))
                    errors.Add(ValidationMessages.DeliveryMissingName);

                deliveries.Add(new Delivery
                {
                    Length = length,
                    Width = width,
                    Height = height,
                    Weight = weight,
                    DeliveryName = input.Name.Trim(),
                    IsSmart = input.IsSmart
                });
            }

            return (deliveries, errors.Distinct().ToList());
        }

        private static bool TryParseDecimal(string input, out decimal result)
        {
            var normalized = input.Replace(',', '.');
            return decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out result);
        }
    }
}
