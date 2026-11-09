namespace ServiceManager.Validation
{
    public static class ValidationMessages
    {
        public const string DeliveryNonPositive = "Wymiary i waga muszą być większe od zera.";
        public const string MarginNegative = "Zakresy marży nie mogą mieć wartości ujemnych.";
        public const string MarginMinGreaterThanMax = "Wartość „Od” nie może być większa niż „Do” w zakresie marży.";
        public const string MarginMissingRange = "Zdefiniuj przynajmniej jeden zakres marży.";
        public const string MarginMustStartAtZero = "Pierwszy zakres marży musi zaczynać się od 0 PLN.";
        public const string MarginOverlap = "Zakresy marży nie mogą się nakładać.";
    }
}
