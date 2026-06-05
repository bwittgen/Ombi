using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ombi.Api.External.ExternalApis.Lidarr;
using Ombi.Api.External.ExternalApis.Radarr;
using Ombi.Api.External.ExternalApis.Sonarr;
using Ombi.Core.Settings;
using Ombi.Settings.Settings.Models.External;
using Ombi.Store.Entities;

namespace Ombi.Core.Services
{
    /// <inheritdoc />
    public class ArrHealthProbe : IArrHealthProbe
    {
        private readonly ISettingsService<SonarrSettings> _sonarrSettings;
        private readonly ISettingsService<RadarrSettings> _radarrSettings;
        private readonly ISettingsService<LidarrSettings> _lidarrSettings;
        private readonly ISonarrApi _sonarrApi;
        private readonly IRadarrV3Api _radarrApi;
        private readonly ILidarrApi _lidarrApi;
        private readonly ILogger<ArrHealthProbe> _logger;

        public ArrHealthProbe(
            ISettingsService<SonarrSettings> sonarrSettings,
            ISettingsService<RadarrSettings> radarrSettings,
            ISettingsService<LidarrSettings> lidarrSettings,
            ISonarrApi sonarrApi,
            IRadarrV3Api radarrApi,
            ILidarrApi lidarrApi,
            ILogger<ArrHealthProbe> logger)
        {
            _sonarrSettings = sonarrSettings;
            _radarrSettings = radarrSettings;
            _lidarrSettings = lidarrSettings;
            _sonarrApi = sonarrApi;
            _radarrApi = radarrApi;
            _lidarrApi = lidarrApi;
            _logger = logger;
        }

        public async Task<ArrHealthStatus> ProbeAsync(RequestType requestType)
        {
            switch (requestType)
            {
                case RequestType.TvShow:
                    return await ProbeSonarrAsync();
                case RequestType.Movie:
                    return await ProbeRadarrAsync();
                case RequestType.Album:
                    return await ProbeLidarrAsync();
                default:
                    return ArrHealthStatus.NotConfigured;
            }
        }

        private async Task<ArrHealthStatus> ProbeSonarrAsync()
        {
            var settings = await _sonarrSettings.GetSettingsAsync();
            if (settings == null || !settings.Enabled)
            {
                return ArrHealthStatus.NotConfigured;
            }

            try
            {
                var status = await _sonarrApi.SystemStatus(settings.ApiKey, settings.FullUri);
                return status != null ? ArrHealthStatus.Healthy : ArrHealthStatus.Unhealthy;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Sonarr health probe failed while assessing fault-queue outage state");
                return ArrHealthStatus.Unhealthy;
            }
        }

        private async Task<ArrHealthStatus> ProbeRadarrAsync()
        {
            var settings = await _radarrSettings.GetSettingsAsync();
            if (settings == null || !settings.Enabled)
            {
                return ArrHealthStatus.NotConfigured;
            }

            try
            {
                var status = await _radarrApi.SystemStatus(settings.ApiKey, settings.FullUri);
                return status != null ? ArrHealthStatus.Healthy : ArrHealthStatus.Unhealthy;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Radarr health probe failed while assessing fault-queue outage state");
                return ArrHealthStatus.Unhealthy;
            }
        }

        private async Task<ArrHealthStatus> ProbeLidarrAsync()
        {
            var settings = await _lidarrSettings.GetSettingsAsync();
            if (settings == null || !settings.Enabled)
            {
                return ArrHealthStatus.NotConfigured;
            }

            try
            {
                var status = await _lidarrApi.Status(settings.ApiKey, settings.FullUri);
                return status != null ? ArrHealthStatus.Healthy : ArrHealthStatus.Unhealthy;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Lidarr health probe failed while assessing fault-queue outage state");
                return ArrHealthStatus.Unhealthy;
            }
        }
    }
}
