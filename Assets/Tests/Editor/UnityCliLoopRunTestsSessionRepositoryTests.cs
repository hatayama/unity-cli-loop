using System;
using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests SessionState persistence for pending run-tests recovery across Domain Reload.
    /// </summary>
    [TestFixture]
    public sealed class UnityCliLoopRunTestsSessionRepositoryTests
    {
        private UnityCliLoopRunTestsSessionRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _repository = new UnityCliLoopRunTestsSessionRepository();
            _repository.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _repository.ClearAll();
        }

        /// <summary>
        /// Verifies a stored pending run is visible through HasPendingRun and the pending id list.
        /// </summary>
        [Test]
        public void StorePendingRun_WhenSaved_IsVisibleInPendingQueries()
        {
            DateTime expiresAtUtc = DateTime.UtcNow.AddMinutes(10);

            _repository.StorePendingRun("run_tests_pending_one", expiresAtUtc);

            IReadOnlyList<string> pendingIds = _repository.GetPendingRunRequestIds();
            Assert.That(_repository.HasPendingRun("run_tests_pending_one"), Is.True);
            Assert.That(_repository.HasAnyPendingRun(), Is.True);
            Assert.That(pendingIds, Does.Contain("run_tests_pending_one"));
        }

        /// <summary>
        /// Verifies storing a result clears that pending run and returns HasResult with the JSON.
        /// </summary>
        [Test]
        public void StoreRunResult_WhenPendingExists_ClearsPendingAndExposesResult()
        {
            _repository.StorePendingRun("run_tests_result_one", DateTime.UtcNow.AddMinutes(10));

            _repository.StoreRunResult(
                "run_tests_result_one",
                "{\"Success\":true}",
                DateTime.UtcNow);

            UnityCliLoopStoredRunTestsResult storedResult = _repository.GetRunResult("run_tests_result_one");
            Assert.That(_repository.HasPendingRun("run_tests_result_one"), Is.False);
            Assert.That(storedResult.HasResult, Is.True);
            Assert.That(storedResult.ResultJson, Is.EqualTo("{\"Success\":true}"));
        }

        /// <summary>
        /// Verifies registering a pending run under a reused request id drops the previous stored result.
        /// </summary>
        [Test]
        public void StorePendingRun_WhenResultExistsForSameId_ClearsStaleResult()
        {
            _repository.StoreRunResult(
                "run_tests_reused",
                "{\"Success\":true}",
                DateTime.UtcNow);

            _repository.StorePendingRun("run_tests_reused", DateTime.UtcNow.AddMinutes(10));

            UnityCliLoopStoredRunTestsResult storedResult = _repository.GetRunResult("run_tests_reused");
            Assert.That(storedResult.HasResult, Is.False);
            Assert.That(_repository.HasPendingRun("run_tests_reused"), Is.True);
        }

        /// <summary>
        /// Verifies ClearExpired drops an expired pending run and a result older than the result lifetime.
        /// </summary>
        [Test]
        public void ClearExpired_WhenPendingAndResultArePastLifetime_RemovesBoth()
        {
            DateTime utcNow = DateTime.UtcNow;
            _repository.StorePendingRun("run_tests_expired_pending", utcNow.AddMinutes(-1));
            _repository.StoreRunResult(
                "run_tests_expired_result",
                "{\"Success\":false}",
                utcNow - UnityCliLoopRunTestsSessionRepository.RunTestsResultLifetime - TimeSpan.FromMinutes(1));

            _repository.ClearExpired(utcNow);

            Assert.That(_repository.HasPendingRun("run_tests_expired_pending"), Is.False);
            Assert.That(_repository.GetRunResult("run_tests_expired_result").HasResult, Is.False);
        }

        /// <summary>
        /// Verifies an unknown request id yields a None result instead of leftover SessionState data.
        /// </summary>
        [Test]
        public void GetRunResult_WhenRequestIdIsUnknown_ReturnsNone()
        {
            UnityCliLoopStoredRunTestsResult storedResult = _repository.GetRunResult("run_tests_unknown");

            Assert.That(storedResult.HasResult, Is.False);
            Assert.That(storedResult.ResultJson, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies two stored pending runs are both enumerated.
        /// </summary>
        [Test]
        public void GetPendingRunRequestIds_WhenTwoPendingRunsExist_ReturnsBothIds()
        {
            DateTime expiresAtUtc = DateTime.UtcNow.AddMinutes(10);
            _repository.StorePendingRun("run_tests_pending_a", expiresAtUtc);
            _repository.StorePendingRun("run_tests_pending_b", expiresAtUtc);

            IReadOnlyList<string> pendingIds = _repository.GetPendingRunRequestIds();
            Assert.That(pendingIds, Does.Contain("run_tests_pending_a"));
            Assert.That(pendingIds, Does.Contain("run_tests_pending_b"));
            Assert.That(pendingIds.Count, Is.EqualTo(2));
        }
    }
}
