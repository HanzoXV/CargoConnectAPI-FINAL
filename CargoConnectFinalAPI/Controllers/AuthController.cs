using CargoConnectFinalAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace CargoConnectFinalAPI.Controllers
{
    public class AuthController : ApiController
    {
        CargoConnectEntity db = new CargoConnectEntity();

        [HttpGet]
        [Route("api/auth/status")]
        public IHttpActionResult GetAuthStatus()
        {
            return Ok("SUCCESS: Auth Connection successful.");
        }

        [HttpPost]
        [Route("api/auth/register")]
        public IHttpActionResult Register(RegisterRequestDTO request)
        {
            if (request == null ||
                string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Password) ||
                string.IsNullOrEmpty(request.Role))
                return BadRequest("ERROR: Invalid registration data.");

            if (UserExists(request.Email))
                return BadRequest("ERROR: User already exists.");

            var user = new Users
            {
                email = request.Email,
                password = request.Password,
                role_id = GetRoleID(request.Role),
                joindate = DateTime.Now,
                suspended = false,
                is_active = true,
                last_login = DateTime.Now,
                updated_at = DateTime.Now
            };
            db.Users.Add(user);
            db.SaveChanges();

            if (request.Role == "Driver")
            {
                if (request.Vehicle == null || request.Documents == null)
                    return BadRequest("ERROR: Vehicle Documents or Info not received.");

                var driver = new Driver
                {
                    user_id = user.user_id,
                    first_name = request.FirstName,
                    last_name = request.LastName,
                    CNIC = request.CNIC,
                    contact_no = request.ContactNo,
                    licence_no = request.LicenseNo,
                    city = request.City,
                    street_no = request.StreetNo,
                    profile_image_url = request.PhotoLink,
                    is_available = true
                };

                db.Driver.Add(driver);
                db.SaveChanges();

                var vehicle = new Vehicle
                {
                    vehicle_reg_no = request.Vehicle.RegNo,
                    driver_id = driver.driver_id,
                    model = request.Vehicle.Model,
                    type = request.Vehicle.Type,
                    weight_capacity = Double.Parse(request.Vehicle.WeightCapacity),
                    length = Double.Parse(request.Vehicle.Length),
                    width = Double.Parse(request.Vehicle.Width),
                    height = Double.Parse(request.Vehicle.Height)
                };

                db.Vehicle.Add(vehicle);

                var docs = new DriverDocuments
                {
                    driver_id = driver.driver_id,
                    uploaded_at = DateTime.Now,
                    cnic_link = request.Documents.CnicLink,
                    license_link = request.Documents.LicenseLink,
                    front_link = request.Documents.FrontLink,
                    back_link = request.Documents.BackLink,
                    photo_link = request.PhotoLink
                };

                db.DriverDocuments.Add(docs);
                db.SaveChanges();

            }
            else if (request.Role == "Customer")
            {
                var customer = new Customer
                {
                    user_id = user.user_id,
                    first_name = request.FirstName,
                    last_name = request.LastName,
                    CNIC = request.CNIC,
                    contact_no = request.ContactNo,
                    city = request.City,
                    street_no = request.StreetNo,
                    profile_image_url = request.PhotoLink
                };

                db.Customer.Add(customer);
                db.SaveChanges();
            }
            else
            {
                return BadRequest("ERROR: Invalid role.");
            }
            int id = -1;
            if (user.role_id == 1)
            {
                id = db.Driver.FirstOrDefault(r => r.user_id == user.user_id)?.driver_id ?? -1;
            }
            else if (user.role_id == 2)
            {
                id = db.Customer.FirstOrDefault(r => r.user_id == user.user_id)?.customer_id ?? -1;
            }
            else if (user.role_id == 3)
            {
                id = db.Admin.FirstOrDefault(r => r.user_id == user.user_id)?.admin_id ?? -1;
            }

            NotificationHelper.Send(db, user.user_id, "Welcome to CargoConnect! Your registration was successful.");
            return Ok(new
            {
                message = "SUCCESS: Registration successful",
                roleID = id,
                roleName = request.Role.ToString(),
                userId = user.user_id,
                roleBasedId = id
            });
        }
        [HttpPost]
        [Route("api/auth/login")]
        public IHttpActionResult LoginUser(LoginRequestDTO request)
        {
            int id = -1;
            if (request == null ||
                string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Password))
                return BadRequest("ERROR: Input data is empty or invalid.");

            var email = request.Email.Trim().ToLower();
            var password = request.Password.Trim();

            var existingUser = db.Users.FirstOrDefault(u =>
                u.email.ToLower() == email &&
                u.password == password
            );

            if (existingUser == null)
                return BadRequest("ERROR: Username or password not found.");

            if (existingUser.role_id == 1)
            {
                id = db.Driver.FirstOrDefault(r => r.user_id == existingUser.user_id)?.driver_id ?? -1;
            }
            else if (existingUser.role_id == 2)
            {
                id = db.Customer.FirstOrDefault(r => r.user_id == existingUser.user_id)?.customer_id ?? -1;
            }
            else if (existingUser.role_id == 3)
            {
                id = db.Admin.FirstOrDefault(r => r.user_id == existingUser.user_id)?.admin_id ?? -1;
            }

            return Ok(new
            {
                message = "SUCCESS: Login successful.",
                roleID = existingUser.role_id,
                roleName = db.Roles
                        .FirstOrDefault(r => r.role_id == existingUser.role_id)?.role_name,
                userID = existingUser.user_id,
                roleBasedId = id
            });
        }
        [HttpPost]
        [Route("api/auth/upload")]
        public async Task<IHttpActionResult> UploadFile()
        {
            if (!Request.Content.IsMimeMultipartContent())
            {
                return BadRequest("Unsupported media type.");
            }

            try
            {
                var root = System.Web.HttpContext.Current.Server.MapPath("~/Uploads");
                if (!System.IO.Directory.Exists(root)) System.IO.Directory.CreateDirectory(root);

                var provider = new MultipartFormDataStreamProvider(root);
                await Request.Content.ReadAsMultipartAsync(provider);

                var file = provider.FileData.FirstOrDefault();
                if (file == null) return BadRequest("No file uploaded.");

                var originalFileName = file.Headers.ContentDisposition.FileName.Trim('\"');
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + originalFileName;
                var fullPath = System.IO.Path.Combine(root, uniqueFileName);

                System.IO.File.Move(file.LocalFileName, fullPath);

                var request = System.Web.HttpContext.Current.Request;
                var baseUrl = $"{request.Url.Scheme}://{request.Url.Authority}{request.ApplicationPath.TrimEnd('/')}/Uploads/{uniqueFileName}";

                return Ok(new { link = baseUrl });
            }
            catch (Exception ex)
            {
                return BadRequest("EXCEPTION: " + ex.Message);
            }
        }
        [HttpPost]
        [Route("api/users/getdata")]
        public IHttpActionResult GetUserData([FromBody] UserIdRequest request)
        {
            if (request == null) return BadRequest("Invalid request");

            var user = db.Users.Find(request.userId);
            if (user == null) return NotFound();

            object userData = null;

            if (user.role_id == 1)
            {
                userData = db.Driver.Where(d => d.user_id == request.userId)
                    .Select(d => new {
                        name = d.first_name + " " + d.last_name,
                        contact = d.contact_no,
                        license_no = d.licence_no,
                        street_no = d.street_no,
                        city = d.city,
                        profileImageUrl = d.profile_image_url,

                        allRoutes = db.Routes.Where(r => r.driver_id == d.driver_id).Select(r => new {
                            r.route_id,
                            r.is_active,
                            r.is_next_route,
                            points = db.Checkpoints.Where(c => c.route_id == r.route_id)
                                       .OrderBy(c => c.sequence_no)
                                       .Select(c => new { c.name, c.latitude, c.longitude, c.sequence_no, c.reached })
                                       .ToList()
                        }).ToList(),

                        activeRoute = db.Routes.Where(r => r.driver_id == d.driver_id && r.is_active == true).Select(r => new {
                            r.route_id,
                            points = db.Checkpoints.Where(c => c.route_id == r.route_id)
                                       .OrderBy(c => c.sequence_no)
                                       .Select(c => new { c.name, c.latitude, c.longitude, c.sequence_no, c.reached })
                                       .ToList()
                        }).FirstOrDefault(),

                        nextRoute = db.Routes.Where(r => r.driver_id == d.driver_id && r.is_next_route == true).Select(r => new {
                            r.route_id,
                            points = db.Checkpoints.Where(c => c.route_id == r.route_id)
                                       .OrderBy(c => c.sequence_no)
                                       .Select(c => new { c.name, c.latitude, c.longitude, c.sequence_no, c.reached })
                                       .ToList()
                        }).FirstOrDefault()

                    }).FirstOrDefault();
            }
            else if (user.role_id == 2)
            {
                userData = db.Customer.Where(c => c.user_id == request.userId)
                    .Select(c => new {
                        name = c.first_name + " " + c.last_name,
                        contact = c.contact_no,
                        license_no = "N/A",
                        street_no = c.street_no,
                        city = c.city,
                        profileImageUrl = c.profile_image_url
                    }).FirstOrDefault();
            }
            else if (user.role_id == 3) // Admin
            {
                userData = db.Admin.Where(a => a.user_id == request.userId)
                    .Select(a => new {
                        name = a.first_name + " " + a.last_name,
                        contact = a.contact_no,
                        license_no = "N/A",
                        street_no = "Office",
                        city = "Headquarters",
                        profileImageUrl = "N/A"
                    }).FirstOrDefault();
            }

            if (userData == null) return NotFound();

            return Ok(userData);
        }
        public bool UserExists(String email)
        {
            return db.Users.Any(u => u.email == email);
        }
        public int GetRoleID(string role)
        {
            switch (role)
            {
                case "Driver": return 1;
                case "Customer": return 2;
                case "Admin": return 3;
                default: return -1;
            }
        }
    }
    public class UserIdRequest
    {
        public int userId { get; set; }
    }
}
