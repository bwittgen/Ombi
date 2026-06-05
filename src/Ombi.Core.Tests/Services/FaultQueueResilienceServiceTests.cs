using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using NUnit.Framework;
using Ombi.Core.Services;
using Ombi.Core.Settings;
using Ombi.Settings.Settings.Models;
using Ombi.Store.Entities;

namespace Ombi.Core.Tests.Services
{
    [TestFixture]
    public class FaultQueueResilienceServiceTests
    {
        private Mock<ISettingsService<OmbiSettings>> _settingsMock;
        private Mock<IArrHealthProbe> _probeMock;
        private MemoryCache _cache;
        private OmbiSettings _settings;
        private TestableFaultQueueResilienceService _subject;

        [SetUp]
        public void Setup()
        {
            _settings = new OmbiSettings
            {
                SuppressFaultQueueNotificationsDuringOutage = true,
                OutageFailureThreshold = 3,
                OutageDetectionWindowMinutes = 10
            };
            _settingsMock = new Mock<ISettingsService<OmbiSettings>>();
            _settingsMock.Setup(x => x.GetSettingsAsync()).ReturnsAsync(() => _settings);

            _probeMock = new Mock<IArrHealthProbe>();
            // Default to a confirmed outage so the suppression path is exercised unless a test
            // overrides the probe result.
            _probeMock.Setup(x => x.ProbeAsync(It.IsAny<RequestType>())).ReturnsAsync(ArrHealthStatus.Unhealthy);

            _cache = new MemoryCache(new MemoryCacheOptions());
            _subject = new TestableFaultQueueResilienceService(_settingsMock.Object, _probeMock.Object, _cache)
            {
                Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        [TearDown]
        public void TearDown() => _cache.Dispose();

        [Test]
        public async Task ConfirmedOutage_NotifiesUntilThresholdThenSuppresses()
        {
            // Ramp-up failures below the threshold still notify.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "1st failure");
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "2nd failure");

            // The 3rd failure reaches the threshold => incident starts. We allow this single
            // notification through so the outage is surfaced once.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "incident-start");

            // Everything after the incident is active is suppressed.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "4th failure");
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "5th failure");
        }

        [Test]
        public async Task HealthyProbe_AlwaysNotifies()
        {
            // Service is up: the send failure is item-specific, so we must keep notifying.
            _probeMock.Setup(x => x.ProbeAsync(It.IsAny<RequestType>())).ReturnsAsync(ArrHealthStatus.Healthy);

            for (var i = 0; i < 10; i++)
            {
                Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True);
            }
        }

        [Test]
        public async Task NotConfiguredProbe_AlwaysNotifies()
        {
            // We cannot assess health, so preserve the legacy notify-every-time behaviour.
            _probeMock.Setup(x => x.ProbeAsync(It.IsAny<RequestType>())).ReturnsAsync(ArrHealthStatus.NotConfigured);

            for (var i = 0; i < 10; i++)
            {
                Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True);
            }
        }

        [Test]
        public async Task SuppressionDisabled_AlwaysNotifies()
        {
            _settings.SuppressFaultQueueNotificationsDuringOutage = false;

            for (var i = 0; i < 10; i++)
            {
                Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True);
            }

            // The probe must not even be consulted when the feature is off.
            _probeMock.Verify(x => x.ProbeAsync(It.IsAny<RequestType>()), Times.Never);
        }

        [Test]
        public async Task RequestTypesTrackedIndependently()
        {
            // Drive movies into an active incident (threshold = 3).
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False);

            // TV is a separate downstream service and tracks its own incident state.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.TvShow), Is.True);
        }

        [Test]
        public async Task RecoveryProbe_ClearsIncident()
        {
            // Establish an active movie incident.
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "incident active");

            // A healthy probe is our recovery signal: it clears the incident and notifies.
            _probeMock.Setup(x => x.ProbeAsync(It.IsAny<RequestType>())).ReturnsAsync(ArrHealthStatus.Healthy);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "recovery");

            // A fresh outage must ramp up from scratch rather than resuming suppression.
            _probeMock.Setup(x => x.ProbeAsync(It.IsAny<RequestType>())).ReturnsAsync(ArrHealthStatus.Unhealthy);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "new outage ramp");
        }

        [Test]
        public async Task StaleState_ResetsIncident()
        {
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "incident active");

            // Advance beyond the detection window; the incident state should be dropped and notify again.
            _subject.Now = _subject.Now.AddMinutes(11);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "after stale reset");
        }

        [Test]
        public async Task InvalidThresholdAndWindow_FallBackToDefaults()
        {
            _settings.OutageFailureThreshold = 0;
            _settings.OutageDetectionWindowMinutes = 0;

            // Default threshold is 2: first failure ramps, second is the incident-start, third suppresses.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Album), Is.True);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Album), Is.True);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Album), Is.False);
        }

        private sealed class TestableFaultQueueResilienceService : FaultQueueResilienceService
        {
            public TestableFaultQueueResilienceService(
                ISettingsService<OmbiSettings> settings, IArrHealthProbe healthProbe, IMemoryCache cache)
                : base(settings, healthProbe, cache)
            {
            }

            public DateTime Now { get; set; }

            protected override DateTime UtcNow => Now;
        }
    }
}
