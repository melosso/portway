namespace PortwayApi.Classes.Providers;

public record SqlPoolingOptions(
    int MinPoolSize,
    int MaxPoolSize,
    int ConnectionTimeout,
    bool EnablePooling,
    string ApplicationName
);
