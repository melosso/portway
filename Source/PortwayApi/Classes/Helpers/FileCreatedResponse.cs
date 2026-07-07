using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Classes.Helpers;

// 201 Created; File upload (distinct shape per HTTP semantics)
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
