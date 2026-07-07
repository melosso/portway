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

public class OpenApiSettings
{
    public bool Enabled { get; set; } = true;
    public string? BaseProtocol { get; set; } = "https";
    public string Title { get; set; } = "API Documentation";
    public string Version { get; set; } = "v1";
    public string Description { get; set; } = "A summary of the API documentation.";
    public ContactInfo Contact { get; set; } = new ContactInfo();
    public SecurityDefinitionInfo SecurityDefinition { get; set; } = new SecurityDefinitionInfo();
    public bool ForceHttpsInProduction { get; set; } = true; // Always use HTTPS in production environments

    // Scalar-specific
    public FooterInfo Footer { get; set; } = new FooterInfo();
    public string ScalarTheme { get; set; } = "purple"; // alternate, default, moon, purple, solarized, bluePlanet, saturn, kepler, mars, deepSpace
    public string ScalarLayout { get; set; } = "modern"; // modern, classic
    public bool ScalarShowSidebar { get; set; } = true;
    public bool ScalarHideDownloadButton { get; set; } = false;
    public bool ScalarHideModels { get; set; } = true; // Hide the Models/Schemas section
    public bool ScalarHideClientButton { get; set; } = true; // Hide the client generation button
    public bool ScalarHideTestRequestButton { get; set; } = false; // Hide the test request button
}
