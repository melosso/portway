using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

// 201 Created response shape for file uploads, distinct from other endpoints' success responses
public sealed record FileCreatedResponse(
    [property: JsonPropertyName("success")]     bool   Success,
    [property: JsonPropertyName("fileId")]      string FileId,
    [property: JsonPropertyName("filename")]    string Filename,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("size")]        long   Size,
    [property: JsonPropertyName("url")]         string Url)
{
    public static FileCreatedResponse Of(
        string fileId, string filename, string contentType, long size, string url)
        => new(true, fileId, filename, contentType, size, url);
}
