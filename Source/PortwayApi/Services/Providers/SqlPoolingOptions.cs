namespace PortwayApi.Services.Providers;

public record SqlPoolingOptions(
    int MinPoolSize,
    int MaxPoolSize,
    int ConnectionTimeout,
    bool EnablePooling,
    string ApplicationName
);
