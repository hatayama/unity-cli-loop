using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Start-parameter bounds and platform-specific output path rules.
    /// </summary>
    public sealed class RecordVideoParameterValidatorTests
    {
        private static ValidationResult Validate(
            int frameRate,
            int maxDurationSeconds,
            string outputPath,
            bool isLinux)
        {
            return RecordVideoParameterValidator.Validate(
                frameRate,
                maxDurationSeconds,
                outputPath,
                isLinux,
                1.0f);
        }

        /// <summary>
        /// What: frame rate 0 is rejected as below the 1–60 range.
        /// </summary>
        [Test]
        public void Validate_WhenFrameRateIs0_IsInvalid()
        {
            ValidationResult result = Validate(0, 60, "", false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("FrameRate"));
        }

        /// <summary>
        /// What: frame rate 1 is the inclusive lower bound.
        /// </summary>
        [Test]
        public void Validate_WhenFrameRateIs1_IsValid()
        {
            ValidationResult result = Validate(1, 60, "", false);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: frame rate 60 is the inclusive upper bound.
        /// </summary>
        [Test]
        public void Validate_WhenFrameRateIs60_IsValid()
        {
            ValidationResult result = Validate(60, 60, "", false);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: frame rate 61 is rejected as above the 1–60 range.
        /// </summary>
        [Test]
        public void Validate_WhenFrameRateIs61_IsInvalid()
        {
            ValidationResult result = Validate(61, 60, "", false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("FrameRate"));
        }

        /// <summary>
        /// What: max duration 0 is rejected as below the 1–600 range.
        /// </summary>
        [Test]
        public void Validate_WhenMaxDurationIs0_IsInvalid()
        {
            ValidationResult result = Validate(30, 0, "", false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("MaxDurationSeconds"));
        }

        /// <summary>
        /// What: max duration 1 is the inclusive lower bound.
        /// </summary>
        [Test]
        public void Validate_WhenMaxDurationIs1_IsValid()
        {
            ValidationResult result = Validate(30, 1, "", false);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: max duration 600 is the inclusive upper bound.
        /// </summary>
        [Test]
        public void Validate_WhenMaxDurationIs600_IsValid()
        {
            ValidationResult result = Validate(30, 600, "", false);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: max duration 601 is rejected as above the 1–600 range.
        /// </summary>
        [Test]
        public void Validate_WhenMaxDurationIs601_IsInvalid()
        {
            ValidationResult result = Validate(30, 601, "", false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("MaxDurationSeconds"));
        }

        /// <summary>
        /// What: an empty output path is accepted so the default resolver path can be used.
        /// </summary>
        [Test]
        public void Validate_WhenOutputPathIsEmpty_IsValid()
        {
            ValidationResult result = Validate(30, 60, "", false);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: a .mov path is rejected because only mp4 and webm are encoded.
        /// </summary>
        [Test]
        public void Validate_WhenOutputPathIsMov_IsInvalid()
        {
            ValidationResult result = Validate(30, 60, "clip.mov", false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain(".mp4").And.Contain(".webm"));
        }

        /// <summary>
        /// What: Linux rejects .mp4 because H.264 is unavailable there.
        /// </summary>
        [Test]
        public void Validate_WhenLinuxAndMp4_IsInvalid()
        {
            ValidationResult result = Validate(30, 60, "clip.mp4", true);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.ErrorMessage,
                Does.Contain("H.264 is not available on Linux; use a .webm output path."));
        }

        /// <summary>
        /// What: Linux accepts .webm so VP8 output can be requested explicitly.
        /// </summary>
        [Test]
        public void Validate_WhenLinuxAndWebm_IsValid()
        {
            ValidationResult result = Validate(30, 60, "clip.webm", true);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: a non-Linux host accepts .webm so VP8 can be requested by extension.
        /// </summary>
        [Test]
        public void Validate_WhenNonLinuxAndWebm_IsValid()
        {
            ValidationResult result = Validate(30, 60, "clip.webm", false);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: resolution scale 0.1 is the inclusive lower bound.
        /// </summary>
        [Test]
        public void Validate_WhenResolutionScaleIs0_1_IsValid()
        {
            ValidationResult result = RecordVideoParameterValidator.Validate(30, 60, "", false, 0.1f);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: resolution scale 1.0 is the inclusive upper bound.
        /// </summary>
        [Test]
        public void Validate_WhenResolutionScaleIs1_IsValid()
        {
            ValidationResult result = RecordVideoParameterValidator.Validate(30, 60, "", false, 1.0f);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// What: resolution scale 0.09 is rejected as below the 0.1–1.0 range.
        /// </summary>
        [Test]
        public void Validate_WhenResolutionScaleIs0_09_IsInvalid()
        {
            ValidationResult result = RecordVideoParameterValidator.Validate(30, 60, "", false, 0.09f);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("ResolutionScale must be between 0.1 and 1.0, got: 0.09"));
        }

        /// <summary>
        /// What: resolution scale 1.01 is rejected as above the 0.1–1.0 range.
        /// </summary>
        [Test]
        public void Validate_WhenResolutionScaleIs1_01_IsInvalid()
        {
            ValidationResult result = RecordVideoParameterValidator.Validate(30, 60, "", false, 1.01f);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("ResolutionScale must be between 0.1 and 1.0, got: 1.01"));
        }
    }
}
