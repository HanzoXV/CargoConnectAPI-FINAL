using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Controllers
{
    public static class NotificationHelper
    {
        public static void Send(CargoConnectEntity db, int userId, string message)
        {
            db.Notifications.Add(new Notifications
            {
                user_id = userId,
                message = message,
                is_read = false,
                created_at = DateTime.Now
            });
            db.SaveChanges();
        }
    }
}