using EasyReasy.Database.Logging.Reading;

namespace EasyReasy.Database.Logging.Tests
{
    /// <summary>
    /// Covers <see cref="OperationalLogReadRepository.NormalizeToUtc"/>, the boundary helper that
    /// forces filter timestamps to an unambiguous UTC instant so they bind as <c>timestamptz</c>
    /// rather than skewing against the session time zone. Exercises each <see cref="DateTimeKind"/>
    /// branch — the reason the helper exists.
    /// </summary>
    public class NormalizeToUtcTests
    {
        [Fact]
        public void NormalizeToUtc_WithNull_ReturnsNull()
        {
            Assert.Null(OperationalLogReadRepository.NormalizeToUtc(null));
        }

        [Fact]
        public void NormalizeToUtc_WithUtcKind_IsUnchanged()
        {
            DateTime utc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            DateTime? result = OperationalLogReadRepository.NormalizeToUtc(utc);

            Assert.Equal(utc, result);
            Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        }

        [Fact]
        public void NormalizeToUtc_WithUnspecifiedKind_IsTreatedAsUtcWithoutShiftingTheClock()
        {
            DateTime unspecified = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

            DateTime? result = OperationalLogReadRepository.NormalizeToUtc(unspecified);

            Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
            // Treated as UTC: the wall-clock reading is preserved, not converted.
            Assert.Equal(unspecified.Ticks, result.Value.Ticks);
        }

        [Fact]
        public void NormalizeToUtc_WithLocalKind_IsConvertedToUtc()
        {
            DateTime local = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local);

            DateTime? result = OperationalLogReadRepository.NormalizeToUtc(local);

            Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
            // Local is converted (offset applied), so it equals the framework's own conversion.
            Assert.Equal(local.ToUniversalTime(), result.Value);
        }
    }
}
