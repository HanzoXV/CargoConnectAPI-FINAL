using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class ShipmentDto
    {
        public int shipment_id { get; set; }
        public string pickup_address { get; set; }
        public string delivery_address { get; set; }
        public string status { get; set; }
        public string sender_name { get; set; }
        public string sender_contact { get; set; }
        public double? total_weight { get; set; }
        public bool? strict { get; set; }
        public string recipient_fname { get; set; }
        public string recipient_lname { get; set; }
        public string recipient_contact { get; set; }
        public string booking_date { get; set; }
        public double? shipment_radius { get; set; }
        public string shipment_type { get; set; }
        public List<PackageDto> packages { get; set; }
    }

    public class PackageDto
    {
        public int shipment_id { get; set; }
        public string name { get; set; }
        public double? weight { get; set; }
        public double? length { get; set; }
        public double? width { get; set; }
        public double? height { get; set; }
        public int? quantity { get; set; }
        public string color { get; set; }
        public string tagNo { get; set; }
    }
}