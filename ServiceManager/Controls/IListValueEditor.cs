namespace ServiceManager.Controls
{
    /// <summary>Edytor pola konfiguracji przechowującego listę wartości.</summary>
    public interface IListValueEditor
    {
        IReadOnlyList<string> GetItems();
    }
}
