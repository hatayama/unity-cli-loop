using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the success and failure factories of the public service result contract.
    /// </summary>
    public sealed class ServiceResultTests
    {
        /// <summary>
        /// Verifies a success result carries its data and no error message.
        /// </summary>
        [Test]
        public void SuccessResult_WhenCreated_CarriesDataAndNoError()
        {
            ServiceResult<string> result = ServiceResult<string>.SuccessResult("payload");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data, Is.EqualTo("payload"));
            Assert.That(result.ErrorMessage, Is.Null);
        }

        /// <summary>
        /// Verifies a failure result carries its error message and default data.
        /// </summary>
        [Test]
        public void FailureResult_WhenCreated_CarriesErrorAndDefaultData()
        {
            ServiceResult<int> result = ServiceResult<int>.FailureResult("not found");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Data, Is.EqualTo(0));
            Assert.That(result.ErrorMessage, Is.EqualTo("not found"));
        }
    }
}
