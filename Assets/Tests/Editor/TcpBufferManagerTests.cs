using System;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Unit tests for DynamicBufferManager class.
    /// Tests buffer allocation, recycling, and memory management functionality.
    /// </summary>
    [TestFixture]
    public class TcpBufferManagerTests
    {
        private DynamicBufferManager bufferManager;
        
        [SetUp]
        public void SetUp()
        {
            bufferManager = new DynamicBufferManager();
        }
        
        [TearDown]
        public void TearDown()
        {
            bufferManager?.Dispose();
        }
        
        [Test]
        public void GetBuffer_ValidSize_ReturnsBufferOfCorrectSize()
        {
            // Arrange
            int requiredSize = 1024;
            
            // Act
            byte[] buffer = bufferManager.GetBuffer(requiredSize);
            
            // Assert
            Assert.IsNotNull(buffer);
            Assert.GreaterOrEqual(buffer.Length, requiredSize);
        }
        
        [Test]
        public void GetBuffer_SmallSize_ReturnsMinimumSizedBuffer()
        {
            // Arrange
            int requiredSize = 100;
            
            // Act
            byte[] buffer = bufferManager.GetBuffer(requiredSize);
            
            // Assert
            Assert.IsNotNull(buffer);
            Assert.GreaterOrEqual(buffer.Length, BufferConfig.INITIAL_BUFFER_SIZE);
        }
        
        [Test]
        public void GetBuffer_LargeSize_ReturnsAppropriatelySizedBuffer()
        {
            // Arrange
            int requiredSize = 10000;
            
            // Act
            byte[] buffer = bufferManager.GetBuffer(requiredSize);
            
            // Assert
            Assert.IsNotNull(buffer);
            Assert.GreaterOrEqual(buffer.Length, requiredSize);
            Assert.LessOrEqual(buffer.Length, BufferConfig.MAX_BUFFER_SIZE);
        }
        
        [Test]
        public void GetBuffer_ExcessiveSize_ThrowsArgumentException()
        {
            // Arrange
            int excessiveSize = BufferConfig.MAX_BUFFER_SIZE + 1;
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => bufferManager.GetBuffer(excessiveSize));
        }
        
        [Test]
        public void GetBuffer_ZeroSize_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => bufferManager.GetBuffer(0));
        }
        
        [Test]
        public void GetBuffer_NegativeSize_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => bufferManager.GetBuffer(-1));
        }
        
        [Test]
        public void ReturnBuffer_ValidBuffer_AcceptsBuffer()
        {
            // Arrange
            byte[] buffer = bufferManager.GetBuffer(1024);
            
            // Act & Assert (should not throw)
            Assert.DoesNotThrow(() => bufferManager.ReturnBuffer(buffer));
        }
        
        [Test]
        public void ReturnBuffer_NullBuffer_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() => bufferManager.ReturnBuffer(null));
        }
        
        [Test]
        public void BufferReuse_ReturnAndGetBuffer_ReusesBuffer()
        {
            // Arrange
            int requiredSize = 1024;
            byte[] originalBuffer = bufferManager.GetBuffer(requiredSize);
            
            // Act
            bufferManager.ReturnBuffer(originalBuffer);
            byte[] reusedBuffer = bufferManager.GetBuffer(requiredSize);
            
            // Assert
            Assert.AreSame(originalBuffer, reusedBuffer);
        }
        
        [Test]
        public void BufferReuse_DifferentSizes_ReusesLargerBuffer()
        {
            // Arrange
            byte[] largeBuffer = bufferManager.GetBuffer(2048);
            bufferManager.ReturnBuffer(largeBuffer);
            
            // Act
            byte[] smallBuffer = bufferManager.GetBuffer(1024);
            
            // Assert
            Assert.AreSame(largeBuffer, smallBuffer);
        }
        
        [Test]
        public void BufferReuse_SmallerBufferInPool_ReusesExistingBuffer()
        {
            // Arrange
            byte[] smallBuffer = bufferManager.GetBuffer(1024);
            bufferManager.ReturnBuffer(smallBuffer);
            
            // Act
            byte[] largeBuffer = bufferManager.GetBuffer(2048);
            
            // Assert
            // The pooled buffer is already equal to or larger than the requested size, so reuse should occur
            Assert.AreSame(smallBuffer, largeBuffer);
            Assert.GreaterOrEqual(largeBuffer.Length, 2048);
        }
        
        [Test]
        public void ResizeBuffer_LargerSize_CreatesNewBuffer()
        {
            // Arrange
            byte[] originalBuffer = bufferManager.GetBuffer(1024);
            byte[] testData = { 1, 2, 3, 4, 5 };
            Array.Copy(testData, originalBuffer, testData.Length);
            
            // Act
            bufferManager.ResizeBuffer(ref originalBuffer, testData.Length, 2048);
            
            // Assert
            Assert.GreaterOrEqual(originalBuffer.Length, 2048);
            // Verify data was copied
            for (int i = 0; i < testData.Length; i++)
            {
                Assert.AreEqual(testData[i], originalBuffer[i]);
            }
        }
        
        [Test]
        public void ResizeBuffer_SameSize_KeepsSameBuffer()
        {
            // Arrange
            byte[] originalBuffer = bufferManager.GetBuffer(1024);
            byte[] bufferReference = originalBuffer;
            
            // Act
            bufferManager.ResizeBuffer(ref originalBuffer, 0, 1024);
            
            // Assert
            Assert.AreSame(bufferReference, originalBuffer);
        }
        
        [Test]
        public void ResizeBuffer_SmallerSize_KeepsSameBuffer()
        {
            // Arrange
            byte[] originalBuffer = bufferManager.GetBuffer(2048);
            byte[] bufferReference = originalBuffer;
            
            // Act
            bufferManager.ResizeBuffer(ref originalBuffer, 0, 1024);
            
            // Assert
            Assert.AreSame(bufferReference, originalBuffer);
        }
        
        [Test]
        public void ResizeBuffer_NullBuffer_ThrowsArgumentNullException()
        {
            // Arrange
            byte[] nullBuffer = null;
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => bufferManager.ResizeBuffer(ref nullBuffer, 0, 1024));
        }
        
        [Test]
        public void ResizeBuffer_InvalidDataLength_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = bufferManager.GetBuffer(1024);
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => bufferManager.ResizeBuffer(ref buffer, -1, 2048));
            Assert.Throws<ArgumentException>(() => bufferManager.ResizeBuffer(ref buffer, buffer.Length + 1, 2048));
        }
        
        [Test]
        public void ResizeBuffer_ExcessiveNewSize_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = bufferManager.GetBuffer(1024);
            int excessiveSize = BufferConfig.MAX_BUFFER_SIZE + 1;
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => bufferManager.ResizeBuffer(ref buffer, 0, excessiveSize));
        }
        
        [Test]
        public void GetStats_InitialState_ReturnsZeroStats()
        {
            // Act
            BufferManagerStats stats = bufferManager.GetStats();
            
            // Assert
            Assert.AreEqual(0, stats.TotalBuffersCreated);
            Assert.AreEqual(0, stats.TotalBuffersReused);
            Assert.AreEqual(0, stats.CurrentPoolSize);
            Assert.AreEqual(0, stats.ReuseRate);
        }
        
        [Test]
        public void GetStats_AfterBufferOperations_ReturnsCorrectStats()
        {
            // Arrange
            byte[] buffer1 = bufferManager.GetBuffer(1024);
            bufferManager.GetBuffer(1024);
            bufferManager.ReturnBuffer(buffer1);
            bufferManager.GetBuffer(1024); // Should reuse buffer1
            
            // Act
            BufferManagerStats stats = bufferManager.GetStats();
            
            // Assert
            Assert.AreEqual(2, stats.TotalBuffersCreated); // buffer1 and buffer2
            Assert.AreEqual(1, stats.TotalBuffersReused);  // buffer1 reused as buffer3
            Assert.Greater(stats.ReuseRate, 0);
        }
        
        [Test]
        public void ClearPool_WithBuffersInPool_ClearsPool()
        {
            // Arrange
            byte[] buffer = bufferManager.GetBuffer(1024);
            bufferManager.ReturnBuffer(buffer);
            
            // Act
            bufferManager.ClearPool();
            
            // Assert
            BufferManagerStats stats = bufferManager.GetStats();
            Assert.AreEqual(0, stats.CurrentPoolSize);
        }
        
        [Test]
        public void ValidateBufferParameters_ValidParameters_ReturnsTrue()
        {
            // Arrange
            byte[] buffer = new byte[1024];
            
            // Act & Assert
            Assert.IsTrue(DynamicBufferManager.ValidateBufferParameters(buffer, 0, 1024));
            Assert.IsTrue(DynamicBufferManager.ValidateBufferParameters(buffer, 100, 500));
            Assert.IsTrue(DynamicBufferManager.ValidateBufferParameters(buffer, 1023, 1));
        }
        
        [Test]
        public void ValidateBufferParameters_InvalidParameters_ReturnsFalse()
        {
            // Arrange
            byte[] buffer = new byte[1024];
            
            // Act & Assert
            Assert.IsFalse(DynamicBufferManager.ValidateBufferParameters(null, 0, 100));
            Assert.IsFalse(DynamicBufferManager.ValidateBufferParameters(buffer, -1, 100));
            Assert.IsFalse(DynamicBufferManager.ValidateBufferParameters(buffer, 1024, 1));
            Assert.IsFalse(DynamicBufferManager.ValidateBufferParameters(buffer, 0, -1));
            Assert.IsFalse(DynamicBufferManager.ValidateBufferParameters(buffer, 0, 1025));
        }
        
        [Test]
        public void Dispose_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            bufferManager.Dispose();
            
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => bufferManager.GetBuffer(1024));
        }
        
        [Test]
        public void Dispose_MultipleDispose_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() => {
                bufferManager.Dispose();
                bufferManager.Dispose();
            });
        }
        
        [Test]
        public void BufferManagerStats_ToString_ReturnsFormattedString()
        {
            // Arrange
            var stats = new BufferManagerStats
            {
                TotalBuffersCreated = 5,
                TotalBuffersReused = 3,
                CurrentPoolSize = 2,
                MaxPoolSize = 10,
                ReuseRate = 37.5
            };
            
            // Act
            string result = stats.ToString();
            
            // Assert
            Assert.IsTrue(result.Contains("Created=5"));
            Assert.IsTrue(result.Contains("Reused=3"));
            Assert.IsTrue(result.Contains("PoolSize=2/10"));
            Assert.IsTrue(result.Contains("ReuseRate=37.5%"));
        }
        
        [Test]
        public void MemoryEfficiency_LargeNumberOfOperations_MaintainsReasonableMemoryUsage()
        {
            // Arrange
            const int operationCount = 100;
            
            // Act
            for (int i = 0; i < operationCount; i++)
            {
                byte[] buffer = bufferManager.GetBuffer(1024 + (i % 100));
                bufferManager.ReturnBuffer(buffer);
            }
            
            // Assert
            BufferManagerStats stats = bufferManager.GetStats();
            Assert.Greater(stats.ReuseRate, 50); // Should have good reuse rate
            Assert.LessOrEqual(stats.CurrentPoolSize, 10); // Should not exceed max pool size
        }
    }
}