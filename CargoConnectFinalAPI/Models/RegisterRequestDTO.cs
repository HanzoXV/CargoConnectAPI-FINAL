using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class RegisterRequestDTO
    {
        public string Email { get; set; } 
        public string Password { get; set; } 
        public string Role { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string CNIC { get; set; }
        public string ContactNo { get; set; }
        public string StreetNo { get; set; }
        public string City { get; set; }
        public string PhotoLink { get; set; }
        public string departure { get; set; }
        public string arrival { get; set; }
        public string LicenseNo { get; set; }
        public VehicleDTO Vehicle { get; set; } 
        public UserDocumentsDTO Documents { get; set; }
    }
}