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

public class FooterInfo
{
    public string Text { get; set; } = "Powered by Scalar";
    public string Target { get; set; } = "_blank";
    public string Url { get; set; } = "#";
    public bool ShowSourceIcon { get; set; } = true;
}
