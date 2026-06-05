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
        private readonly IMemoryCache _cache;

        public FaultQueueResilienceService(ISettingsService<OmbiSettings> ombiSettings, IMemoryCache cache)
        {
            _ombiSettings = ombiSettings;
            _cache = cache;
        }

        internal const int DefaultFailureThreshold = 3;
        internal const int DefaultWindowMinutes = 10;
        private const string CacheKey = "FaultQueueResilience_WindowState";

        /// <summary>
        /// Overridable clock seam so the rolling-window logic can be tested deterministically.
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

            var threshold = settings.OutageFailureThreshold > 0
                ? settings.OutageFailureThreshold
                : DefaultFailureThreshold;
            var window = TimeSpan.FromMinutes(settings.OutageDetectionWindowMinutes > 0
                ? settings.OutageDetectionWindowMinutes
                : DefaultWindowMinutes);

            var state = _cache.GetOrCreate(CacheKey, entry =>
            {
                entry.Priority = CacheItemPriority.NeverRemove;
                return new ConcurrentDictionary<RequestType, FaultWindow>();
            });

            var windowState = state.GetOrAdd(requestType, _ => new FaultWindow());
            var now = UtcNow;

            lock (windowState)
            {
                // Reset the counter when the previous failure is older than the detection window,
                // so a healthy period naturally clears the incident without needing success hooks.
                if (now - windowState.LastFailureUtc > window)
                {
                    windowState.Count = 0;
                }

                windowState.Count++;
                windowState.LastFailureUtc = now;

                // Notify while we are below the threshold (healthy/degraded). Once the threshold is
                // reached we treat it as an active outage and suppress the per-item notifications.
                return windowState.Count < threshold;
            }
        }

        private sealed class FaultWindow
        {
            public int Count { get; set; }
            public DateTime LastFailureUtc { get; set; }
        }
    }
}
