namespace ServiceManager.Enums
{
    public enum ConfigFieldType
    {
        String,
        Int,
        Decimal,
        StringList,
        IntList,

        /// <summary>Tak / nie - pole wyboru.</summary>
        Bool,

        /// <summary>Tabela: lista obiektów albo słownik, kolumny w <c>ConfigField.Columns</c>.</summary>
        Table
    }
}
