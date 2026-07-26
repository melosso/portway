namespace PortwayApi.Classes.OpenApi;

/// <summary>The distinct operation shapes Portway documents, each with its own error surface</summary>
public enum ApiOperationKind
{
    SqlRead,

    /// <summary>POST, PUT, PATCH or MERGE</summary>
    SqlWrite,

    /// <summary>Search over a request body (RFC 10008 QUERY)</summary>
    SqlQuery,

    SqlDelete,
    Proxy,
    Composite,
    Static,
    Webhook,
    FileUpload,
    FileDownload,
    FileDelete,
    FileList
}
