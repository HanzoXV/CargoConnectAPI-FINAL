using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class CreateRouteRequestDTO
    {
        public int DriverId { get; set; }

        public DateTime DepartureDate { get; set; }
        public DateTime ArrivalDate { get; set; }

        public bool ActivateNow { get; set; }
        public decimal BaseFare { get; set; }

        public bool IsFragile { get; set; }
        public bool IsLiquid { get; set; }
        public bool IsFlammable { get; set; }
        public bool KeepUpright { get; set; }
        public string ShipmentType { get; set; }
        public List<RouteCheckpoint> Points { get; set; }
    }
}