namespace JSAGROSyncServices.Orders.Settings
{
    /// <summary>Godziny, po których dany kurier nie odbiera już przesyłek danego dnia.</summary>
    public class CourierSettings
    {
        public int DpdFinalOrderHour { get; set; }

        public int FedexFinalOrderHour { get; set; }

        public int GlsFinalOrderHour { get; set; }
    }
}
