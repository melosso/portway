namespace PortwayApi.Services.Providers;

public record SqlPoolingOptions(
    int MinPoolSize,
    int MaxPoolSize,
    int ConnectionTimeout,
    bool EnablePooling,
    string ApplicationName,
    // Applied as the driver's default command timeout, so every Dapper call on the connection inherits it
    int CommandTimeout = 30
);
