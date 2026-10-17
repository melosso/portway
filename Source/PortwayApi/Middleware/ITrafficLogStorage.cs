namespace PortwayApi.Middleware;

using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Interface for log storage providers
/// </summary>
public interface ITrafficLogStorage
{
    Task InitializeAsync();
    Task SaveLogsAsync(IEnumerable<ProxyTrafficLogEntry> logs);
}
