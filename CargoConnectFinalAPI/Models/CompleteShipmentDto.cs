using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class CompleteShipmentDto
    {
        public int shipment_id { get; set; }

        public double pickup_lat { get; set; }
        public double pickup_long { get; set; }
        public string pickup_address { get; set; }

        public double delivery_lat { get; set; }
        public double delivery_long { get; set; }
        public string delivery_address { get; set; }

        public bool strict { get; set; }

        public string recipient_fname { get; set; }
        public string recipient_lname { get; set; }
        public string recipient_contact { get; set; }
        public string instructionsMessage { get; set; }
        public DateTime? booking_date { get; set; }

        public double shipment_radius { get; set; }
        public String shipment_type { get; set; }
    }
}