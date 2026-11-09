namespace PortwayApi.Services.Providers;

public record SqlPoolingOptions(
    int MinPoolSize,
    int MaxPoolSize,
    int ConnectionTimeout,
    bool EnablePooling,
    string ApplicationName,

    // Applied as the driver's default command timeout for every Dapper execution
    int CommandTimeout = 30
);
