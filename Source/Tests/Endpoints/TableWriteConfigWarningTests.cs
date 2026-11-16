using PortwayApi.Classes;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// CheckConfig returns a warning for a WriteMode Table endpoint that fails table write validation
/// </summary>
public class TableWriteConfigWarningTests
{
    [Fact]
    public void CheckConfig_WarnsWhenTableWritesLackPrimaryKey()
    {
        var (errors, warnings) = EndpointHandler.CheckConfig(EndpointType.SQL,
            """{"DatabaseObjectName":"Items","DatabaseObjectType":"Table","WriteMode":"Table","AllowedColumns":["Id"],"AllowedMethods":["GET","POST"]}""");

        Assert.Empty(errors);
        Assert.Contains(warnings, w => w.Contains("WriteMode") && w.Contains("PrimaryKey"));
    }

    [Fact]
    public void CheckConfig_AcceptsCompleteTableWrites()
    {
        var (errors, warnings) = EndpointHandler.CheckConfig(EndpointType.SQL,
            """{"DatabaseObjectName":"Items","DatabaseObjectType":"Table","WriteMode":"Table","PrimaryKey":"Id","AllowedColumns":["Id","Name"],"AllowedMethods":["GET","POST"]}""");

        Assert.Empty(errors);
        Assert.DoesNotContain(warnings, w => w.Contains("WriteMode"));
    }
}
