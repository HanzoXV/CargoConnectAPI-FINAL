using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace CargoConnectFinalAPI.Controllers
{
    public class ReviewsController : ApiController
    {
        CargoConnectEntity db = new CargoConnectEntity();

        [HttpGet]
        [Route("api/reviews/{userId}")]
        public IHttpActionResult GetAllReviews(int userId)
        {
            try
            {
                var reviews = db.Reviews
                    .Where(r => r.target_user_id == userId)
                    .OrderByDescending(r => r.created_at)
                    .Select(r => new
                    {
                        r.review_id,
                        r.trip_id,
                        r.reviewer_user_id,
                        r.target_user_id,
                        r.rating,
                        r.comments,
                        r.created_at,
                        reviewer_name = db.Driver
                            .Where(d => d.user_id == r.reviewer_user_id)
                            .Select(d => d.first_name + " " + d.last_name)
                            .FirstOrDefault() ??
                                db.Customer
                            .Where(c => c.user_id == r.reviewer_user_id)
                            .Select(c => c.first_name + " " + c.last_name)
                            .FirstOrDefault() ?? "Unknown"
                    })
                    .ToList();

                return Ok(reviews);
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"GetAllReviews failed for userId={userId}");
                return InternalServerError();
            }
        }

        [HttpPost]
        [Route("api/reviews/send")]
        public IHttpActionResult SendReview(int fromUserId, int toUserId, int tripId, int rating, string comments)
        {
            try
            {
                if (rating < 1 || rating > 5)
                    return BadRequest("Rating must be between 1 and 5.");

                var tripExists = db.Trips.Any(t => t.trip_id == tripId);
                if (!tripExists)
                    return BadRequest("Trip not found.");

                var alreadyReviewed = db.Reviews.Any(r =>
                    r.reviewer_user_id == fromUserId &&
                    r.target_user_id == toUserId &&
                    r.trip_id == tripId);

                if (alreadyReviewed)
                    return BadRequest("You have already reviewed this person for this trip.");

                var review = new Reviews
                {
                    reviewer_user_id = fromUserId,
                    target_user_id = toUserId,
                    trip_id = tripId,
                    rating = rating,
                    comments = comments,
                    created_at = DateTime.Now
                };

                db.Reviews.Add(review);
                db.SaveChanges();

                NotificationHelper.Send(db, toUserId, $"You received a {rating}★ review.");

                return Ok(new
                {
                    message = "Review submitted successfully.",
                    review_id = review.review_id
                });
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"SendReview failed: from={fromUserId}, to={toUserId}, trip={tripId}");
                return InternalServerError();
            }
        }

        [HttpGet]
        [Route("api/reviews/rating/{userId}")]
        public IHttpActionResult GetAverageRating(int userId)
        {
            try
            {
                var reviews = db.Reviews
                    .Where(r => r.target_user_id == userId)
                    .ToList();

                if (!reviews.Any())
                    return Ok(new { averageRating = 0.0, totalReviews = 0 });

                double average = reviews.Average(r => (double)(r.rating ?? 0));

                return Ok(new
                {
                    averageRating = Math.Round(average, 1),
                    totalReviews = reviews.Count
                });
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"GetAverageRating failed for userId={userId}");
                return InternalServerError();
            }
        }
        [HttpGet]
        [Route("api/reviews/booking-info/{bookingId}")]
        public IHttpActionResult GetReviewInfoForBooking(int bookingId)
        {
            try
            {
                var booking = db.Bookings.FirstOrDefault(b => b.booking_id == bookingId);
                if (booking == null)
                    return BadRequest("Booking not found.");

                var customer = db.Customer.FirstOrDefault(c => c.customer_id == booking.customer_id);
                if (customer == null)
                    return BadRequest("Customer not found.");

                var alreadyReviewed = db.Reviews.Any(r =>
                    r.trip_id == booking.trip_id &&
                    r.target_user_id == customer.user_id);

                return Ok(new
                {
                    booking_id = bookingId,
                    trip_id = booking.trip_id,
                    target_user_id = customer.user_id,
                    target_name = customer.first_name + " " + customer.last_name,
                    already_reviewed = alreadyReviewed
                });
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"GetReviewInfoForBooking failed: bookingId={bookingId}");
                return InternalServerError();
            }
        }
        [HttpGet]
        [Route("api/reviews/can-review")]
        public IHttpActionResult CanReview(int reviewerUserId, int bookingId)
        {
            try
            {
                var booking = db.Bookings.FirstOrDefault(b => b.booking_id == bookingId);
                if (booking == null)
                    return BadRequest("Booking not found.");

                // Get driver's user_id through route
                var route = db.Routes.FirstOrDefault(r => r.route_id == booking.route_id);
                if (route == null)
                    return BadRequest("Route not found.");

                var driver = db.Driver.FirstOrDefault(d => d.driver_id == route.driver_id);
                if (driver == null)
                    return BadRequest("Driver not found.");

                var alreadyReviewed = db.Reviews.Any(r =>
                    r.reviewer_user_id == reviewerUserId &&
                    r.target_user_id == driver.user_id &&
                    r.trip_id == booking.trip_id);

                return Ok(new
                {
                    can_review = !alreadyReviewed,
                    target_user_id = driver.user_id,
                    target_name = driver.first_name + " " + driver.last_name,
                    trip_id = booking.trip_id
                });
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"CanReview failed: reviewerUserId={reviewerUserId}, bookingId={bookingId}");
                return InternalServerError();
            }
        }
    }
}
