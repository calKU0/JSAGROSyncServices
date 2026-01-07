using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceManager.Models
{
    public class Delivery
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public string DeliveryName { get; set; }

        public override string ToString() => $"{Min}-{Max}: {DeliveryName}";

        public static Delivery Parse(string s)
        {
            var parts = s.Split(':');
            var range = parts[0].Split('-');
            return new Delivery
            {
                Min = decimal.Parse(range[0]),
                Max = decimal.Parse(range[1]),
                DeliveryName = parts[1]
            };
        }
    }
}
