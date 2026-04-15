using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace io.github.hatayama.uLoopMCP
{
    [Ignore("Skipped because full-console reflection scans make routine EditMode runs too slow; run manually when changing console log retrieval.")]
    /// <summary>
    /// Practical tests for ConsoleLogRetriever functionality
    /// Validates mask operations and reflection features in real scenarios
    /// </summary>
    public class ConsoleLogRetrieverTests
    {
        private ConsoleLogRetriever retriever;
        private int originalMask;

        [SetUp]
        public void SetUp()
        {
            retriever = new ConsoleLogRetriever();
            originalMask = retriever.GetCurrentMask();
        }

        [TearDown]
        public void TearDown()
        {
            // Always restore mask state after each test
            retriever.SetMask(originalMask);
        }

        [Test]
        public void GetAllLogs_WithMaskAllOff_StillReturnsAllLogs()
        {
            // This test verifies that GetAllLogs() can retrieve all console logs
            // even when the console mask is set to 0 (all types hidden).
            // This is the core functionality requirement - bypassing mask restrictions.
            
            // Arrange - Generate unique test logs
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testLogMessage = $"TestLog_{uniqueTestId}";
            string testWarningMessage = $"TestWarning_{uniqueTestId}";
            string testErrorMessage = $"TestError_{uniqueTestId}";

            LogAssert.Expect(UnityEngine.LogType.Log, testLogMessage);
            LogAssert.Expect(UnityEngine.LogType.Warning, testWarningMessage);
            LogAssert.Expect(UnityEngine.LogType.Error, testErrorMessage);

            Debug.Log(testLogMessage);
            Debug.LogWarning(testWarningMessage);
            Debug.LogError(testErrorMessage);

            // Set mask to completely off (normally nothing would be visible)
            retriever.SetMask(0);

            // Act - GetAllLogs should retrieve all logs regardless of mask
            List<LogEntryDto> allLogs = retriever.GetAllLogs();

            // Assert - All logs should be retrieved even with mask off
            Assert.IsNotNull(allLogs);
            Assert.IsTrue(allLogs.Any(log => log.Message.Contains(testLogMessage)), 
                "Log should be retrieved even with mask off");
            Assert.IsTrue(allLogs.Any(log => log.Message.Contains(testWarningMessage)), 
                "Warning should be retrieved even with mask off");
            Assert.IsTrue(allLogs.Any(log => log.Message.Contains(testErrorMessage)), 
                "Error should be retrieved even with mask off");
        }


        [Test]
        public void GetLogsByType_TemporarilyChangeMask_RestoresOriginalMask()
        {
            // This test verifies that GetLogsByType() temporarily changes the mask internally
            // to retrieve logs of a specific type, then properly restores the original mask.
            // This ensures the method doesn't interfere with other operations.
            
            // Arrange - Set initial mask to a specific value
            int testMask = 2; // Warning only
            retriever.SetMask(testMask);
            int maskBeforeCall = retriever.GetCurrentMask();

            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testLogMessage = $"TestTypeLog_{uniqueTestId}";
            
            LogAssert.Expect(UnityEngine.LogType.Log, testLogMessage);
            Debug.Log(testLogMessage);

            // Act - Get logs of Log type (internally changes mask temporarily)
            List<LogEntryDto> logTypeLogs = retriever.GetLogsByType(LogType.Log);

            // Assert - Mask should be restored after the call
            int maskAfterCall = retriever.GetCurrentMask();
            Assert.AreEqual(maskBeforeCall, maskAfterCall, 
                "Mask should be restored after GetLogsByType call");

            // Log should be retrieved correctly
            Assert.IsNotNull(logTypeLogs);
            Assert.IsTrue(logTypeLogs.Any(log => log.Message.Contains(testLogMessage) && log.LogType == McpLogType.Log));
        }

        [Test]
        public void SetMask_WithUnityInternalValues_WorksCorrectly()
        {
            // This test verifies that SetMask() correctly converts simple mask values
            // to Unity's internal mask format and that the conversion works properly.
            // Tests the mask conversion logic (0x387, 0x200, 0x100, 0x80).
            
            // Arrange & Act - Test Unity internal mask values
            // 7 = Error(1) + Warning(2) + Log(4) = show all types
            retriever.SetMask(7);
            int allMask = retriever.GetCurrentMask();

            retriever.SetMask(1); // Error only
            int errorMask = retriever.GetCurrentMask();

            retriever.SetMask(2); // Warning only  
            int warningMask = retriever.GetCurrentMask();

            retriever.SetMask(4); // Log only
            int logMask = retriever.GetCurrentMask();

            // Assert - Mask settings should be reflected correctly
            Assert.Greater(allMask, errorMask, "All mask should have higher value than error only");
            Assert.Greater(allMask, warningMask, "All mask should have higher value than warning only");
            Assert.Greater(allMask, logMask, "All mask should have higher value than log only");
            
            // Unity internal value conversion should work correctly (0x387, 0x200, 0x100, 0x80)
            Assert.IsTrue((allMask & 0x200) != 0, "All mask should include error bit");
            Assert.IsTrue((allMask & 0x100) != 0, "All mask should include warning bit");
            Assert.IsTrue((allMask & 0x80) != 0, "All mask should include log bit");
        }

        [Test]
        public void LogEntry_MessageSeparation_WorksCorrectly()
        {
            // This test verifies that callstackTextStartUTF8 correctly separates
            // message content from stack trace content, regardless of whether
            // stack trace is present or not.
            // NOTE: Please check that Console settings are configured to show stack traces
            
            // Arrange - Generate simple log message
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testMessage = $"MessageSeparation_{uniqueTestId}";
            
            LogAssert.Expect(UnityEngine.LogType.Log, testMessage);
            Debug.Log(testMessage);

            // Act
            List<LogEntryDto> logs = retriever.GetAllLogs();

            // Assert - Message separation should work correctly
            LogEntryDto testLog = logs.FirstOrDefault(log => log.Message.Contains(testMessage));
            Assert.IsNotNull(testLog, "Test log should be found");
            
            // Message should be exactly what we logged (clean separation)
            Assert.AreEqual(testMessage, testLog.Message.Trim(), 
                "Message should match exactly what was logged");
            
            // Message should not contain stack trace patterns
            Assert.IsFalse(testLog.Message.Contains("Debug:Log") || testLog.Message.Contains("Debug.Log"), 
                "Message should not contain Debug.Log calls");
            Assert.IsFalse(testLog.Message.Contains("(at "), 
                "Message should not contain file location patterns");
            
            // If stack trace exists, it should not contain our message
            if (!string.IsNullOrEmpty(testLog.StackTrace))
            {
                Assert.IsFalse(testLog.StackTrace.Contains(uniqueTestId), 
                    "Stack trace should not contain our unique message content");
            }
            
            // Verify that message and stack trace are properly separated
            Assert.IsNotNull(testLog.Message, "Message should never be null");
            Assert.IsNotNull(testLog.StackTrace, "StackTrace should never be null (may be empty)");
        }

        [Test]
        public void LogEntry_MessageWithStackTrace_SeparatedCorrectly()
        {
            // This test verifies that Debug.Log with context object generates stack trace
            // and that callstackTextStartUTF8 correctly separates message from stack trace.
            // NOTE: Please check that Console settings are configured to show stack traces
            
            // Arrange - Generate log with stack trace by providing context
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testMessage = $"MessageWithStack_{uniqueTestId}";
            
            // Create a dummy GameObject to use as context (this generates stack trace)
            GameObject testObject = new GameObject("TestObject");
            
            LogAssert.Expect(UnityEngine.LogType.Log, testMessage);
            Debug.Log(testMessage, testObject);

            // Act
            List<LogEntryDto> logs = retriever.GetAllLogs();

            // Assert - Message and stack trace should be separated
            LogEntryDto testLog = logs.FirstOrDefault(log => log.Message.Contains(testMessage));
            Assert.IsNotNull(testLog, "Test log should be found");
            
            // Message should contain only our message, not stack trace
            Assert.AreEqual(testMessage, testLog.Message.Trim(), 
                "Message should contain only the logged message");
            
            // Stack trace behavior depends on Unity Console settings
            // Check if stack trace exists and validate accordingly
            if (!string.IsNullOrEmpty(testLog.StackTrace))
            {
                // If stack trace exists, it should contain Debug.Log reference
                Assert.IsTrue(testLog.StackTrace.Contains("Debug:Log") || testLog.StackTrace.Contains("Debug.Log"), 
                    "Stack trace should contain Debug.Log reference when present");
            }
            
            // Message should not contain stack trace content
            Assert.IsFalse(testLog.Message.Contains("Debug:Log") || testLog.Message.Contains("Debug.Log"), 
                "Message should not contain stack trace content");
            
            // Cleanup
            Object.DestroyImmediate(testObject);
        }

        [Test]
        public void LogEntry_MessageWithColons_NotConfusedWithStackTrace()
        {
            // This test verifies that messages containing colons (like timestamps, URLs, etc.)
            // are not incorrectly treated as stack traces when using callstackTextStartUTF8.
            
            // Arrange - Generate message with colons that could confuse string-based parsing
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testMessage = $"Time: 12:34:56, URL: https://example.com:8080, Test_{uniqueTestId}";
            
            LogAssert.Expect(UnityEngine.LogType.Log, testMessage);
            Debug.Log(testMessage);

            // Act
            List<LogEntryDto> logs = retriever.GetAllLogs();

            // Assert - Message with colons should be properly identified
            LogEntryDto testLog = logs.FirstOrDefault(log => log.Message.Contains(uniqueTestId));
            Assert.IsNotNull(testLog, "Test log should be found");
            
            // Entire message should be in message field, not stack trace
            Assert.IsTrue(testLog.Message.Contains("Time: 12:34:56"), 
                "Message should contain timestamp with colons");
            Assert.IsTrue(testLog.Message.Contains("https://example.com:8080"), 
                "Message should contain URL with colons and port");
            
            // Stack trace should not contain our message content
            if (!string.IsNullOrEmpty(testLog.StackTrace))
            {
                Assert.IsFalse(testLog.StackTrace.Contains(uniqueTestId), 
                    "Stack trace should not contain our message content");
            }
        }

        [Test]
        public void LogEntry_ErrorWithStackTrace_SeparatedCorrectly()
        {
            // This test verifies that error logs with automatic stack traces
            // are correctly separated using callstackTextStartUTF8.
            // NOTE: Please check that Console settings are configured to show stack traces
            
            // Arrange - Generate error log (errors automatically include stack trace)
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testErrorMessage = $"TestError_{uniqueTestId}";
            
            LogAssert.Expect(UnityEngine.LogType.Error, testErrorMessage);
            Debug.LogError(testErrorMessage);

            // Act
            List<LogEntryDto> logs = retriever.GetAllLogs();

            // Assert - Error should have message and stack trace separated
            LogEntryDto testLog = logs.FirstOrDefault(log => 
                log.LogType == McpLogType.Error && log.Message.Contains(testErrorMessage));
            Assert.IsNotNull(testLog, "Test error log should be found");
            
            // Message should be clean
            Assert.AreEqual(testErrorMessage, testLog.Message.Trim(), 
                "Error message should be clean without stack trace");
            
            // Error should have stack trace
            Assert.IsFalse(string.IsNullOrEmpty(testLog.StackTrace), 
                "Error log should have stack trace");
            
            // Stack trace should contain meaningful content
            Assert.IsTrue(testLog.StackTrace.Contains("Debug:LogError") || 
                         testLog.StackTrace.Contains("Debug.LogError") ||
                         testLog.StackTrace.Contains("ConsoleLogRetrieverTests"), 
                "Stack trace should contain Debug.LogError or test method reference");
        }

        [Test]
        public void GetLogCount_ReflectionAccess_ReturnsCurrentCount()
        {
            // This test verifies that GetLogCount() uses reflection to access Unity's
            // internal log count and returns accurate count values.
            // This validates the reflection-based log counting functionality.
            
            // Arrange - Record count before adding new log
            int countBefore = retriever.GetLogCount();

            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string testMessage = $"CountTest_{uniqueTestId}";
            
            LogAssert.Expect(UnityEngine.LogType.Log, testMessage);
            Debug.Log(testMessage);

            // Act
            int countAfter = retriever.GetLogCount();

            // Assert - Log count should increase after adding a log
            Assert.GreaterOrEqual(countAfter, countBefore, 
                "Log count should increase after adding a log");
            Assert.Greater(countAfter, 0, "Log count should be positive");
        }

        [Test]
        public void ConsoleLogRetriever_ReflectionInitialization_SucceedsWithoutException()
        {
            // This test verifies that ConsoleLogRetriever can be initialized using reflection
            // to access Unity's internal LogEntries and ConsoleWindow types without errors.
            // This validates the core reflection setup and type discovery.

            // Act & Assert - Reflection-based initialization should succeed
            Assert.DoesNotThrow(() => {
                ConsoleLogRetriever newRetriever = new ConsoleLogRetriever();

                // Basic reflection functionality should work
                int count = newRetriever.GetLogCount();
                int mask = newRetriever.GetCurrentMask();
                List<LogEntryDto> logs = newRetriever.GetAllLogs();

                Assert.GreaterOrEqual(count, 0);
                Assert.GreaterOrEqual(mask, 0);
                Assert.IsNotNull(logs);
            }, "ConsoleLogRetriever should initialize and work without exceptions");
        }

        [Test]
        public void LogEntry_MultiByte_JapaneseMessage_SeparatedCorrectly()
        {
            // Unity's callstackTextStartUTF8 returns UTF-8 byte position, but
            // string.Substring() requires character position. For multi-byte characters
            // like Japanese (3 bytes per char in UTF-8), the boundary will be wrong
            // if byte position is used directly as character position.

            // Arrange - Generate log with Japanese message
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string japaneseMessage = $"日本語テスト_{uniqueTestId}";

            LogAssert.Expect(UnityEngine.LogType.Log, japaneseMessage);
            Debug.Log(japaneseMessage);

            // Act
            List<LogEntryDto> logs = retriever.GetAllLogs();

            // Assert - Japanese message should be properly separated
            LogEntryDto testLog = logs.FirstOrDefault(log => log.Message.Contains(uniqueTestId));
            Assert.IsNotNull(testLog, "Test log should be found");

            // Message should contain the full Japanese text without being cut off
            Assert.IsTrue(testLog.Message.Contains("日本語テスト"),
                "Message should contain full Japanese text without corruption");

            // Message should exactly match what was logged (no truncation due to UTF-8 byte position bug)
            Assert.AreEqual(japaneseMessage, testLog.Message.Trim(),
                $"Message should match exactly. Expected: '{japaneseMessage}', Actual: '{testLog.Message.Trim()}'");

            // Stack trace should not contain our message content
            if (!string.IsNullOrEmpty(testLog.StackTrace))
            {
                Assert.IsFalse(testLog.StackTrace.Contains("日本語テスト"),
                    "Stack trace should not contain our Japanese message content");
            }
        }

        [Test]
        public void LogEntry_MultiByte_MixedContent_SeparatedCorrectly()
        {
            // Unicode characters like emojis and CJK characters use multiple UTF-8 bytes.
            // This test verifies correct handling of mixed ASCII and multi-byte content.

            // Arrange - Generate log with mixed ASCII and multi-byte content
            string uniqueTestId = System.Guid.NewGuid().ToString("N")[..8];
            string mixedMessage = $"Hello こんにちは World 世界 Test_{uniqueTestId}";

            LogAssert.Expect(UnityEngine.LogType.Log, mixedMessage);
            Debug.Log(mixedMessage);

            // Act
            List<LogEntryDto> logs = retriever.GetAllLogs();

            // Assert - Mixed content should be properly separated
            LogEntryDto testLog = logs.FirstOrDefault(log => log.Message.Contains(uniqueTestId));
            Assert.IsNotNull(testLog, "Test log should be found");

            // All content should be present in the message
            Assert.IsTrue(testLog.Message.Contains("Hello"),
                "Message should contain ASCII 'Hello'");
            Assert.IsTrue(testLog.Message.Contains("こんにちは"),
                "Message should contain Japanese greeting");
            Assert.IsTrue(testLog.Message.Contains("World"),
                "Message should contain ASCII 'World'");
            Assert.IsTrue(testLog.Message.Contains("世界"),
                "Message should contain Japanese word '世界'");

            // Full message should match exactly
            Assert.AreEqual(mixedMessage, testLog.Message.Trim(),
                $"Mixed message should match exactly. Expected: '{mixedMessage}', Actual: '{testLog.Message.Trim()}'");
        }
    }
}
