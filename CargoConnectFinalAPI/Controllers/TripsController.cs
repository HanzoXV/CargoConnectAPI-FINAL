using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
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
        [HttpGet]
        [Route("api/checkpoints/{checkpointId}/shipments")]
        public IHttpActionResult GetShipmentsAtCheckpoint(int checkpointId, int driverId)
        {
            var checkpoint = db.Checkpoints.FirstOrDefault(c => c.checkpoint_id == checkpointId);
            if (checkpoint == null)
                return BadRequest("Checkpoint not found");

            int routeId = checkpoint.route_id;

            // Verify checkpoint belongs to this driver's route
            var route = db.Routes.FirstOrDefault(r => r.route_id == routeId && r.driver_id == driverId);
            if (route == null)
                return BadRequest("This checkpoint does not belong to the driver's route");

            var allBookings = (
                from b in db.Bookings
                join t in db.Trips on b.trip_id equals t.trip_id
                join s in db.Shipments on b.shipment_id equals s.shipment_id
                join r in db.RecipientDetails
                    on b.shipment_id equals r.shipment_id into recipientGroup
                from r in recipientGroup.DefaultIfEmpty()
                where b.route_id == routeId
                   && t.driver_id == driverId
                   && (b.status == "Assigned" || b.status == "In-Transit")
                   && s.delivery_lat != null
                   && s.delivery_long != null
                   && s.pickup_lat != null
                   && s.pickup_long != null
                select new
                {
                    booking_id = b.booking_id,
                    shipment_id = b.shipment_id,
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

                    // Recipient
                    recipient_name = r != null ? r.recipient_fname + " " + r.recipient_lname : null,
                    recipient_contact = r != null ? r.recipient_contact : null,

                    // Packages
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

            // Shipments to DROP — delivery location matches this checkpoint
            var toDrop = allBookings
                .Where(b =>
                    b.delivery_lat.HasValue && b.delivery_long.HasValue &&
                    Math.Abs(b.delivery_lat.Value - checkpoint.latitude.Value) < 0.05 &&
                    Math.Abs(b.delivery_long.Value - checkpoint.longitude.Value) < 0.05
                ).ToList();

            // Shipments to LOAD — pickup location matches this checkpoint
            var toLoad = allBookings
                .Where(b =>
                    b.pickup_lat.HasValue && b.pickup_long.HasValue &&
                    Math.Abs(b.pickup_lat.Value - checkpoint.latitude.Value) < 0.05 &&
                    Math.Abs(b.pickup_long.Value - checkpoint.longitude.Value) < 0.05
                ).ToList();

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
            try
            {
                var checkpoint = db.Checkpoints.FirstOrDefault(c => c.checkpoint_id == checkpointId);
                if (checkpoint == null)
                    return BadRequest("Checkpoint not found");

                checkpoint.reached = true;
                int routeId = checkpoint.route_id;

                // Verify checkpoint belongs to this driver
                var route = db.Routes.FirstOrDefault(r => r.route_id == routeId && r.driver_id == driverId);
                if (route == null)
                    return BadRequest("This checkpoint does not belong to the driver's route");

                // LOAD — pickup location matches checkpoint → set to In-Transit
                var toLoad = (
                    from b in db.Bookings
                    join s in db.Shipments on b.shipment_id equals s.shipment_id
                    join t in db.Trips on b.trip_id equals t.trip_id
                    where b.route_id == routeId
                       && t.driver_id == driverId
                       && b.status == "Assigned"
                       && s.pickup_lat != null
                       && s.pickup_long != null
                       && Math.Abs(s.pickup_lat.Value - checkpoint.latitude.Value) < 0.05
                       && Math.Abs(s.pickup_long.Value - checkpoint.longitude.Value) < 0.05
                    select new { booking = b, shipment = s }
                ).ToList();

                foreach (var item in toLoad)
                {
                    item.booking.status = "In-Transit";
                    item.shipment.status = "In-Transit";
                }

                // DROP — delivery location matches checkpoint → set to Delivered
                var toDrop = (
                    from b in db.Bookings
                    join s in db.Shipments on b.shipment_id equals s.shipment_id
                    join t in db.Trips on b.trip_id equals t.trip_id
                    where b.route_id == routeId
                       && t.driver_id == driverId
                       && b.status == "In-Transit"
                       && s.delivery_lat != null
                       && s.delivery_long != null
                       && Math.Abs(s.delivery_lat.Value - checkpoint.latitude.Value) < 0.05
                       && Math.Abs(s.delivery_long.Value - checkpoint.longitude.Value) < 0.05
                    select new { booking = b, shipment = s }
                ).ToList();

                foreach (var item in toDrop)
                {
                    item.booking.status = "Completed";
                    item.booking.actual_delivery_datetime = DateTime.Now;
                    item.shipment.status = "Delivered";
                }

                // Check if this is the last checkpoint
                var lastCheckpoint = db.Checkpoints
                    .Where(c => c.route_id == routeId)
                    .OrderByDescending(c => c.sequence_no)
                    .FirstOrDefault();

                if (lastCheckpoint != null && lastCheckpoint.checkpoint_id == checkpointId)
                {
                    // Complete the trip
                    var trip = db.Trips.FirstOrDefault(t =>
                        t.route_id == routeId &&
                        t.status == "In-Transit"
                    );

                    if (trip != null)
                    {
                        trip.end_time = DateTime.Now;
                        trip.status = "Completed";
                    }

                    // Rotate routes
                    var activeRoute = db.Routes.FirstOrDefault(r =>
                        r.route_id == routeId && r.is_active == true);

                    if (activeRoute != null)
                    {
                        var nextRoute = db.Routes.FirstOrDefault(r =>
                            r.driver_id == driverId &&
                            r.is_next_route == true &&
                            r.route_id != routeId
                        );

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
                    checkpoint_name = checkpoint.name,
                    loaded = toLoad.Count,
                    delivered = toDrop.Count
                });
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

                // Verify checkpoint belongs to this driver
                var route = db.Routes.FirstOrDefault(r => r.route_id == routeId && r.driver_id == driverId);
                if (route == null)
                    return BadRequest("This checkpoint does not belong to the driver's route");

                // Update trip status
                var trip = db.Trips.FirstOrDefault(t =>
                    t.route_id == routeId &&
                    t.status == "Scheduled"
                );

                if (trip != null)
                {
                    trip.start_time = DateTime.Now;
                    trip.status = "In-Transit";
                }

                // Set all Assigned bookings + shipments on this route to In-Transit
                var bookingsToUpdate = (
                    from b in db.Bookings
                    join s in db.Shipments on b.shipment_id equals s.shipment_id
                    join t in db.Trips on b.trip_id equals t.trip_id
                    where b.route_id == routeId
                       && t.driver_id == driverId
                       && b.status == "Assigned"
                    select new { booking = b, shipment = s }
                ).ToList();

                foreach (var item in bookingsToUpdate)
                {
                    item.booking.status = "In-Transit";
                    item.shipment.status = "In-Transit";
                }

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

    }
}