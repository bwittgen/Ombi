using System.Threading.Tasks;
using Ombi.Store.Entities;

namespace Ombi.Core.Services
{
    /// <summary>
    /// Detects downstream DVR (Sonarr/Radarr/Lidarr) outages by tracking how many requests
    /// are added to the fault queue within a rolling time window. This lets Ombi suppress the
    /// repetitive "Item Added To Fault Queue" notification during an incident instead of
    /// notifying once per failed item (which spams users when a Plex watchlist import fans out
    /// many requests against an unhealthy Arr instance).
    /// </summary>
    public interface IFaultQueueResilienceService
    {
        /// <summary>
        /// Records a fault-queue addition for the given request type and returns whether the
        /// user-facing fault-queue notification should be sent. Returns <c>false</c> once the
        /// number of failures within the detection window reaches the configured outage
        /// threshold (i.e. we consider the downstream service to be in an active incident).
        /// </summary>
        Task<bool> ShouldNotifyFaultQueueAsync(RequestType requestType);
    }
}
