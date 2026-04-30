using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace CargoConnectFinalAPI.Controllers
{
    public class ActivityController : ApiController
    {
        CargoConnectEntity db = new CargoConnectEntity();

        [HttpGet]
        [Route("api/activity/notifications/{userId}")]
        public IHttpActionResult GetNotifications(int userId)
        {
            var notifications = db.Notifications
                .Where(n => n.user_id == userId)
                .OrderByDescending(n => n.created_at)
                .Select(n => new
                {
                    n.notification_id,
                    n.message,
                    n.is_read,
                    n.created_at
                }).ToList();

            return Ok(notifications);
        }

        [HttpPost]
        [Route("api/activity/sendnotification")]
        public IHttpActionResult SendNotification(int userId, string message)
        {
            InternalSendNotification(userId, message);
            return Ok("Notification Sent");
        }
        [NonAction]
        public void InternalSendNotification(int userId, string message)
        {
            Notifications notif = new Notifications
            {
                user_id = userId,
                message = message,
                is_read = false,
                created_at = DateTime.Now
            };
            db.Notifications.Add(notif);
            db.SaveChanges();
        }
    }
}