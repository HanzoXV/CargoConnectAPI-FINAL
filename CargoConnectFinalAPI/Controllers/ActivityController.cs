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
        [Route("api/chat/thread")]
        public IHttpActionResult GetOrCreateThread(int customerUserId, int driverUserId)
        {
            try
            {
                var thread = db.ChatThreads.FirstOrDefault(t =>
                    t.customer_user_id == customerUserId &&
                    t.driver_user_id == driverUserId);

                if (thread == null)
                {
                    thread = new ChatThreads
                    {
                        customer_user_id = customerUserId,
                        driver_user_id = driverUserId,
                        created_at = DateTime.Now
                    };
                    db.ChatThreads.Add(thread);
                    db.SaveChanges();
                }

                return Ok(new { thread_id = thread.thread_id });
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"GetOrCreateThread failed: customerUserId={customerUserId}, driverUserId={driverUserId}");
                return InternalServerError();
            }
        }

        [HttpGet]
        [Route("api/chat/list/{userId}")]
        public IHttpActionResult GetChatList(int userId)
        {
            try
            {
                var threads = db.ChatThreads
                    .Where(t => t.customer_user_id == userId || t.driver_user_id == userId)
                    .ToList();

                var result = threads.Select(t =>
                {
                    var lastMessage = db.ChatMessages
                        .Where(m => m.thread_id == t.thread_id)
                        .OrderByDescending(m => m.sent_at)
                        .FirstOrDefault();

                    int unreadCount = db.ChatMessages
                        .Count(m => m.thread_id == t.thread_id
                            && m.sender_user_id != userId
                            && m.is_read == false);

                    // Get the other person's name
                    int otherUserId = t.customer_user_id == userId
                        ? t.driver_user_id
                        : t.customer_user_id;

                    string otherName = db.Driver
                        .Where(d => d.user_id == otherUserId)
                        .Select(d => d.first_name + " " + d.last_name)
                        .FirstOrDefault() ??
                        db.Customer
                        .Where(c => c.user_id == otherUserId)
                        .Select(c => c.first_name + " " + c.last_name)
                        .FirstOrDefault() ?? "Unknown";

                    return new
                    {
                        thread_id = t.thread_id,
                        other_user_id = otherUserId,
                        other_name = otherName,
                        last_message = lastMessage?.message_text ?? "",
                        last_message_time = lastMessage?.sent_at,
                        unread_count = unreadCount
                    };
                })
                .OrderByDescending(t => t.last_message_time)
                .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"GetChatList failed: userId={userId}");
                return InternalServerError();
            }
        }

        [HttpGet]
        [Route("api/chat/history/{threadId}")]
        public IHttpActionResult GetChatHistory(int threadId, int userId)
        {
            try
            {
                var thread = db.ChatThreads.FirstOrDefault(t => t.thread_id == threadId);
                if (thread == null)
                    return BadRequest("Thread not found.");

                if (thread.customer_user_id != userId && thread.driver_user_id != userId)
                    return BadRequest("You are not a participant of this thread.");

                // Mark all messages from the other person as read
                var unreadMessages = db.ChatMessages
                    .Where(m => m.thread_id == threadId
                        && m.sender_user_id != userId
                        && m.is_read == false)
                    .ToList();

                foreach (var m in unreadMessages)
                    m.is_read = true;

                db.SaveChanges();

                var messages = db.ChatMessages
                    .Where(m => m.thread_id == threadId)
                    .OrderBy(m => m.sent_at)
                    .Select(m => new
                    {
                        m.message_id,
                        m.thread_id,
                        m.sender_user_id,
                        is_mine = m.sender_user_id == userId,
                        m.message_text,
                        m.is_read,
                        m.sent_at
                    })
                    .ToList();

                return Ok(messages);
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"GetChatHistory failed: threadId={threadId}, userId={userId}");
                return InternalServerError();
            }
        }

        [HttpPost]
        [Route("api/chat/send")]
        public IHttpActionResult SendMessage(int threadId, int senderUserId, string messageText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageText))
                    return BadRequest("Message cannot be empty.");

                var thread = db.ChatThreads.FirstOrDefault(t => t.thread_id == threadId);
                if (thread == null)
                    return BadRequest("Thread not found.");

                if (thread.customer_user_id != senderUserId && thread.driver_user_id != senderUserId)
                    return BadRequest("You are not a participant of this thread.");

                var message = new ChatMessages
                {
                    thread_id = threadId,
                    sender_user_id = senderUserId,
                    message_text = messageText,
                    is_read = false,
                    sent_at = DateTime.Now
                };

                db.ChatMessages.Add(message);

                // Notify the other person
                int receiverUserId = thread.customer_user_id == senderUserId
                    ? thread.driver_user_id
                    : thread.customer_user_id;

                string senderName = db.Driver
                    .Where(d => d.user_id == senderUserId)
                    .Select(d => d.first_name + " " + d.last_name)
                    .FirstOrDefault() ??
                    db.Customer
                    .Where(c => c.user_id == senderUserId)
                    .Select(c => c.first_name + " " + c.last_name)
                    .FirstOrDefault() ?? "Someone";

                db.SaveChanges();

                NotificationHelper.Send(db, receiverUserId, $"{senderName} sent you a message.");

                return Ok(new
                {
                    message_id = message.message_id,
                    thread_id = threadId,
                    sent_at = message.sent_at
                });
            }
            catch (Exception ex)
            {
                //Logger.Error(ex, $"SendMessage failed: threadId={threadId}, senderUserId={senderUserId}");
                return InternalServerError();
            }
        }
    }
}