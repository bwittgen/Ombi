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
            _cache = new MemoryCache(new MemoryCacheOptions());
            _subject = new TestableFaultQueueResilienceService(_settingsMock.Object, _cache)
            {
                Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        [TearDown]
        public void TearDown() => _cache.Dispose();

        [Test]
        public async Task NotifiesUntilThresholdReached()
        {
            // Failures below the threshold should still notify (healthy/degraded).
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "1st failure");
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "2nd failure");

            // The 3rd failure reaches the threshold => incident active => suppress.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "3rd failure");
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "4th failure");
        }

        [Test]
        public async Task SuppressionDisabled_AlwaysNotifies()
        {
            _settings.SuppressFaultQueueNotificationsDuringOutage = false;

            for (var i = 0; i < 10; i++)
            {
                Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True);
            }
        }

        [Test]
        public async Task RequestTypesTrackedIndependently()
        {
            // Drive movies into an active incident.
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False);

            // TV is a separate downstream service and should still notify.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.TvShow), Is.True);
        }

        [Test]
        public async Task WindowExpiry_ResetsCounter()
        {
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.False, "incident active");

            // Advance beyond the detection window; the counter should reset and notify again.
            _subject.Now = _subject.Now.AddMinutes(11);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Movie), Is.True, "after window reset");
        }

        [Test]
        public async Task InvalidThresholdAndWindow_FallBackToDefaults()
        {
            _settings.OutageFailureThreshold = 0;
            _settings.OutageDetectionWindowMinutes = 0;

            // Default threshold is 3.
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Album), Is.True);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Album), Is.True);
            Assert.That(await _subject.ShouldNotifyFaultQueueAsync(RequestType.Album), Is.False);
        }

        private sealed class TestableFaultQueueResilienceService : FaultQueueResilienceService
        {
            public TestableFaultQueueResilienceService(ISettingsService<OmbiSettings> settings, IMemoryCache cache)
                : base(settings, cache)
            {
            }

            public DateTime Now { get; set; }

            protected override DateTime UtcNow => Now;
        }
    }
}
