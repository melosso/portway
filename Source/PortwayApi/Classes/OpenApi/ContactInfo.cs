using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PortwayApi.Classes.OpenApi;
using Scalar.AspNetCore;
using Serilog;

namespace PortwayApi.Classes;

public class ContactInfo
{
    public string Name { get; set; } = "Support";
    public string Email { get; set; } = "support@yourcompany.com";
}
