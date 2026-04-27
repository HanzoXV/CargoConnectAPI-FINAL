using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class UserDocumentsDTO
    {
        public string CnicLink { get; set; }
        public string LicenseLink { get; set; }
        public string FrontLink { get; set; }
        public string BackLink { get; set; }
    }
}