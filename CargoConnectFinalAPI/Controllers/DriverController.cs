using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Routing;

namespace CargoConnectFinalAPI.Controllers
{

    public class DriverController : ApiController
    {

        private static readonly List<string> ColorPool = new List<string>
        {
            "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Cyan",
            "Magenta", "Lime", "Indigo", "Violet", "Teal", "Coral", "Salmon",
            "Crimson", "Scarlet", "Ruby", "Maroon", "Rose", "Fuchsia", "Lavender",
            "Periwinkle", "Cobalt", "Navy", "Sky Blue", "Turquoise", "Mint",
            "Emerald", "Olive", "Forest Green", "Sage", "Chartreuse", "Amber",
            "Gold", "Mustard", "Tangerine", "Peach", "Apricot", "Bronze", "Copper",
            "Chocolate", "Sienna", "Mahogany", "Burgundy", "Plum", "Eggplant",
            "Lilac", "Mauve", "Taupe"
        };          

        private static int _colorIndex = 0;
        private static readonly object _colorLock = new object();
        CargoConnectEntity db = new CargoConnectEntity();

        [HttpGet]
        [Route("api/drivers/{id}/requests/pending")]
        public IHttpActionResult GetDriverPendingRequests(int id)
        {
            return GetDriverRequestsByStatus(id, "pending");
        }

        [HttpGet]
        [Route("api/drivers/{id}/requests/accepted")]
        public IHttpActionResult GetDriverAcceptedRequests(int id)
        {
            return GetDriverRequestsByStatus(id, "accepted");
        }

        [HttpGet]
        [Route("api/drivers/{id}/requests/declined")]
        public IHttpActionResult GetDriverDeclinedRequests(int id)
        {
            return GetDriverRequestsByStatus(id, "declined");
        }

        private IHttpActionResult GetDriverRequestsByStatus(int id, string status)
        {
            var requests = db.Requests
                .Where(r => r.driver_id == id && r.status == status)
                .Select(r => new
                {
                    r.request_id,
                    r.shipment_id,
                    r.status,
                    r.route_id,
                    r.fare,
                })
                .ToList();

            var shipmentIds = requests.Select(r => r.shipment_id).ToList();

            var shipments = db.Shipments
                .Where(s => shipmentIds.Contains(s.shipment_id))
                .Select(s => new
                {
                    s.shipment_id,
                    s.sender_name,
                    s.sender_contact,
                    s.delivery_lat,
                    s.delivery_long,
                    s.delivery_address,
                    s.pickup_lat,
                    s.pickup_long,
                    s.pickup_address,
                    s.customer_id,
                    s.package_count,
                    s.total_weight,
                    packages = db.Packages
                        .Where(p => p.shipment_id == s.shipment_id)
                        .Select(p => new
                        {
                            p.shipment_id,
                            p.name,
                            p.weight,
                            p.length,
                            p.width,
                            p.height,
                            p.quantity,
                            p.color,
                            p.tagNo
                        }).ToList()
                })
                .ToList();

            return Ok(new
            {
                requestsData = requests,
                totalRequests = requests.Count,
                shipmentData = shipments
            });
        }
        [HttpPost]
        [Route("api/trucks/available")]
        public IHttpActionResult GetDriversByAvailability(int shipmentId)
        {
            try
            {
                var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == shipmentId);
                if (shipment == null)
                    return Content(System.Net.HttpStatusCode.BadRequest, "Shipment not found");

                if (shipment.pickup_lat == null || shipment.delivery_lat == null)
                    return Content(System.Net.HttpStatusCode.BadRequest, "Shipment location missing");

                var isStrict = shipment.strict ?? false;

                double radius = (shipment.shipment_radius ?? 10000) / 1000.0;

                double pickupLat = shipment.pickup_lat.Value;
                double pickupLong = shipment.pickup_long.Value;
                double destLat = shipment.delivery_lat.Value;
                double destLong = shipment.delivery_long.Value;
                DateTime requestedDate = shipment.pickup_date ?? DateTime.Now;
                double MaxDistanceKm = radius;

                var allSchedules = db.RouteSchedule.ToList();
                var allCheckpoints = db.Checkpoints.ToList();

                var allRoutes = db.Routes
                    .Where(r => r.is_active == true || r.is_next_route == true)
                    .ToList();

                var matchedRouteIds = new List<int>();

                foreach (var route in allRoutes)
                {
                    var checkpoints = allCheckpoints
                        .Where(c => c.route_id == route.route_id)
                        .OrderBy(c => c.sequence_no)
                        .ToList();

                    int pickupIndex = -1;
                    int dropIndex = -1;

                    for (int i = 0; i < checkpoints.Count; i++)
                    {
                        var cp = checkpoints[i];
                        if (cp.latitude == null || cp.longitude == null) continue;

                        if (pickupIndex == -1 &&
                            CalculateDistance(pickupLat, pickupLong, cp.latitude.Value, cp.longitude.Value) <= MaxDistanceKm)
                            pickupIndex = i;

                        if (dropIndex == -1 &&
                            CalculateDistance(destLat, destLong, cp.latitude.Value, cp.longitude.Value) <= MaxDistanceKm)
                            dropIndex = i;
                    }

                    if (pickupIndex == -1 || dropIndex == -1 || pickupIndex >= dropIndex)
                        continue;

                    var schedule = allSchedules.FirstOrDefault(s => s.route_id == route.route_id);
                    if (schedule == null) continue;

                    var pickupCheckpoint = checkpoints[pickupIndex];
                    DateTime estimatedPickupTime = pickupCheckpoint.estimated_arrival_datetime ?? DateTime.Now;

                    if (isStrict)
                    {
                        if (requestedDate.Date != estimatedPickupTime.Date)
                            continue;
                    }
                    else
                    {
                        if (estimatedPickupTime.Date < requestedDate.Date)
                            continue;
                    }

                    var preferences = db.RoutePreferences.FirstOrDefault(p => p.route_id == route.route_id);
                    if (preferences == null) continue;

                    string shipmentType = shipment.shipment_type ?? "Full";

                    if (preferences.shipment_type.ToLower() == "full")
                    {
                        if (shipmentType.ToLower() != "full") continue;

                        var hasExistingBookings = db.Bookings.Any(b =>
                            b.route_id == route.route_id &&
                            (b.status == "Assigned" || b.status == "In-Transit")
                        );
                        if (hasExistingBookings) continue;
                    }
                    else if (preferences.shipment_type.ToLower() == "shared")
                    {
                        if (shipmentType.ToLower() != "shared") continue;
                    }

                    var shipmentAttributeIds = db.PackageAttributeMapping
                        .Where(m => db.Packages
                            .Where(p => p.shipment_id == shipmentId)
                            .Select(p => p.package_id)
                            .Contains(m.package_id))
                        .Select(m => m.attribute_id)
                        .Distinct()
                        .ToList();

                    bool shipmentIsFragile = shipmentAttributeIds.Contains(1);
                    bool shipmentIsLiquid = shipmentAttributeIds.Contains(2);
                    bool shipmentIsFlammable = shipmentAttributeIds.Contains(3);
                    bool shipmentIsUpright = shipmentAttributeIds.Contains(4);

                    if (shipmentIsFragile && preferences.is_fragile != true) continue;

                    if (shipmentIsLiquid && preferences.is_liquid != true) continue;

                    if (shipmentIsFlammable && preferences.is_flammable != true) continue;

                    if (shipmentIsUpright && preferences.keep_upright != true) continue;

                    matchedRouteIds.Add(route.route_id);
                }

                var result = BuildAvailableTruckDtos(matchedRouteIds, shipmentId, pickupLat, pickupLong, destLat, destLong, requestedDate, isStrict, radius);
                return Ok(result.OrderByDescending(x => x.Rating).ToList());
            }
            catch (Exception ex)
            {
                return Content(System.Net.HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private List<AvailabilityDto> BuildAvailableTruckDtos(List<int> routeIds, int shipmentId, double pickupLat, double pickupLong, double destLat, double destLong, DateTime requestedDate, bool isStrict, double radius)
        {
            var result = new List<AvailabilityDto>();

            var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == shipmentId);
            if (shipment == null) return result;

            double shipmentWeight = shipment.total_weight ?? 0;
            double shipmentVolume = CalculateShipmentVolume(shipmentId);

            var routes = db.Routes.Where(r => routeIds.Contains(r.route_id)).ToList();
            var driverIds = routes.Select(r => r.driver_id).Distinct().ToList();
            var drivers = db.Driver.Where(d => driverIds.Contains(d.driver_id) && d.is_available == true).ToList();
            var vehicles = db.Vehicle.Where(v => driverIds.Contains(v.driver_id.Value)).ToList();

            double distance = CalculateDistance(pickupLat, pickupLong, destLat, destLong);

            foreach (var route in routes)
            {
                var driver = drivers.FirstOrDefault(d => d.driver_id == route.driver_id);
                var vehicle = vehicles.FirstOrDefault(v => v.driver_id == route.driver_id);
                if (driver == null || vehicle == null) continue;

                var checkpoints = db.Checkpoints
                    .Where(c => c.route_id == route.route_id)
                    .OrderBy(c => c.sequence_no)
                    .ToList();

                if (checkpoints.Count < 2) continue;

                var matchedPickup = checkpoints
                    .Select(c => new {
                        Cp = c,
                        Dist = (c.latitude.HasValue && c.longitude.HasValue)
                            ? CalculateDistance(pickupLat, pickupLong, (double)c.latitude, (double)c.longitude)
                            : double.MaxValue
                    })
                    .Where(x => x.Dist <= radius)
                    .OrderBy(x => x.Dist)
                    .FirstOrDefault()?.Cp;

                var matchedDrop = checkpoints
                    .Select(c => new {
                        Cp = c,
                        Dist = (c.latitude.HasValue && c.longitude.HasValue)
                            ? CalculateDistance(destLat, destLong, (double)c.latitude, (double)c.longitude)
                            : double.MaxValue
                    })
                    .Where(x => x.Dist <= radius)
                    .OrderBy(x => x.Dist)
                    .FirstOrDefault()?.Cp;

                if (matchedPickup == null || matchedDrop == null || matchedPickup.sequence_no >= matchedDrop.sequence_no)
                    continue;

                var preferences = db.RoutePreferences.FirstOrDefault(p => p.route_id == route.route_id);
                var ratingData = GetDriverRating(driver.driver_id);
                bool canAccommodate = CanDriverAccommodateShipment(driver.driver_id, shipmentId, requestedDate);

                double baseFare = (double)(route.base_fare ?? 0);
                double maxWeight = vehicle.weight_capacity ?? 0;
                double totalCapacity = (double)vehicle.weight_capacity;
                double price = 0;

                string prefType = preferences?.shipment_type?.ToLower() ?? "shared";
                if (prefType == "full")
                {
                    price = baseFare * Math.Max(maxWeight, totalCapacity);
                }
                else
                {
                    price = baseFare * Math.Max(shipmentWeight, shipmentVolume);
                }

                result.Add(new AvailabilityDto
                {
                    shipmentId = shipmentId,
                    pickupLat = pickupLat,
                    pickupLong = pickupLong,
                    destLat = destLat,
                    destLong = destLong,
                    requestedDate = requestedDate,
                    isStrict = isStrict,
                    DriverId = driver.driver_id,
                    DriverName = $"{driver.first_name} {driver.last_name}",
                    ContactNo = driver.contact_no,
                    TruckModel = vehicle.model ?? "Unknown",
                    LicenseNo = vehicle.vehicle_reg_no ?? "Unknown",
                    TotalCapacity = Math.Round(totalCapacity, 2),
                    PickupCity = matchedPickup.name ?? "Unknown",
                    DestinationCity = matchedDrop.name ?? "Unknown",
                    Price = Math.Round(price, 0),
                    IsFull = !canAccommodate,
                    RouteId = route.route_id,
                    Distance = Math.Round(distance, 2),
                    Rating = ratingData.rating,
                    TotalReviews = ratingData.totalReviews,
                    DepartureDate = matchedPickup.estimated_arrival_datetime ?? DateTime.Now,
                    ArrivalDate = matchedDrop.estimated_arrival_datetime ?? DateTime.Now,
                });
            }

            return result;
        }

        private bool CanDriverAccommodateShipment(int driverId, int newShipmentId, DateTime requestedDate)
        {
            var vehicle = db.Vehicle.FirstOrDefault(v => v.driver_id == driverId);
            if (vehicle == null) return false;

            double maxWeight = vehicle.weight_capacity ?? 0;
            double maxVolume = (vehicle.length ?? 0) * (vehicle.width ?? 0) * (vehicle.height ?? 0);

            var driverRoutes = db.Routes
                .Where(r => r.driver_id == driverId && (r.is_active == true || r.is_next_route == true))
                .Select(r => r.route_id)
                .ToList();

            if (!driverRoutes.Any()) return false;

            var activeBookings = db.Bookings
                .Where(b =>
                    driverRoutes.Contains(b.route_id) &&
                    b.status == "Confirmed" &&
                    b.pickup_date <= requestedDate &&
                    b.delivery_date >= requestedDate)
                .ToList();

            double usedWeight = 0;
            double usedVolume = 0;

            foreach (var booking in activeBookings)
            {
                var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == booking.shipment_id);
                if (shipment == null) continue;

                usedWeight += shipment.total_weight ?? 0;
                usedVolume += CalculateShipmentVolume(booking.shipment_id);
            }

            var newShipment = db.Shipments.FirstOrDefault(s => s.shipment_id == newShipmentId);
            if (newShipment == null) return false;

            double newWeight = newShipment.total_weight ?? 0;
            double newVolume = CalculateShipmentVolume(newShipmentId);

            bool weightOk = (usedWeight + newWeight) <= maxWeight;
            bool volumeOk = (usedVolume + newVolume) <= maxVolume;

            return weightOk && volumeOk;
        }
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371;

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }
        private double ToRadians(double angle)
        {
            return angle * Math.PI / 180;
        }
        private double CalculateShipmentVolume(int shipmentId)
        {
            var packages = db.Packages
                .Where(p => p.shipment_id == shipmentId)
                .ToList();

            double totalVolume = 0;

            foreach (var p in packages)
            {
                double volume = (p.length ?? 0) * (p.width ?? 0) * (p.height ?? 0);

                totalVolume += volume;
            }

            return totalVolume;
        }
        private (double rating, int totalReviews) GetDriverRating(int driverId)
        {
            var driver = db.Driver.FirstOrDefault(d => d.driver_id == driverId);
            if (driver == null) return (0, 0);

            int userId = driver.user_id;

            var reviews = db.Reviews
                .Where(r => r.target_user_id == userId)
                .ToList();

            if (reviews.Count == 0)
                return (0, 0);

            double avgRating = reviews.Average(r => (double)(r.rating ?? 0));
            int total = reviews.Count;

            return (Math.Round(avgRating, 1), total);
        }
        [HttpPost]
        [Route("api/request/send")]
        public IHttpActionResult SendRequest(int shipmentId, int driverId, int routeId, double fare)
        {
            var exists = db.Requests.FirstOrDefault(r =>
                r.shipment_id == shipmentId && 
                r.driver_id == driverId &&
                r.status == "Pending"
            );

            if (exists != null)
                return Ok("Already sent");

            var request = new Requests
            {
                shipment_id = shipmentId,
                driver_id = driverId,
                status = "Pending",
                route_id = routeId,
                fare = (decimal?)fare
            };
            var customer = db.Shipments
                .Where(s => s.shipment_id == shipmentId)
                .Select(s => s.customer_id)
                .FirstOrDefault();
            var customerName = db.Customer
                .Where(c => c.customer_id == customer)
                .Select(c => c.first_name + " " + c.last_name)
                .FirstOrDefault();

            var driver = db.Driver.FirstOrDefault(d => d.driver_id == driverId);
            if (driver != null)
                NotificationHelper.Send(db, driver.user_id, "You have received a request from "+customerName+".");

            db.Requests.Add(request);
            db.SaveChanges();

            return Ok("Request sent");
        }
        [HttpPost]
        [Route("api/requests/decline")]
        public IHttpActionResult DeclineRequest(int requestId)
        {
            var request = db.Requests.FirstOrDefault(r => r.request_id == requestId);

            if (request == null)
                return NotFound();

            request.status = "Declined";

            db.SaveChanges();

            return Ok(new { message = "Request declined" });
        }



        [HttpPost]
        [Route("api/drivers/accept-request")]
        public IHttpActionResult AcceptRequest(int requestId)
        {
            try
            {
                var request = db.Requests.FirstOrDefault(r => r.request_id == requestId);
                if (request == null)
                    return BadRequest("Request not found");

                if (request.status != "Pending")
                    return BadRequest("Request already processed");

                int shipmentId = request.shipment_id;
                int driverId = request.driver_id;

                var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == shipmentId);
                if (shipment == null)
                    return BadRequest("Shipment not found");

                var vehicleForCheck = db.Vehicle.FirstOrDefault(v => v.driver_id == driverId);
                if (vehicleForCheck == null)
                    return BadRequest("Driver vehicle not found");

                string shipmentType = shipment.shipment_type ?? "Full";

                if (shipmentType.ToLower() == "full")
                {
                    var hasExistingBookings = db.Bookings.Any(b =>
                        b.route_id == request.route_id &&
                        (b.status == "Assigned" || b.status == "In-Transit")
                    );
                    if (hasExistingBookings)
                        return BadRequest("Truck is no longer empty. Cannot accept a full load request.");
                }
                else if (shipmentType.ToLower() == "shared")
                {
                    double maxWeight = vehicleForCheck.weight_capacity ?? 0;
                    double maxVolume = (vehicleForCheck.length ?? 0) * (vehicleForCheck.width ?? 0) * (vehicleForCheck.height ?? 0);

                    var existingBookings = db.Bookings
                        .Where(b =>
                            b.route_id == request.route_id &&
                            (b.status == "Assigned" || b.status == "In-Transit"))
                        .ToList();

                    double usedWeight = 0;
                    double usedVolume = 0;

                    foreach (var b in existingBookings)
                    {
                        var s = db.Shipments.FirstOrDefault(x => x.shipment_id == b.shipment_id);
                        if (s == null) continue;
                        usedWeight += s.total_weight ?? 0;
                        usedVolume += CalculateShipmentVolume(b.shipment_id);
                    }

                    double newWeight = shipment.total_weight ?? 0;
                    double newVolume = CalculateShipmentVolume(shipmentId);

                    if ((usedWeight + newWeight) > maxWeight)
                        return BadRequest("Truck does not have enough weight capacity for this shipment.");

                    if ((usedVolume + newVolume) > maxVolume)
                        return BadRequest("Truck does not have enough space for this shipment.");
                }

                request.status = "Accepted";

                var otherRequests = db.Requests
                    .Where(r => r.shipment_id == shipmentId && r.request_id != requestId)
                    .ToList();
                foreach (var r in otherRequests)
                    r.status = "Declined";

                shipment.status = "Assigned";

                var vehicleRegNo = db.Vehicle
                    .Where(v => v.driver_id == driverId)
                    .Select(v => v.vehicle_reg_no)
                    .FirstOrDefault();
                if (vehicleRegNo == null)
                    return BadRequest("Driver vehicle not found");

                var routeId = db.Routes
                    .Where(rt => rt.driver_id == driverId)
                    .Select(rt => rt.route_id)
                    .FirstOrDefault();
                if (routeId == 0)
                    return BadRequest("Driver route not found");

                var existingTrip = db.Trips.FirstOrDefault(t =>
                    t.driver_id == driverId &&
                    t.route_id == routeId &&
                    t.status == "Scheduled"
                );

                Trips trip;
                if (existingTrip != null)
                {
                    // Reuse existing trip
                    trip = existingTrip;
                }
                else
                {
                    // Create new trip
                    trip = new Trips
                    {
                        driver_id = driverId,
                        vehicle_reg_no = vehicleRegNo,
                        route_id = routeId,
                        start_time = null,
                        end_time = null,
                        status = "Scheduled"
                    };
                    db.Trips.Add(trip);
                    db.SaveChanges();
                }

                var booking = new Bookings
                {
                    shipment_id = shipmentId,
                    customer_id = shipment.customer_id,
                    route_id = routeId,
                    trip_id = trip.trip_id,
                    status = "Assigned",
                    amount = request.fare ?? 0,
                    booking_type = shipment.strict == true ? "Private" : "Shared",
                    pickup_date = shipment.pickup_date,
                    created_at = DateTime.Now
                };

                db.Bookings.Add(booking);

                var packages = db.Packages
                    .Where(p => p.shipment_id == shipmentId)
                    .ToList();

                foreach (var pkg in packages)
                {
                    pkg.tagNo = GenerateTagNo();
                    pkg.color = GetNextColor();
                }

                db.SaveChanges();

                var driverName = db.Driver
                    .Where(d => d.driver_id == driverId)
                    .Select(d => d.first_name + " " + d.last_name)
                    .FirstOrDefault();

                var customer = db.Customer.FirstOrDefault(c => c.customer_id == booking.customer_id);
                if (customer != null)
                    NotificationHelper.Send(db, customer.user_id, "Your request has been accepted by " + driverName + ".");

                return Ok(new
                {
                    message = "Request accepted successfully",
                    tripId = trip.trip_id,
                    bookingId = booking.booking_id
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        private string GetNextColor()
        {
            lock (_colorLock)
            {
                var color = ColorPool[_colorIndex % ColorPool.Count];
                _colorIndex++;
                return color;
            }
        }

        private string GenerateTagNo()
        {
            var random = new Random();
            return "PKG-" + random.Next(100000, 999999).ToString();
        }
        [HttpGet]
        [Route("api/drivers/{driverId}/truck-stats")]
        public IHttpActionResult GetTruckStats(int driverId)
        {
            try
            {
                var vehicle = db.Vehicle.FirstOrDefault(v => v.driver_id == driverId);
                if (vehicle == null)
                    return BadRequest("Vehicle not found for this driver");

                // Get active route for this driver
                var driverRouteIds = db.Routes
                    .Where(r => r.driver_id == driverId &&
                     (r.is_active == true || r.is_next_route == true))
                    .Select(r => r.route_id)
                    .ToList();

                if (!driverRouteIds.Any())
                    return BadRequest("No active or next route found");

                // Get all active bookings across both routes
                var activeBookings = db.Bookings
                    .Where(b =>
                        driverRouteIds.Contains(b.route_id) &&
                        (b.status == "Assigned" || b.status == "In-Transit"))
                    .ToList();

                // Calculate used weight and volume from all active bookings
                double usedWeight = 0;
                double usedVolume = 0;

                foreach (var booking in activeBookings)
                {
                    var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == booking.shipment_id);
                    if (shipment == null) continue;
                    usedWeight += shipment.total_weight ?? 0;
                    usedVolume += CalculateShipmentVolume(booking.shipment_id);
                }

                // Truck total dimensions
                double totalLength = vehicle.length ?? 0;
                double totalWidth = vehicle.width ?? 0;
                double totalHeight = vehicle.height ?? 0;
                double totalVolume = totalLength * totalWidth * totalHeight;
                double maxWeight = vehicle.weight_capacity ?? 0;

                // Remaining
                double remainingWeight = Math.Max(0, maxWeight - usedWeight);
                double remainingVolume = Math.Max(0, totalVolume - usedVolume);

                // Back-calculate remaining dimensions assuming uniform fill
                // remaining as a ratio of total
                double fillRatio = totalVolume > 0 ? (remainingVolume / totalVolume) : 0;
                double remainingLength = Math.Round(totalLength * fillRatio, 2);
                double remainingWidth = Math.Round(totalWidth * fillRatio, 2);
                double remainingHeight = Math.Round(totalHeight * fillRatio, 2);

                return Ok(new
                {
                    // Weight
                    max_weight = Math.Round(maxWeight, 2),
                    used_weight = Math.Round(usedWeight, 2),
                    remaining_weight = Math.Round(remainingWeight, 2),

                    // Total dimensions
                    total_length = Math.Round(totalLength, 2),
                    total_width = Math.Round(totalWidth, 2),
                    total_height = Math.Round(totalHeight, 2),
                    total_volume = Math.Round(totalVolume, 2),

                    // Used
                    used_volume = Math.Round(usedVolume, 2),

                    // Remaining
                    remaining_volume = Math.Round(remainingVolume, 2),
                    remaining_length = remainingLength,
                    remaining_width = remainingWidth,
                    remaining_height = remainingHeight,

                    // Meta
                    active_bookings = activeBookings.Count
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("api/drivers/{driverId}/truck-info")]
        public IHttpActionResult GetTruckInfo(int driverId)
        {
            try
            {
                var vehicle = db.Vehicle.FirstOrDefault(v => v.driver_id == driverId);
                if (vehicle == null)
                    return BadRequest("No vehicle assigned to this driver");

                var activeShipments = (from b in db.Bookings
                                       join p in db.Packages on b.shipment_id equals p.shipment_id
                                       where b.status == "In-Transit"
                                       
                                       select p).ToList();

                double usedWeight = activeShipments.Sum(p => (double)(p.weight ?? 0) * (p.quantity ?? 1));
                double usedLength = activeShipments.Sum(p => (double)(p.length ?? 0) * (p.quantity ?? 1));
                double usedWidth = activeShipments.Sum(p => (double)(p.width ?? 0) * (p.quantity ?? 1));
                double usedHeight = activeShipments.Sum(p => (double)(p.height ?? 0) * (p.quantity ?? 1));

                double maxWeight = (double)(vehicle.weight_capacity ?? 0);
                double maxLength = (double)(vehicle.length ?? 0);
                double maxWidth = (double)(vehicle.width ?? 0);
                double maxHeight = (double)(vehicle.height ?? 0);

                return Ok(new
                {
                    vehicle_model = vehicle.model,
                    reg_no = vehicle.vehicle_reg_no,

                    weight = new
                    {
                        max_capacity = maxWeight,
                        used_capacity = usedWeight,
                        remaining_capacity = maxWeight - usedWeight
                    },

                    dimensions = new
                    {
                        max_dimensions = new { l = maxLength, w = maxWidth, h = maxHeight },
                        used_dimensions = new { l = usedLength, w = usedWidth, h = usedHeight },
                        remaining_dimensions = new
                        {
                            l = Math.Max(0, maxLength - usedLength),
                            w = Math.Max(0, maxWidth - usedWidth),
                            h = Math.Max(0, maxHeight - usedHeight)
                        }
                    },

                    active_package_count = activeShipments.Count
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }

}
