namespace PortwayApi.Tests.Parity;

public sealed class MsSqlProviderParityTests(MsSqlParityFixture fixture)
    : ProviderParityTests<MsSqlParityFixture>(fixture);

public sealed class MsSqlProviderHttpParityTests(MsSqlParityFixture fixture)
    : ProviderHttpParityTests<MsSqlParityFixture>(fixture);
