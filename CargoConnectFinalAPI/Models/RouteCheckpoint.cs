using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class RouteCheckpoint
    {
        public string name { get; set; }

        public double latitude { get; set; }
        public double longitude { get; set; }

        public int sequenceNo { get; set; }

        public DateTime? estimatedArrival { get; set; }
    }
}