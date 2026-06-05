using System.Threading.Tasks;
using Ombi.Store.Entities;

namespace Ombi.Core.Services
{
    /// <summary>
    /// Performs a live health probe against the downstream Arr service (Sonarr/Radarr/Lidarr)
    /// that backs a given <see cref="RequestType"/>, so that callers can distinguish a real
    /// service outage from an item-specific send failure.
    /// </summary>
    public interface IArrHealthProbe
    {
        Task<ArrHealthStatus> ProbeAsync(RequestType requestType);
    }

    public enum ArrHealthStatus
    {
        /// <summary>The relevant Arr service is disabled/not configured, so health cannot be assessed.</summary>
        NotConfigured,

        /// <summary>The Arr service responded to a status probe.</summary>
        Healthy,

        /// <summary>The Arr service could not be reached or returned no status.</summary>
        Unhealthy
    }
}
