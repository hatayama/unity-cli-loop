using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies external watch tool input validation and empty-state responses.
    /// </summary>
    [TestFixture]
    public sealed class WatchToolTests
    {
        [SetUp]
        public void SetUp()
        {
            WatchExpressionServices.Registry.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            WatchExpressionServices.Registry.ClearAll();
        }

        /// <summary>
        /// Verifies an empty watch identifier is rejected without compiling user code.
        /// </summary>
        [Test]
        public void EnableAsync_WhenIdIsEmpty_ReturnsValidationFailure()
        {
            Task<WatchResponse> task = WatchUseCase.EnableAsync(
                new EnableWatchSchema { Expression = "1 + 2" },
                CancellationToken.None);

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Message, Does.Contain("Id must not be null or empty"));
        }

        /// <summary>
        /// Verifies a non-positive history limit is rejected as external input validation.
        /// </summary>
        [Test]
        public void EnableAsync_WhenMaxHistoryIsZero_ReturnsValidationFailure()
        {
            Task<WatchResponse> task = WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = "1 + 2", MaxHistory = 0 },
                CancellationToken.None);

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Message, Does.Contain("between 1 and 100"));
        }

        /// <summary>
        /// Verifies get-watch-values returns a successful empty collection before registration.
        /// </summary>
        [Test]
        public void GetValues_WhenNoWatchesAreRegistered_ReturnsEmptySuccess()
        {
            WatchResponse response = WatchUseCase.GetValues(new GetWatchValuesSchema());

            Assert.That(response.Success, Is.True);
            Assert.That(response.Watches, Is.Empty);
            Assert.That(response.Message, Does.Contain("No watch expressions"));
        }
    }
}
