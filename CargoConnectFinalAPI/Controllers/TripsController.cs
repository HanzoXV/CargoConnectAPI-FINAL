using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.Remoting.Messaging;
using System.Web.Http;

namespace CargoConnectFinalAPI.Controllers
{
    public class TripsController : ApiController
    {
        CargoConnectEntity db = new CargoConnectEntity();

        [HttpGet]
        [Route("api/drivers/{driverId}/bookings/assigned")]
        public IHttpActionResult GetConfirmedBookings(int driverId) => GetBookingsByStatus(driverId, "Assigned");

        [HttpGet]
        [Route("api/drivers/{driverId}/bookings/in-transit")]
        public IHttpActionResult GetInTransitBookings(int driverId) => GetBookingsByStatus(driverId, "In-Transit");

        [HttpGet]
        [Route("api/drivers/{driverId}/bookings/completed")]
        public IHttpActionResult GetCompletedBookings(int driverId) => GetBookingsByStatus(driverId, "Completed");

        [HttpGet]
        [Route("api/drivers/{driverId}/bookings/canceled")]
        public IHttpActionResult GetCanceledBookings(int driverId) => GetBookingsByStatus(driverId, "Canceled");

        private IHttpActionResult GetBookingsByStatus(int driverId, string status)
        {
            var bookings = (
                from b in db.Bookings
                join t in db.Trips on b.trip_id equals t.trip_id
                join s in db.Shipments on b.shipment_id equals s.shipment_id
                join r in db.RecipientDetails                                    // left join
                    on b.shipment_id equals r.shipment_id into recipientGroup
                from r in recipientGroup.DefaultIfEmpty()
                where t.driver_id == driverId && b.status == status
                select new
                {
                    booking_id = b.booking_id,
                    shipment_id = b.shipment_id,
                    route_id = b.route_id,
                    trip_id = b.trip_id,
                    status = b.status,
                    amount = b.amount,
                    pickup_date = b.pickup_date,

                    // Shipment details
                    pickup_address = s.pickup_address,
                    pickup_lat = s.pickup_lat,
                    pickup_long = s.pickup_long,
                    delivery_address = s.delivery_address,
                    delivery_lat = s.delivery_lat,
                    delivery_long = s.delivery_long,
                    sender_name = s.sender_name,
                    sender_contact = s.sender_contact,
                    total_weight = s.total_weight,
                    package_count = s.package_count,
                    customer_user_id = db.Customer                          // ← add this
                        .Where(c => c.customer_id == b.customer_id)
                        .Select(c => (int?)c.user_id)
                        .FirstOrDefault(),

                    // Recipient details (null safe)
                    recipient_name = r != null ? r.recipient_fname + " " + r.recipient_lname : null,
                    recipient_contact = r != null ? r.recipient_contact : null,

                    // Package details
                    packages = db.Packages
                        .Where(p => p.shipment_id == b.shipment_id)
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
                }).ToList();

            return Ok(bookings);
        }
        [Route("api/checkpoints/{checkpointId}/shipments")]
        public IHttpActionResult GetShipmentsAtCheckpoint(int checkpointId, int driverId)
        {

            var checkpoint = db.Checkpoints.FirstOrDefault(c => c.checkpoint_id == checkpointId);
            if (checkpoint == null)
                return BadRequest("Checkpoint not found");

            int routeId = checkpoint.route_id;


            var route = db.Routes.FirstOrDefault(r => r.route_id == routeId && r.driver_id == driverId);
            if (route == null)
                return BadRequest("This checkpoint does not belong to the driver's route");


            var bookingsData = (
                from b in db.Bookings
                join s in db.Shipments on b.shipment_id equals s.shipment_id
                join rd in db.RecipientDetails
                on b.shipment_id equals rd.shipment_id into recipientGroup
                from r in recipientGroup.DefaultIfEmpty()
                where b.route_id == routeId
                && (b.status == "Assigned" || b.status == "In-Transit")
                && s.delivery_lat != null
                && s.delivery_long != null
                && s.pickup_lat != null
                && s.pickup_long != null
                select new
                {
                    b.booking_id,
                    b.shipment_id,
                    b.status,
                    b.amount,
                    b.pickup_date,

                    // Shipment
                    s.pickup_address,
                    s.pickup_lat,
                    s.pickup_long,
                    s.delivery_address,
                    s.delivery_lat,
                    s.delivery_long,
                    s.sender_name,
                    s.sender_contact,
                    s.total_weight,
                    s.package_count,
                    s.shipment_radius,

                    // Recipient
                    recipient_name = r != null ? r.recipient_fname + " " + r.recipient_lname : null,
                    recipient_contact = r != null ? r.recipient_contact : null
                }
            ).ToList(); // ✅ only here

            // 4. Fetch all packages in ONE query (no N+1 problem)
            var shipmentIds = bookingsData.Select(x => x.shipment_id).Distinct().ToList();

            var allPackages = db.Packages
                .Where(p => shipmentIds.Contains(p.shipment_id))
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
                })
                .ToList();

            // 5. Merge packages into bookings
            var allBookings = bookingsData.Select(b => new
            {
                b.booking_id,
                b.shipment_id,
                b.status,
                b.amount,
                b.pickup_date,

                b.pickup_address,
                b.pickup_lat,
                b.pickup_long,
                b.delivery_address,
                b.delivery_lat,
                b.delivery_long,
                b.sender_name,
                b.sender_contact,
                b.total_weight,
                b.package_count,
                b.shipment_radius,

                b.recipient_name,
                b.recipient_contact,

                packages = allPackages
                    .Where(p => p.shipment_id == b.shipment_id)
                    .ToList()
            }).ToList();

            double checkpointLat = checkpoint.latitude ?? 0;
            double checkpointLng = checkpoint.longitude ?? 0;

            var toLoad = allBookings
    .Where(b =>
        b.status == "Assigned" &&
        b.pickup_lat.HasValue && b.pickup_long.HasValue &&
        CalculateDistance(
            b.pickup_lat.Value, b.pickup_long.Value,
            checkpointLat, checkpointLng
        ) <= (b.shipment_radius ?? 10)
    ).ToList();

            var toDrop = allBookings
                .Where(b =>
                    b.status == "In-Transit" &&
                    b.delivery_lat.HasValue && b.delivery_long.HasValue &&
                    CalculateDistance(
                        b.delivery_lat.Value, b.delivery_long.Value,
                        checkpointLat, checkpointLng
                    ) <= (b.shipment_radius ?? 10)
                ).ToList();

            // 9. Final response
            return Ok(new
            {
                checkpoint_id = checkpointId,
                checkpoint_name = checkpoint.name,

                to_load = new
                {
                    total = toLoad.Count,
                    shipments = toLoad
                },

                to_drop = new
                {
                    total = toDrop.Count,
                    shipments = toDrop
                }
            });
        }

        [HttpPost]
        [Route("api/checkpoints/{checkpointId}/confirm")]
        public IHttpActionResult ConfirmCheckpointReached(int checkpointId, int driverId)
        {

            var checkpoint = db.Checkpoints.FirstOrDefault(c => c.checkpoint_id == checkpointId);
            if (checkpoint == null)
                return BadRequest("Checkpoint not found");

            int routeId = checkpoint.route_id;

            var route = db.Routes.FirstOrDefault(r => r.route_id == routeId && r.driver_id == driverId);
            if (route == null)
                return BadRequest("This checkpoint does not belong to the driver's route");

            var pendingPickups = (
    from b in db.Bookings
    join s in db.Shipments on b.shipment_id equals s.shipment_id
    where b.route_id == routeId
       && b.status == "Assigned"
       && s.pickup_lat != null && s.pickup_long != null
    select new { b, s.pickup_lat, s.pickup_long, s.shipment_radius }
            ).ToList()
            .Where(x => CalculateDistance(
                x.pickup_lat.Value, x.pickup_long.Value,
                checkpoint.latitude.Value, checkpoint.longitude.Value
                ) <= (x.shipment_radius ?? 10))
                .Select(x => x.b)
                .ToList();

            if (pendingPickups.Any())
                return BadRequest($"Please pickup all {pendingPickups.Count} remaining shipment(s) before confirming.");

            var pendingDropoffs = (
                from b in db.Bookings
                join s in db.Shipments on b.shipment_id equals s.shipment_id
                where b.route_id == routeId
                   && b.status == "In-Transit"
                   && s.delivery_lat != null && s.delivery_long != null
                select new { b, s.delivery_lat, s.delivery_long, s.shipment_radius }
            ).ToList()
            .Where(x => CalculateDistance(
                x.delivery_lat.Value, x.delivery_long.Value,
                checkpoint.latitude.Value, checkpoint.longitude.Value
            ) <= (x.shipment_radius ?? 10))
            .Select(x => x.b)
            .ToList();

            if (pendingDropoffs.Any())
                return BadRequest($"Please deliver all {pendingDropoffs.Count} remaining shipment(s) before confirming.");

            checkpoint.reached = true;

            var lastCheckpoint = db.Checkpoints
                .Where(c => c.route_id == routeId)
                .OrderByDescending(c => c.sequence_no)
                .FirstOrDefault();

            if (lastCheckpoint != null && lastCheckpoint.checkpoint_id == checkpointId)
            {
                var trip = db.Trips.FirstOrDefault(t =>
                    t.route_id == routeId && t.status == "In-Transit");

                if (trip != null)
                {
                    trip.end_time = DateTime.Now;
                    trip.status = "Completed";
                }

                var activeRoute = db.Routes.FirstOrDefault(r =>
                    r.route_id == routeId && r.is_active == true);

                if (activeRoute != null)
                {
                    var nextRoute = db.Routes.FirstOrDefault(r =>
                        r.driver_id == driverId &&
                        r.is_next_route == true &&
                        r.route_id != routeId);

                    activeRoute.is_active = false;
                    activeRoute.is_next_route = false;

                    if (nextRoute != null)
                    {
                        nextRoute.is_active = true;
                        nextRoute.is_next_route = false;
                    }
                }
            }

            db.SaveChanges();

            return Ok(new
            {
                message = "Checkpoint confirmed",
                checkpoint_id = checkpointId,
                checkpoint_name = checkpoint.name
            });


        }
        [HttpPost]
        [Route("api/bookings/{bookingId}/pickup")]
        public IHttpActionResult PickupBooking(int bookingId)
        {
            try
            {
                var booking = db.Bookings.FirstOrDefault(b => b.booking_id == bookingId);
                if (booking == null)
                    return BadRequest("Booking not found");

                if (booking.status != "Assigned")
                    return BadRequest("Booking is not in Assigned status");

                var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == booking.shipment_id);
                if (shipment == null)
                    return BadRequest("Shipment not found");

                booking.status = "In-Transit";
                shipment.status = "In-Transit";
                booking.pickup_date = DateTime.Now;

                var customer = db.Customer.FirstOrDefault(c => c.customer_id == booking.customer_id);
                if (customer != null)
                    NotificationHelper.Send(db, customer.user_id, "Your shipment has been picked up by the driver. View details in the shipments tab.");

                db.SaveChanges();

                return Ok(new { message = "Booking picked up", bookingId = bookingId });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost]
        [Route("api/bookings/{bookingId}/deliver")]
        public IHttpActionResult DeliverBooking(int bookingId)
        {
            try
            {
                var booking = db.Bookings.FirstOrDefault(b => b.booking_id == bookingId);
                if (booking == null)
                    return BadRequest("Booking not found");

                if (booking.status != "In-Transit")
                    return BadRequest("Booking is not In-Transit");

                var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == booking.shipment_id);
                if (shipment == null)
                    return BadRequest("Shipment not found");

                booking.status = "Completed";
                booking.actual_delivery_datetime = DateTime.Now;
                shipment.status = "Delivered";

                var customer = db.Customer.FirstOrDefault(c => c.customer_id == booking.customer_id);
                if (customer != null)
                    NotificationHelper.Send(db, customer.user_id, "Your shipment has successfully been delivered. View details in the shipments tab.");


                db.SaveChanges();

                return Ok(new { message = "Booking delivered", bookingId = bookingId });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpPost]
        [Route("api/trips/start/{checkpointId}")]
        public IHttpActionResult StartTrip(int checkpointId, int driverId)
        {

            try
            {
                var checkpoint = db.Checkpoints.FirstOrDefault(c => c.checkpoint_id == checkpointId);
                if (checkpoint == null)
                    return BadRequest("Checkpoint not found");

                checkpoint.reached = true;

                int routeId = checkpoint.route_id;

                var route = db.Routes.FirstOrDefault(r => r.route_id == routeId && r.driver_id == driverId);
                if (route == null)
                    return BadRequest("This checkpoint does not belong to the driver's route");

                var trip = db.Trips.FirstOrDefault(t =>
                    t.route_id == routeId &&
                    t.status == "Scheduled"
                );

                if (trip != null)
                {
                    trip.start_time = DateTime.Now;
                    trip.status = "In-Transit";

                    var driver = db.Driver.FirstOrDefault(d => d.driver_id == driverId);
                    if (driver != null)
                        NotificationHelper.Send(db, driver.user_id, "Your trip has started. Safe travels!");

                }

                var bookingsToUpdate = (
                    from b in db.Bookings
                    join s in db.Shipments on b.shipment_id equals s.shipment_id
                    join t in db.Trips on b.trip_id equals t.trip_id
                    where b.route_id == routeId
                       && t.driver_id == driverId
                       && b.status == "Assigned"
                    select new { booking = b, shipment = s }
                ).ToList();

                //foreach (var item in bookingsToUpdate)
                //{
                //    item.booking.status = "In-Transit";
                //   // item.shipment.status = "In-Transit";
                //}

                db.SaveChanges();

                return Ok(new
                {
                    message = "Trip started",
                    checkpoint_id = checkpointId,
                    checkpoint_name = checkpoint.name,
                    trip_id = trip?.trip_id,
                    updated_bookings = bookingsToUpdate.Count
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpPost]
        [Route("api/bookings/{bookingId}/cancel")]
        public IHttpActionResult CancelBooking(int bookingId)
        {
            try
            {
                var booking = db.Bookings.FirstOrDefault(b => b.booking_id == bookingId);
                if (booking == null)
                    return BadRequest("Booking not found.");

                if (booking.status != "Assigned")
                    return BadRequest("Only assigned bookings can be canceled.");

                var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == booking.shipment_id);
                if (shipment == null)
                    return BadRequest("Shipment not found.");

                booking.status = "Canceled";
                shipment.status = "Pending";
                booking.cancel_reason = "Canceled by customer";

                var pickup = shipment.pickup_address ?? "Unknown pickup";
                var delivery = shipment.delivery_address ?? "Unknown delivery";

                var route = db.Routes.FirstOrDefault(r => r.route_id == booking.route_id);
                if (route != null)
                {
                    var driver = db.Driver.FirstOrDefault(d => d.driver_id == route.driver_id);
                    if (driver != null)
                        NotificationHelper.Send(
                            db,
                            driver.user_id,
                            $"Booking #{bookingId} ({pickup} → {delivery}) has been canceled by the customer."
                        );
                }


                db.SaveChanges();
                return Ok(new { message = "Booking canceled successfully.", bookingId = bookingId });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpPost]
        [Route("api/driver/add-delay")]
        public IHttpActionResult AddDelay(int checkpointId, double hours, string reason)
        {
            try
            {
                var targetCheckpoint = db.Checkpoints.FirstOrDefault(c => c.checkpoint_id == checkpointId);

                if (targetCheckpoint == null)
                    return BadRequest("Checkpoint not found.");

                int routeId = targetCheckpoint.route_id;
                int currentSequence = targetCheckpoint.sequence_no;

                var futureCheckpoints = db.Checkpoints
                    .Where(c => c.route_id == routeId && c.sequence_no >= currentSequence)
                    .ToList();

                foreach (var cp in futureCheckpoints)
                {
                    if (cp.estimated_arrival_datetime.HasValue)
                    {
                        cp.estimated_arrival_datetime = cp.estimated_arrival_datetime.Value.AddHours(hours);
                    }
                }

                var maxSequence = db.Checkpoints
                    .Where(c => c.route_id == routeId)
                    .Max(c => c.sequence_no);

                if (futureCheckpoints.Any(c => c.sequence_no == maxSequence))
                {
                    var schedule = db.RouteSchedule.FirstOrDefault(s => s.route_id == routeId);
                    if (schedule != null && schedule.arrivalDate.HasValue)
                    {
                        schedule.arrivalDate = schedule.arrivalDate.Value.AddHours(hours);
                    }
                }

                db.SaveChanges();

                var bookings = db.Bookings
                    .Where(b => b.route_id == routeId &&
                        (b.status == "Assigned" || b.status == "In-Transit"))
                .ToList();

                String time = "";
                var days = 0;
                while(hours >= 24)
                {
                    days++;
                    hours -= 24;
                }

                if(days > 0)
                    time = $"{days} day(s) and {hours} hour(s). ";
                else 
                    time = $"{hours} hour(s). ";

                foreach (Bookings b in bookings)
                    {
                        var customer = db.Customer.FirstOrDefault(c => c.customer_id == b.customer_id);
                        if (customer != null)
                            NotificationHelper.Send(db, customer.user_id, $"A delay of {time} has been added to your route due to: {reason}. ");
                    }
                var route = db.Routes.FirstOrDefault(r => r.route_id == routeId);
                if (route == null)
                    return BadRequest("Route not found.");

                var driver = db.Driver.FirstOrDefault(d => d.driver_id == route.driver_id);
                if (driver != null)
                    NotificationHelper.Send(db, driver.user_id, $"A delay of {time} has been added to your route due to: {reason}. Updated estimated arrival times have been calculated.");

                return Ok(new
                {
                    message = "Delay applied successfully",
                    affectedCheckpoints = futureCheckpoints.Count
                });
            }
            catch (Exception ex)
            {
                return InternalServerError();
            }
        }
        [HttpGet]
        [Route("api/checkpoints/estimated-arrival/{checkpointId}")]
        public IHttpActionResult GetEstimatedArrivalDate(int checkpointId)
        {
            var estimatedTime = db.Checkpoints
                .Where(c => c.checkpoint_id == checkpointId)
                .Select(c => c.estimated_arrival_datetime)
                .FirstOrDefault();

            if (estimatedTime == null)
            {
                return NotFound();
            }

            return Ok(estimatedTime);
        }
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
    }
}