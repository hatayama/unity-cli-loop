using System;
using System.Text;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Unit tests for MessageReassembler class.
    /// Tests message fragmentation handling and reassembly functionality.
    /// </summary>
    [TestFixture]
    public class MessageReassemblerTests
    {
        private MessageReassembler reassembler;
        private DynamicBufferManager bufferManager;
        
        [SetUp]
        public void SetUp()
        {
            bufferManager = new DynamicBufferManager();
            reassembler = new MessageReassembler(bufferManager);
        }
        
        [TearDown]
        public void TearDown()
        {
            reassembler?.Dispose();
            bufferManager?.Dispose();
        }
        
        [Test]
        public void AddData_ValidData_IncreasesDataLength()
        {
            // Arrange
            byte[] data = Encoding.UTF8.GetBytes("test data");
            
            // Act
            reassembler.AddData(data, data.Length);
            
            // Assert
            Assert.AreEqual(data.Length, reassembler.CurrentDataLength);
            Assert.IsTrue(reassembler.HasIncompleteData);
        }
        
        [Test]
        public void AddData_NullData_DoesNothing()
        {
            // Act
            reassembler.AddData(null, 10);
            
            // Assert
            Assert.AreEqual(0, reassembler.CurrentDataLength);
            Assert.IsFalse(reassembler.HasIncompleteData);
        }
        
        [Test]
        public void AddData_ZeroLength_DoesNothing()
        {
            // Arrange
            byte[] data = Encoding.UTF8.GetBytes("test data");
            
            // Act
            reassembler.AddData(data, 0);
            
            // Assert
            Assert.AreEqual(0, reassembler.CurrentDataLength);
            Assert.IsFalse(reassembler.HasIncompleteData);
        }
        
        [Test]
        public void AddData_LengthExceedsArraySize_ThrowsArgumentException()
        {
            // Arrange
            byte[] data = new byte[10];
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => reassembler.AddData(data, 15));
        }
        
        [Test]
        public void ExtractCompleteMessages_CompleteMessage_ReturnsMessage()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            string frameData = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] data = Encoding.UTF8.GetBytes(frameData);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(1, messages.Length);
            Assert.AreEqual(jsonContent, messages[0]);
            Assert.AreEqual(0, reassembler.CurrentDataLength);
            Assert.IsFalse(reassembler.HasIncompleteData);
        }
        
        [Test]
        public void ExtractCompleteMessages_IncompleteMessage_ReturnsEmpty()
        {
            // Arrange
            string incompleteFrame = "Content-Length: 50\r\n\r\n{\"jsonrpc\":\"2.0\""; // Incomplete JSON
            byte[] data = Encoding.UTF8.GetBytes(incompleteFrame);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(0, messages.Length);
            Assert.IsTrue(reassembler.HasIncompleteData);
            Assert.IsTrue(reassembler.IsWaitingForContent);
        }
        
        [Test]
        public void ExtractCompleteMessages_MultipleMessages_ReturnsAllMessages()
        {
            // Arrange
            string json1 = "{\"id\":1}";
            string json2 = "{\"id\":2}";
            string frame1 = $"Content-Length: {json1.Length}\r\n\r\n{json1}";
            string frame2 = $"Content-Length: {json2.Length}\r\n\r\n{json2}";
            byte[] data = Encoding.UTF8.GetBytes(frame1 + frame2);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(2, messages.Length);
            Assert.AreEqual(json1, messages[0]);
            Assert.AreEqual(json2, messages[1]);
            Assert.AreEqual(0, reassembler.CurrentDataLength);
        }
        
        [Test]
        public void ExtractCompleteMessages_FragmentedMessage_ReassemblesCorrectly()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"method\":\"test\",\"id\":1}";
            string frameData = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] fullData = Encoding.UTF8.GetBytes(frameData);
            
            // Add data in fragments
            int chunkSize = 10;
            for (int i = 0; i < fullData.Length; i += chunkSize)
            {
                int remainingBytes = Math.Min(chunkSize, fullData.Length - i);
                byte[] chunk = new byte[remainingBytes];
                Array.Copy(fullData, i, chunk, 0, remainingBytes);
                reassembler.AddData(chunk, remainingBytes);
            }
            
            // Act
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(1, messages.Length);
            Assert.AreEqual(jsonContent, messages[0]);
        }
        
        [Test]
        public void ExtractCompleteMessages_HeaderFragmented_WaitsForCompleteHeader()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            string frameData = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] fullData = Encoding.UTF8.GetBytes(frameData);
            
            // Add only part of the header
            byte[] headerPart = new byte[10];
            Array.Copy(fullData, 0, headerPart, 0, 10);
            
            // Act
            reassembler.AddData(headerPart, headerPart.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(0, messages.Length);
            Assert.IsTrue(reassembler.HasIncompleteData);
            Assert.IsFalse(reassembler.IsWaitingForContent); // Header not complete yet
        }
        
        [Test]
        public void ExtractCompleteMessages_ContentFragmented_WaitsForCompleteContent()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"method\":\"test\",\"id\":1}";
            string frameData = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] fullData = Encoding.UTF8.GetBytes(frameData);
            
            // Add header and part of content
            int headerEndIndex = frameData.IndexOf("\r\n\r\n") + 4;
            byte[] headerAndPartialContent = new byte[headerEndIndex + 10];
            Array.Copy(fullData, 0, headerAndPartialContent, 0, headerAndPartialContent.Length);
            
            // Act
            reassembler.AddData(headerAndPartialContent, headerAndPartialContent.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(0, messages.Length);
            Assert.IsTrue(reassembler.HasIncompleteData);
            Assert.IsTrue(reassembler.IsWaitingForContent);
        }
        
        [Test]
        public void ExtractCompleteMessages_MixedCompleteAndIncomplete_ReturnsCompleteOnly()
        {
            // Arrange
            string json1 = "{\"id\":1}";
            string json2 = "{\"id\":2,\"method\":\"test\"}"; // This will be incomplete
            string frame1 = $"Content-Length: {json1.Length}\r\n\r\n{json1}";
            string frame2 = $"Content-Length: {json2.Length}\r\n\r\n{{\"id\":2"; // Incomplete content
            byte[] data = Encoding.UTF8.GetBytes(frame1 + frame2);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(1, messages.Length);
            Assert.AreEqual(json1, messages[0]);
            Assert.IsTrue(reassembler.HasIncompleteData); // Second message still incomplete
        }
        
        [Test]
        public void Clear_WithData_ClearsAllData()
        {
            // Arrange
            byte[] data = Encoding.UTF8.GetBytes("Content-Length: 25\r\n\r\n{\"test\":\"data\"}");
            reassembler.AddData(data, data.Length);
            
            // Act
            reassembler.Clear();
            
            // Assert
            Assert.AreEqual(0, reassembler.CurrentDataLength);
            Assert.IsFalse(reassembler.HasIncompleteData);
            Assert.IsFalse(reassembler.IsWaitingForContent);
        }
        
        [Test]
        public void GetStats_InitialState_ReturnsZeroStats()
        {
            // Act
            MessageReassemblerStats stats = reassembler.GetStats();
            
            // Assert
            Assert.AreEqual(0, stats.TotalMessagesReassembled);
            Assert.AreEqual(0, stats.TotalDataChunksProcessed);
            Assert.AreEqual(0, stats.CurrentDataLength);
            Assert.IsFalse(stats.HasIncompleteData);
            Assert.IsFalse(stats.IsWaitingForContent);
        }
        
        [Test]
        public void GetStats_AfterOperations_ReturnsCorrectStats()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            string frameData = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] data = Encoding.UTF8.GetBytes(frameData);
            
            // Act
            reassembler.AddData(data, data.Length);
            reassembler.ExtractCompleteMessages();
            MessageReassemblerStats stats = reassembler.GetStats();
            
            // Assert
            Assert.AreEqual(1, stats.TotalMessagesReassembled);
            Assert.AreEqual(1, stats.TotalDataChunksProcessed);
            Assert.AreEqual(0, stats.CurrentDataLength);
            Assert.IsFalse(stats.HasIncompleteData);
        }
        
        [Test]
        public void ValidateState_NormalState_ReturnsTrue()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            string frameData = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] data = Encoding.UTF8.GetBytes(frameData);
            reassembler.AddData(data, data.Length);
            
            // Act
            bool isValid = reassembler.ValidateState();
            
            // Assert
            Assert.IsTrue(isValid);
        }
        
        [Test]
        public void GetBufferPreview_WithData_ReturnsPreview()
        {
            // Arrange
            string testData = "Content-Length: 25\r\n\r\n{\"test\":\"data\"}";
            byte[] data = Encoding.UTF8.GetBytes(testData);
            reassembler.AddData(data, data.Length);
            
            // Act
            string preview = reassembler.GetBufferPreview(20);
            
            // Assert
            Assert.IsNotEmpty(preview);
            Assert.IsTrue(preview.StartsWith("Content-Length"));
        }
        
        [Test]
        public void GetBufferPreview_EmptyBuffer_ReturnsEmpty()
        {
            // Act
            string preview = reassembler.GetBufferPreview();
            
            // Assert
            Assert.IsEmpty(preview);
        }
        
        [Test]
        public void Dispose_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            reassembler.Dispose();
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => reassembler.AddData(new byte[10], 10));
            Assert.Throws<ObjectDisposedException>(() => reassembler.ExtractCompleteMessages());
        }
        
        [Test]
        public void MessageReassemblerStats_ToString_ReturnsFormattedString()
        {
            // Arrange
            var stats = new MessageReassemblerStats
            {
                TotalMessagesReassembled = 5,
                TotalDataChunksProcessed = 10,
                CurrentDataLength = 100,
                CurrentBufferSize = 1024,
                HasIncompleteData = true,
                IsWaitingForContent = false
            };
            
            // Act
            string result = stats.ToString();
            
            // Assert
            Assert.IsTrue(result.Contains("Messages=5"));
            Assert.IsTrue(result.Contains("Chunks=10"));
            Assert.IsTrue(result.Contains("BufferSize=100/1024"));
            Assert.IsTrue(result.Contains("Incomplete=True"));
            Assert.IsTrue(result.Contains("WaitingForContent=False"));
        }
        
        [Test]
        public void ExtractCompleteMessages_JsonWithEmbeddedNewlines_HandlesCorrectly()
        {
            // Arrange
            string jsonContent = "{\n  \"jsonrpc\": \"2.0\",\n  \"method\": \"test\",\n  \"id\": 1\n}";
            string frameData = $"Content-Length: {Encoding.UTF8.GetByteCount(jsonContent)}\r\n\r\n{jsonContent}";
            byte[] data = Encoding.UTF8.GetBytes(frameData);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(1, messages.Length);
            Assert.AreEqual(jsonContent, messages[0]);
        }
        
        [Test]
        public void ExtractCompleteMessages_UnicodeContent_HandlesCorrectly()
        {
            // Arrange
            string jsonContent = "{\"message\":\"Hello World\",\"id\":1}";
            int contentLength = Encoding.UTF8.GetByteCount(jsonContent);
            string frameData = $"Content-Length: {contentLength}\r\n\r\n{jsonContent}";
            byte[] data = Encoding.UTF8.GetBytes(frameData);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(1, messages.Length);
            Assert.AreEqual(jsonContent, messages[0]);
        }
        
        [Test]
        public void ExtractCompleteMessages_LargeMessage_HandlesCorrectly()
        {
            // Arrange
            string largeJsonContent = "{\"data\":\"" + new string('x', 5000) + "\",\"id\":1}";
            string frameData = $"Content-Length: {Encoding.UTF8.GetByteCount(largeJsonContent)}\r\n\r\n{largeJsonContent}";
            byte[] data = Encoding.UTF8.GetBytes(frameData);
            
            // Act
            reassembler.AddData(data, data.Length);
            string[] messages = reassembler.ExtractCompleteMessages();
            
            // Assert
            Assert.AreEqual(1, messages.Length);
            Assert.AreEqual(largeJsonContent, messages[0]);
        }
    }
}