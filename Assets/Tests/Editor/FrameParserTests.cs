using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Unit tests for FrameParser class.
    /// Tests Content-Length header parsing and frame validation functionality.
    /// </summary>
    [TestFixture]
    public class FrameParserTests
    {
        private FrameParser frameParser;
        
        [SetUp]
        public void SetUp()
        {
            frameParser = new FrameParser();
        }
        
        [Test]
        public void TryParseFrame_ValidHeader_ReturnsTrue()
        {
            // Arrange
            string message = "Content-Length: 25\r\n\r\n{\"jsonrpc\":\"2.0\",\"id\":1}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(25, contentLength);
            Assert.AreEqual(22, headerLength); // "Content-Length: 25\r\n\r\n".Length
        }
        
        [Test]
        public void TryParseFrame_IncompleteHeader_ReturnsFalse()
        {
            // Arrange
            string message = "Content-Length: 25\r\n"; // Missing \r\n\r\n
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(-1, contentLength);
            Assert.AreEqual(-1, headerLength);
        }
        
        [Test]
        public void TryParseFrame_InvalidContentLength_ReturnsFalse()
        {
            // Arrange
            string message = "Content-Length: invalid\r\n\r\n{\"test\":\"data\"}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(-1, contentLength);
            Assert.AreEqual(-1, headerLength);
        }
        
        [Test]
        public void TryParseFrame_NegativeContentLength_ReturnsFalse()
        {
            // Arrange
            string message = "Content-Length: -10\r\n\r\n{\"test\":\"data\"}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(-1, contentLength);
            Assert.AreEqual(-1, headerLength);
        }
        
        [Test]
        public void TryParseFrame_ExcessiveContentLength_ReturnsFalse()
        {
            // Arrange
            string message = $"Content-Length: {BufferConfig.MAX_MESSAGE_SIZE + 1}\r\n\r\n{{\"test\":\"data\"}}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(-1, contentLength);
            Assert.AreEqual(-1, headerLength);
        }
        
        [Test]
        public void TryParseFrame_CaseInsensitiveHeader_ReturnsTrue()
        {
            // Arrange
            string message = "content-length: 15\r\n\r\n{\"id\":123,\"ok\":1}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(15, contentLength);
            Assert.AreEqual(22, headerLength);
        }
        
        [Test]
        public void TryParseFrame_MultipleHeaders_ParsesContentLength()
        {
            // Arrange
            string message = "Content-Type: application/json\r\nContent-Length: 30\r\n\r\n{\"jsonrpc\":\"2.0\",\"method\":\"test\"}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(30, contentLength);
            Assert.AreEqual(54, headerLength); // Length of headers + \r\n\r\n
        }
        
        [Test]
        public void IsCompleteFrame_CompleteFrame_ReturnsTrue()
        {
            // Arrange
            string jsonContent = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            string message = $"Content-Length: {jsonContent.Length}\r\n\r\n{jsonContent}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            int contentLength = jsonContent.Length; // Use actual content length
            int headerLength = message.Length - jsonContent.Length; // Calculate header length
            
            // Act
            bool result = frameParser.IsCompleteFrame(buffer, buffer.Length, contentLength, headerLength);
            
            // Assert
            Assert.IsTrue(result);
        }
        
        [Test]
        public void IsCompleteFrame_IncompleteFrame_ReturnsFalse()
        {
            // Arrange
            string message = "Content-Length: 50\r\n\r\n{\"jsonrpc\":\"2.0\",\"id\":1}"; // Content is shorter than declared
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            int contentLength = 50;
            int headerLength = 22;
            
            // Act
            bool result = frameParser.IsCompleteFrame(buffer, buffer.Length, contentLength, headerLength);
            
            // Assert
            Assert.IsFalse(result);
        }
        
        [Test]
        public void ExtractJsonContent_ValidFrame_ReturnsJsonString()
        {
            // Arrange
            string expectedJson = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            string message = $"Content-Length: {expectedJson.Length}\r\n\r\n{expectedJson}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            int contentLength = expectedJson.Length;
            int headerLength = 22; // Correct header length
            
            // Act
            string result = frameParser.ExtractJsonContent(buffer, contentLength, headerLength);
            
            // Assert
            Assert.AreEqual(expectedJson, result);
        }
        
        [Test]
        public void ExtractJsonContent_InvalidParameters_ReturnsNull()
        {
            // Arrange
            byte[] buffer = Encoding.UTF8.GetBytes("test");
            
            // Act & Assert
            Assert.IsNull(frameParser.ExtractJsonContent(null, 10, 5));
            Assert.IsNull(frameParser.ExtractJsonContent(buffer, -1, 5));
            Assert.IsNull(frameParser.ExtractJsonContent(buffer, 10, -1));
        }
        
        [Test]
        public void IsValidContentLength_ValidValues_ReturnsTrue()
        {
            // Act & Assert
            Assert.IsTrue(frameParser.IsValidContentLength(0));
            Assert.IsTrue(frameParser.IsValidContentLength(1024));
            Assert.IsTrue(frameParser.IsValidContentLength(BufferConfig.MAX_MESSAGE_SIZE));
        }
        
        [Test]
        public void IsValidContentLength_InvalidValues_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(frameParser.IsValidContentLength(-1));
            Assert.IsFalse(frameParser.IsValidContentLength(BufferConfig.MAX_MESSAGE_SIZE + 1));
        }
        
        [Test]
        public void TryParseFrame_EmptyBuffer_ReturnsFalse()
        {
            // Arrange
            byte[] buffer = new byte[0];
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, 0, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(-1, contentLength);
            Assert.AreEqual(-1, headerLength);
        }
        
        [Test]
        public void TryParseFrame_NullBuffer_ReturnsFalse()
        {
            // Act
            bool result = frameParser.TryParseFrame(null, 10, out int contentLength, out int headerLength);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(-1, contentLength);
            Assert.AreEqual(-1, headerLength);
        }
        
        [Test]
        public void TryParseFrame_WithWhitespaceInHeader_ReturnsTrue()
        {
            // Arrange
            string message = "Content-Length:    42   \r\n\r\n{\"jsonrpc\":\"2.0\",\"method\":\"test\",\"id\":123}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool result = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int _);
            
            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(42, contentLength);
        }
        
        [Test]
        public void TryParseFrame_JsonWithEmbeddedNewlines_HandlesCorrectly()
        {
            // Arrange
            string jsonContent = "{\n  \"jsonrpc\": \"2.0\",\n  \"method\": \"test\",\n  \"id\": 1\n}";
            string message = $"Content-Length: {Encoding.UTF8.GetByteCount(jsonContent)}\r\n\r\n{jsonContent}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            
            // Act
            bool parseResult = frameParser.TryParseFrame(buffer, buffer.Length, out int contentLength, out int headerLength);
            bool completeResult = frameParser.IsCompleteFrame(buffer, buffer.Length, contentLength, headerLength);
            string extractedJson = frameParser.ExtractJsonContent(buffer, contentLength, headerLength);
            
            // Assert
            Assert.IsTrue(parseResult);
            Assert.IsTrue(completeResult);
            Assert.AreEqual(jsonContent, extractedJson);
        }
    }
}
