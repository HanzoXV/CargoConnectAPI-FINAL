using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CargoConnectFinalAPI.Models
{
    public class PackageWithMapping
    {
        public Packages Package { get; set; }
        public List<int> AttributeIds { get; set; }
    }
}