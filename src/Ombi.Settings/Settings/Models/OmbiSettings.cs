using Ombi.I18n.Resources;
using System.Globalization;
namespace Ombi.Settings.Settings.Models
{
    public class OmbiSettings : Settings
    {
        private string defaultLanguageCode = "en";
        public string BaseUrl { get; set; }
        public bool CollectAnalyticData { get; set; }
        public bool Wizard { get; set; }
        public string ApiKey { get; set; }
        public bool DoNotSendNotificationsForAutoApprove { get; set; }
        public bool HideRequestsUsers { get; set; }
        public bool DisableHealthChecks { get; set; }
        public string DefaultLanguageCode
        {
            get => defaultLanguageCode;
            set {
                defaultLanguageCode = value;
                Texts.Culture = new CultureInfo(value);
            }
        }
        public bool AutoDeleteAvailableRequests { get; set; }
        public int AutoDeleteAfterDays { get; set; }
        public Branch Branch { get; set; }

        // When enabled, Ombi stops sending the "Item Added To Fault Queue" notification for every
        // failed request once a downstream DVR (Sonarr/Radarr/Lidarr) outage is detected, to avoid
        // spamming users when a burst of requests fails against an unhealthy instance.
        public bool SuppressFaultQueueNotificationsDuringOutage { get; set; }
        // Number of fault-queue additions within the detection window before an outage is assumed.
        public int OutageFailureThreshold { get; set; }
        // Rolling window (in minutes) used to count fault-queue additions for outage detection.
        public int OutageDetectionWindowMinutes { get; set; }

        //INTERNAL
        public bool HasMigratedOldTvDbData { get; set; }
        public bool Set { get; set; }
    }

    public enum Branch
    {
        Develop = 0,
        Stable = 1,
    }
}