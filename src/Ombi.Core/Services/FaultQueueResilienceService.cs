using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Ombi.Core.Settings;
using Ombi.Settings.Settings.Models;
using Ombi.Store.Entities;

namespace Ombi.Core.Services
{
    /// <inheritdoc />
    public class FaultQueueResilienceService : IFaultQueueResilienceService
    {
        private readonly ISettingsService<OmbiSettings> _ombiSettings;
        private readonly IArrHealthProbe _healthProbe;
        private readonly IMemoryCache _cache;

        public FaultQueueResilienceService(
            ISettingsService<OmbiSettings> ombiSettings,
            IArrHealthProbe healthProbe,
            IMemoryCache cache)
        {
            _ombiSettings = ombiSettings;
            _healthProbe = healthProbe;
            _cache = cache;
        }

        internal const int DefaultFailureThreshold = 2;
        internal const int DefaultWindowMinutes = 30;
        private const string CacheKey = "FaultQueueResilience_IncidentState";

        /// <summary>
        /// Overridable clock seam so the staleness logic can be tested deterministically.
        /// </summary>
        protected virtual DateTime UtcNow => DateTime.UtcNow;

        public async Task<bool> ShouldNotifyFaultQueueAsync(RequestType requestType)
        {
            var settings = await _ombiSettings.GetSettingsAsync();

            // Opt-in behaviour: when the feature is disabled we preserve the legacy behaviour of
            // notifying for every item added to the fault queue.
            if (settings == null || !settings.SuppressFaultQueueNotificationsDuringOutage)
            {
                return true;
            }

            // Confirm whether the failure is actually an outage by probing the downstream Arr
            // service. A healthy probe means the send failure is item-specific (e.g. bad metadata),
            // so we still notify; only a confirmed-unhealthy service triggers suppression.
            var health = await _healthProbe.ProbeAsync(requestType);

            var threshold = settings.OutageFailureThreshold > 0
                ? settings.OutageFailureThreshold
                : DefaultFailureThreshold;
            var staleAfter = TimeSpan.FromMinutes(settings.OutageDetectionWindowMinutes > 0
                ? settings.OutageDetectionWindowMinutes
                : DefaultWindowMinutes);

            var state = _cache.GetOrCreate(CacheKey, entry =>
            {
                entry.Priority = CacheItemPriority.NeverRemove;
                return new ConcurrentDictionary<RequestType, IncidentState>();
            });

            var incident = state.GetOrAdd(requestType, _ => new IncidentState());
            var now = UtcNow;

            lock (incident)
            {
                // Drop stale incident state so a fresh burst of failures is evaluated from scratch.
                if (now - incident.LastProbeUtc > staleAfter)
                {
                    incident.ConsecutiveUnhealthy = 0;
                    incident.IncidentActive = false;
                }

                incident.LastProbeUtc = now;

                if (health != ArrHealthStatus.Unhealthy)
                {
                    // Service is healthy (or not configured / cannot be assessed). Clear any active
                    // incident — a subsequent healthy probe is our recovery signal — and notify,
                    // because a failure while the service is up is a genuine per-item problem.
                    incident.ConsecutiveUnhealthy = 0;
                    incident.IncidentActive = false;
                    return true;
                }

                // Confirmed unhealthy.
                if (incident.IncidentActive)
                {
                    // Already inside a known outage: suppress the per-item spam.
                    return false;
                }

                incident.ConsecutiveUnhealthy++;

                if (incident.ConsecutiveUnhealthy >= threshold)
                {
                    // Transition into an active incident. Allow this single notification through so
                    // the outage is still surfaced once, then suppress everything that follows.
                    incident.IncidentActive = true;
                }

                return true;
            }
        }

        private sealed class IncidentState
        {
            public int ConsecutiveUnhealthy { get; set; }
            public bool IncidentActive { get; set; }
            public DateTime LastProbeUtc { get; set; }
        }
    }
}
