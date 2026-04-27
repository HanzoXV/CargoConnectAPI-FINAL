using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class AvailabilityDto
    {
            public int shipmentId { get; set; }
            public double pickupLat { get; set; }
            public double pickupLong { get; set; }
            public double destLat { get; set; }
            public double destLong { get; set; }
            public DateTime requestedDate { get; set; }

            public bool isStrict { get; set; }
            public int DriverId { get; set; }

            public string DriverName { get; set; }

            public string ContactNo { get; set; }

            public string TruckModel { get; set; }
            
            public string LicenseNo { get; set; }
            public double TotalCapacity { get; set; }

            public string PickupCity { get; set; }

            public string DestinationCity { get; set; }

            public double Price { get; set; }

            public bool IsFull { get; set; }

            public int RouteId { get; set; }

            public double Distance { get; set; }
            public double Rating { get; set; }
            public int TotalReviews { get; set; }

            public DateTime DepartureDate { get; set; }
            public DateTime ArrivalDate { get; set; }
    }
}