using UnityEditor;
using UnityEngine;
using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Test menu for VibeLogger stacktrace functionality
    /// </summary>
    public static class VibeLoggerTest
    {
        [MenuItem("UnityCliLoop/Debug/VibeLogger Tests/Test All Log Levels")]
        public static void TestAllLogLevels()
        {
            string correlationId = VibeLogger.GenerateCorrelationId();
            
            // Info - stacktrace should be false by default
            VibeLogger.LogInfo(
                "test_info_log",
                "This is a test Info log",
                new { test_data = "info_test" },
                correlationId,
                "Testing Info log level",
                "Verify stacktrace is NOT included for Info"
            );
            
            // Debug - stacktrace should be false by default
            VibeLogger.LogDebug(
                "test_debug_log",
                "This is a test Debug log",
                new { test_data = "debug_test" },
                correlationId,
                "Testing Debug log level",
                "Verify stacktrace is NOT included for Debug"
            );
            
            // Warning - stacktrace should be true by default
            VibeLogger.LogWarning(
                "test_warning_log",
                "This is a test Warning log",
                new { test_data = "warning_test" },
                correlationId,
                "Testing Warning log level",
                "Verify stacktrace IS included for Warning"
            );
            
            // Error - stacktrace should be true by default
            VibeLogger.LogError(
                "test_error_log",
                "This is a test Error log",
                new { test_data = "error_test" },
                correlationId,
                "Testing Error log level",
                "Verify stacktrace IS included for Error"
            );
            
            // Exception - stacktrace should be true by default
            try
            {
                throw new InvalidOperationException("Test exception for VibeLogger");
            }
            catch (Exception ex)
            {
                VibeLogger.LogException(
                    "test_exception_log",
                    ex,
                    new { test_data = "exception_test" },
                    correlationId,
                    "Testing Exception log level",
                    "Verify stacktrace IS included for Exception"
                );
            }
            
            Debug.Log($"[VibeLoggerTest] All log levels tested with correlation ID: {correlationId}");
            Debug.Log("[VibeLoggerTest] Check .uloop/outputs/VibeLogs/ folder for output files");
        }

        [MenuItem("UnityCliLoop/Debug/VibeLogger Tests/Test Explicit StackTrace Control")]
        public static void TestExplicitStackTraceControl()
        {
            string correlationId = VibeLogger.GenerateCorrelationId();
            
            // Info with stacktrace explicitly enabled
            VibeLogger.LogInfo(
                "test_info_with_stacktrace",
                "Info log with explicit stacktrace enabled",
                new { test_data = "info_explicit_true" },
                correlationId,
                "Testing Info with explicit stacktrace=true",
                "Verify stacktrace IS included despite Info default",
                includeStackTrace: true
            );
            
            // Error with stacktrace explicitly disabled
            VibeLogger.LogError(
                "test_error_without_stacktrace",
                "Error log with explicit stacktrace disabled",
                new { test_data = "error_explicit_false" },
                correlationId,
                "Testing Error with explicit stacktrace=false",
                "Verify stacktrace is NOT included despite Error default",
                includeStackTrace: false
            );
            
            Debug.Log($"[VibeLoggerTest] Explicit control tested with correlation ID: {correlationId}");
            Debug.Log("[VibeLoggerTest] Check .uloop/outputs/VibeLogs/ folder for output files");
        }
    }
}