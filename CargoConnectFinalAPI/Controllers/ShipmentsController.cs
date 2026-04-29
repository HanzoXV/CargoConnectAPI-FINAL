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
    public class ShipmentsController : ApiController
    {
        CargoConnectEntity db = new CargoConnectEntity();

        [Route("api/shipments/draft/{customerId}")]
        [HttpPost]
        public IHttpActionResult CreateOrGetDraft(int customerId)
        {
            var customer = db.Customer.FirstOrDefault(c => c.customer_id == customerId);
            if (customer == null)
                return BadRequest("Customer not found");

            var draft = db.Shipments
                .FirstOrDefault(s => s.customer_id == customerId && s.status == "Draft");

            if (draft != null)
                return Ok(new { shipmentId = draft.shipment_id });

            var shipment = new Shipments
            {
                customer_id = customerId,
                sender_name = customer.first_name,
                sender_contact = customer.contact_no,
                package_count = 0,
                total_weight = 0,
                status = "Draft",
                pickup_date = DateTime.Now,
                shipment_radius = 1,
                shipment_type = "FULL"
            };

            db.Shipments.Add(shipment);
            db.SaveChanges();

            return Ok(new { shipmentId = shipment.shipment_id });
        }

        [Route("api/shipments/complete")]
        [HttpPost]
        public IHttpActionResult CompleteShipment(CompleteShipmentDto model)
        {
            if (model == null)
                return BadRequest("Invalid data");

            var shipment = db.Shipments
                .FirstOrDefault(s => s.shipment_id == model.shipment_id);

            if (shipment == null)
                return BadRequest("Shipment not found");

            if (shipment.status != "Draft")
                return BadRequest("Shipment already processed");

            var hasPackages = db.Packages
                .Any(p => p.shipment_id == model.shipment_id);

            if (!hasPackages)
                return BadRequest("Add at least one package before completing shipment");

            if (string.IsNullOrWhiteSpace(model.recipient_fname) ||
                string.IsNullOrWhiteSpace(model.recipient_contact))
                return BadRequest("Recipient details are required");

            // 🔥 FIX: DATE VALIDATION
            if (model.booking_date == null)
                return BadRequest("Pickup date is required");

            var existingRecipient = db.RecipientDetails
                .FirstOrDefault(r => r.shipment_id == model.shipment_id);

            // 🔥 LOCATION
            shipment.pickup_lat = model.pickup_lat;
            shipment.pickup_long = model.pickup_long;
            shipment.pickup_address = model.pickup_address;

            shipment.delivery_lat = model.delivery_lat;
            shipment.delivery_long = model.delivery_long;
            shipment.delivery_address = model.delivery_address;

            // 🔥 IMPORTANT FIX: SAVE DATE
            shipment.pickup_date = model.booking_date;

            shipment.strict = model.strict;
            shipment.status = "Pending";
            shipment.shipment_radius = model.shipment_radius;
            shipment.shipment_type = model.shipment_type;

            if (existingRecipient != null)
            {
                existingRecipient.recipient_fname = model.recipient_fname;
                existingRecipient.recipient_lname = model.recipient_lname;
                existingRecipient.recipient_contact = model.recipient_contact;
                existingRecipient.instructionsMessage = model.instructionsMessage;
            }
            else
            {
                var recipient = new RecipientDetails
                {
                    shipment_id = model.shipment_id,
                    recipient_fname = model.recipient_fname,
                    recipient_lname = model.recipient_lname,
                    recipient_contact = model.recipient_contact,
                    instructionsMessage = model.instructionsMessage
                };

                db.RecipientDetails.Add(recipient);
            }

            db.SaveChanges();

            return Ok(new
            {
                message = "Shipment completed successfully",
                shipmentId = shipment.shipment_id,
                status = shipment.status,
                pickupDate = shipment.pickup_date
            });
        }

        [Route("api/shipments/add/package")]
        [HttpPost]
        public IHttpActionResult AddPackage(PackageWithMapping request)
        {
            if (request?.Package == null)
                return BadRequest("Invalid package data");

            var shipment = db.Shipments
                .FirstOrDefault(s => s.shipment_id == request.Package.shipment_id);

            if (shipment == null)
                return BadRequest("Shipment not found");

            if (shipment.status != "Draft")
                return BadRequest("Cannot modify non-draft shipment");

            db.Packages.Add(request.Package);
            db.SaveChanges();

            if (request.AttributeIds != null)
            {
                foreach (var attributeId in request.AttributeIds)
                {
                    db.PackageAttributeMapping.Add(new PackageAttributeMapping
                    {
                        package_id = request.Package.package_id,
                        attribute_id = attributeId
                    });
                }
            }

            // Update totals
            shipment.package_count = db.Packages.Count(p => p.shipment_id == shipment.shipment_id);
            shipment.total_weight = db.Packages
                .Where(p => p.shipment_id == shipment.shipment_id)
                .Sum(p => p.weight ?? 0);

            db.SaveChanges();

            return Ok(new
            {
                packageId = request.Package.package_id,
                shipmentId = shipment.shipment_id
            });
        }
        [Route("api/shipments/delete/package/{id}")]
        [HttpDelete]
        public IHttpActionResult DeletePackage(int id)
        {
            var package = db.Packages.FirstOrDefault(p => p.package_id == id);
            if (package == null)
                return NotFound();

            var shipment = db.Shipments.FirstOrDefault(s => s.shipment_id == package.shipment_id);
            if (shipment.status != "Draft")
                return BadRequest("Cannot modify non-draft shipment");

            var mappings = db.PackageAttributeMapping.Where(m => m.package_id == id).ToList();
            foreach (var m in mappings)
                db.PackageAttributeMapping.Remove(m);

            db.Packages.Remove(package);
            db.SaveChanges();

            shipment.package_count = db.Packages.Count(p => p.shipment_id == shipment.shipment_id);
            shipment.total_weight = db.Packages
                .Where(p => p.shipment_id == shipment.shipment_id)
                .Sum(p => p.weight ?? 0);

            db.SaveChanges();

            return Ok("Package deleted successfully");
        }
        [HttpGet]
        [Route("api/shipments/pending/customer/{customerId}")]
        public IHttpActionResult GetCustomerPendingShipments(int customerId)
        {
            var shipments = db.Shipments
                .Where(s => s.customer_id == customerId && s.status == "Pending")
                .Select(s => new ShipmentDto
                {
                    shipment_id = s.shipment_id,
                    pickup_address = s.pickup_address,
                    delivery_address = s.delivery_address,
                    status = s.status,
                    sender_name = s.sender_name,
                    sender_contact = s.sender_contact,
                    total_weight = s.total_weight,
                    strict = s.strict,
                    shipment_radius = s.shipment_radius,
                    shipment_type = s.shipment_type,
                    booking_date = s.pickup_date.ToString(),
                    packages = db.Packages
                        .Where(p => p.shipment_id == s.shipment_id)
                        .Select(p => new PackageDto
                        {
                            shipment_id = p.shipment_id,
                            name = p.name,
                            weight = p.weight,
                            length = p.length,
                            width = p.width,
                            height = p.height,
                            quantity = p.quantity,
                            color = p.color,
                            tagNo = p.tagNo
                        }).ToList()
                })
                .ToList();

            return Ok(shipments);
        }

        [HttpGet]
        [Route("api/customers/{customerId}/bookings")]
        public IHttpActionResult GetCustomerBookings(int customerId)
        {
            var bookings = (
                from b in db.Bookings
                join r in db.Routes on b.route_id equals r.route_id into routeGroup
                from r in routeGroup.DefaultIfEmpty()
                join d in db.Driver on r.driver_id equals d.driver_id into driverGroup
                from d in driverGroup.DefaultIfEmpty()
                join s in db.Shipments on b.shipment_id equals s.shipment_id into shipmentGroup
                from s in shipmentGroup.DefaultIfEmpty()
                join rs in db.RouteSchedule on r.route_id equals rs.route_id into scheduleGroup
                from rs in scheduleGroup.DefaultIfEmpty()
                join v in db.Vehicle on r.driver_id equals v.driver_id into vehicleGroup
                from v in vehicleGroup.DefaultIfEmpty()
                join rd in db.RecipientDetails
                    on b.shipment_id equals rd.shipment_id into recipientGroup
                from rd in recipientGroup.DefaultIfEmpty()

                let fromCp = db.Checkpoints
                    .Where(c => r != null && c.route_id == r.route_id)
                    .OrderBy(c => c.sequence_no)
                    .FirstOrDefault()

                let toCp = db.Checkpoints
                    .Where(c => r != null && c.route_id == r.route_id)
                    .OrderByDescending(c => c.sequence_no)
                    .FirstOrDefault()

                where b.customer_id == customerId
                select new
                {
                    // Booking
                    id = b.booking_id,
                    status = b.status,
                    amount = b.amount,
                    booking_type = b.booking_type,
                    pickup_date = b.pickup_date,
                    estimated_delivery = b.estimated_delivery_datetime,
                    actual_delivery = b.actual_delivery_datetime,

                    // Route checkpoints
                    fromCheckpoint = fromCp != null ? fromCp.name : null,
                    toCheckpoint = toCp != null ? toCp.name : null,

                    // Schedule
                    departure_date = rs != null ? rs.departureDate : null,
                    arrival_date = rs != null ? rs.arrivalDate : null,

                    // Driver
                    driver_name = d != null ? d.first_name + " " + d.last_name : null,
                    driver_contact = d != null ? d.contact_no : null,
                    license_no = d != null ? d.licence_no : null,

                    // Vehicle
                    vehicle_model = v != null ? v.model : null,
                    vehicle_reg_no = v != null ? v.vehicle_reg_no : null,

                    // Shipment
                    shipment_id = s != null ? s.shipment_id : (int?)null,
                    pickup_address = s != null ? s.pickup_address : null,
                    delivery_address = s != null ? s.delivery_address : null,
                    total_weight = s != null ? s.total_weight : null,
                    package_count = s != null ? s.package_count : null,

                    // Recipient
                    recipient_name = rd != null ? rd.recipient_fname + " " + rd.recipient_lname : null,
                    recipient_contact = rd != null ? rd.recipient_contact : null,

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
                }
            ).ToList();

            return Ok(bookings);
        }
        [HttpGet]
        [Route("api/customers/{customerId}/recent-packages")]
        public IHttpActionResult GetRecentPackages(int customerId)
        {
            var recentPackages = db.Packages
                .Where(p => db.Shipments
                    .Where(s => s.customer_id == customerId)
                    .Select(s => s.shipment_id)
                    .Contains(p.shipment_id))
                .OrderByDescending(p => p.package_id)
                .ToList()
                .GroupBy(p => p.name.ToLower().Trim())
                .Select(g => g.First())
                .Take(5)
                .Select(p => new
                {
                    p.name,
                    p.weight,
                    p.length,
                    p.width,
                    p.height,
                    p.quantity
                })
                .ToList();

            return Ok(recentPackages);
        }
    }
}
