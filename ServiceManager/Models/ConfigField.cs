using ServiceManager.Enums;

namespace ServiceManager.Models
{
    public class ConfigField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "Other";
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Czy pole pokazywać w konfiguratorze. <c>false</c> ukrywa je całkowicie -
        /// wartość zostaje w appsettings nietknięta, bo zapis nadpisuje tylko pola z ekranu.
        /// </summary>
        public bool IsVisable { get; set; } = true;
        public ConfigFieldType FieldType { get; set; } = ConfigFieldType.String;

        /// <summary>
        /// Dla list kategorii: dostawca (1 = Rolmar, 2 = Gąska), którego kategorie podpowiadać z bazy.
        /// <c>null</c> - zwykła lista wpisywana ręcznie.
        /// </summary>
        public int? CategoryCompany { get; set; }
    }
}