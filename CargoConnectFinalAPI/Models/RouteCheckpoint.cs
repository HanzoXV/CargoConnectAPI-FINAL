using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class RouteCheckpoint
    {
        public string Name { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public int SequenceNo { get; set; }
    }
}