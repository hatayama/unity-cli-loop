using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Manages dynamic buffer allocation and recycling for framed bridge communication.
    /// Provides memory-efficient buffer management with automatic sizing and cleanup.
    /// </summary>
    public class DynamicBufferManager : IDisposable
    {
        private readonly ConcurrentQueue<byte[]> _bufferPool = new();
        private readonly object _lockObject = new();
        private bool _disposed = false;
        
        // Statistics for monitoring
        private int _totalBuffersCreated = 0;
        private int _totalBuffersReused = 0;
        private int _currentPoolSize = 0;
        
        /// <summary>
        /// Maximum number of buffers to keep in the pool to prevent memory bloat.
        /// </summary>
        private const int MAX_POOL_SIZE = 10;
        
        /// <summary>
        /// Gets a buffer of at least the specified size.
        /// Reuses existing buffers when possible for memory efficiency.
        /// </summary>
        /// <param name="requiredSize">The minimum required buffer size</param>
        /// <returns>A byte array of at least the required size</returns>
        public byte[] GetBuffer(int requiredSize)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicBufferManager));
            }
            
            if (requiredSize <= 0)
            {
                throw new ArgumentException("Required size must be positive", nameof(requiredSize));
            }
            
            if (requiredSize > BufferConfig.MAX_BUFFER_SIZE)
            {
                throw new ArgumentException($"Required size {requiredSize} exceeds maximum buffer size {BufferConfig.MAX_BUFFER_SIZE}", nameof(requiredSize));
            }
            
            // Try to reuse an existing buffer from the pool
            byte[] suitableBuffer = null;
            var tempBuffers = new List<byte[]>();
            
            // Look for a suitable buffer - stop when found
            while (_bufferPool.TryDequeue(out byte[] pooledBuffer))
            {
                lock (_lockObject)
                {
                    _currentPoolSize--;
                }
                
                if (pooledBuffer.Length >= requiredSize && suitableBuffer == null)
                {
                    suitableBuffer = pooledBuffer;
                    break; // Stop searching once we find a suitable buffer
                }
                else
                {
                    // Put back buffers that are too small
                    tempBuffers.Add(pooledBuffer);
                }
            }
            
            // Put back the buffers that were too small
            foreach (var buffer in tempBuffers)
            {
                lock (_lockObject)
                {
                    if (_currentPoolSize < MAX_POOL_SIZE)
                    {
                        _bufferPool.Enqueue(buffer);
                        _currentPoolSize++;
                    }
                }
            }
            
            if (suitableBuffer != null)
            {
                _totalBuffersReused++;
                return suitableBuffer;
            }
            
            // No suitable buffer found, create a new one
            int newBufferSize = CalculateOptimalBufferSize(requiredSize);
            byte[] newBuffer = new byte[newBufferSize];
            
            _totalBuffersCreated++;
            
            return newBuffer;
        }
        
        /// <summary>
        /// Returns a buffer to the pool for potential reuse.
        /// Only keeps buffers within reasonable size limits to prevent memory bloat.
        /// </summary>
        /// <param name="buffer">The buffer to return to the pool</param>
        public void ReturnBuffer(byte[] buffer)
        {
            if (_disposed || buffer == null)
            {
                return;
            }
            
            // Only pool buffers within reasonable size limits
            if (buffer.Length < BufferConfig.MIN_BUFFER_SIZE || buffer.Length > BufferConfig.MAX_BUFFER_SIZE)
            {
                return;
            }
            
            lock (_lockObject)
            {
                // Limit pool size to prevent memory bloat
                if (_currentPoolSize >= MAX_POOL_SIZE)
                {
                    return;
                }
                
                _currentPoolSize++;
            }
            
            _bufferPool.Enqueue(buffer);
        }
        
        /// <summary>
        /// Resizes a buffer to accommodate new data.
        /// Creates a new buffer and copies existing data if necessary.
        /// </summary>
        /// <param name="buffer">Reference to the current buffer</param>
        /// <param name="currentDataLength">Length of valid data in the current buffer</param>
        /// <param name="newSize">The new required buffer size</param>
        public void ResizeBuffer(ref byte[] buffer, int currentDataLength, int newSize)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicBufferManager));
            }
            
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            
            if (currentDataLength < 0)
            {
                throw new ArgumentException("Current data length cannot be negative", nameof(currentDataLength));
            }
            
            if (currentDataLength > buffer.Length)
            {
                throw new ArgumentException("Current data length exceeds buffer size", nameof(currentDataLength));
            }
            
            if (newSize <= buffer.Length)
            {
                // Current buffer is already large enough
                return;
            }
            
            if (newSize > BufferConfig.MAX_BUFFER_SIZE)
            {
                throw new ArgumentException($"New size {newSize} exceeds maximum buffer size {BufferConfig.MAX_BUFFER_SIZE}", nameof(newSize));
            }
            
            // Get a new larger buffer
            byte[] oldBuffer = buffer;
            buffer = GetBuffer(newSize);
            
            // Copy existing data to the new buffer
            if (currentDataLength > 0)
            {
                Array.Copy(oldBuffer, 0, buffer, 0, currentDataLength);
            }
            
            // Return the old buffer to the pool
            ReturnBuffer(oldBuffer);
        }
        
        /// <summary>
        /// Calculates the optimal buffer size for the given required size.
        /// Uses growth factor to minimize frequent reallocations.
        /// </summary>
        /// <param name="requiredSize">The minimum required size</param>
        /// <returns>The optimal buffer size</returns>
        private int CalculateOptimalBufferSize(int requiredSize)
        {
            // Start with initial buffer size
            int optimalSize = BufferConfig.INITIAL_BUFFER_SIZE;
            
            // Grow by factor until we meet the requirement
            while (optimalSize < requiredSize)
            {
                optimalSize *= BufferConfig.BUFFER_GROWTH_FACTOR;
                
                // Cap at maximum buffer size
                if (optimalSize > BufferConfig.MAX_BUFFER_SIZE)
                {
                    optimalSize = BufferConfig.MAX_BUFFER_SIZE;
                    break;
                }
            }
            
            // Ensure we meet the minimum requirement
            return Math.Max(optimalSize, requiredSize);
        }
        
        /// <summary>
        /// Gets buffer management statistics for monitoring and debugging.
        /// </summary>
        /// <returns>Statistics about buffer usage</returns>
        public BufferManagerStats GetStats()
        {
            lock (_lockObject)
            {
                return new BufferManagerStats
                {
                    TotalBuffersCreated = _totalBuffersCreated,
                    TotalBuffersReused = _totalBuffersReused,
                    CurrentPoolSize = _currentPoolSize,
                    MaxPoolSize = MAX_POOL_SIZE,
                    ReuseRate = _totalBuffersCreated > 0 ? (double)_totalBuffersReused / (_totalBuffersCreated + _totalBuffersReused) * 100 : 0
                };
            }
        }
        
        /// <summary>
        /// Clears the buffer pool and releases all pooled buffers.
        /// Useful for memory cleanup during low-usage periods.
        /// </summary>
        public void ClearPool()
        {
            if (_disposed)
            {
                return;
            }
            
            lock (_lockObject)
            {
                int buffersReleased = 0;
                while (_bufferPool.TryDequeue(out _))
                {
                    buffersReleased++;
                }
                
                _currentPoolSize = 0;
            }
        }
        
        /// <summary>
        /// Validates buffer parameters for safety checks.
        /// </summary>
        /// <param name="buffer">The buffer to validate</param>
        /// <param name="offset">The offset within the buffer</param>
        /// <param name="length">The length of data</param>
        /// <returns>True if parameters are valid, false otherwise</returns>
        public static bool ValidateBufferParameters(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                return false;
            }
            
            if (offset < 0 || offset >= buffer.Length)
            {
                return false;
            }
            
            if (length < 0 || offset + length > buffer.Length)
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Releases all resources used by the DynamicBufferManager.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                ClearPool();
                _disposed = true;
            }
        }
    }
    
    /// <summary>
    /// Statistics about buffer manager performance and usage.
    /// </summary>
    public class BufferManagerStats
    {
        /// <summary>
        /// Total number of new buffers created.
        /// </summary>
        public int TotalBuffersCreated { get; set; }
        
        /// <summary>
        /// Total number of buffers reused from the pool.
        /// </summary>
        public int TotalBuffersReused { get; set; }
        
        /// <summary>
        /// Current number of buffers in the pool.
        /// </summary>
        public int CurrentPoolSize { get; set; }
        
        /// <summary>
        /// Maximum allowed pool size.
        /// </summary>
        public int MaxPoolSize { get; set; }
        
        /// <summary>
        /// Buffer reuse rate as a percentage.
        /// </summary>
        public double ReuseRate { get; set; }
        
        /// <summary>
        /// Returns a formatted string representation of the statistics.
        /// </summary>
        public override string ToString()
        {
            return $"BufferManager Stats: Created={TotalBuffersCreated}, Reused={TotalBuffersReused}, " +
                   $"PoolSize={CurrentPoolSize}/{MaxPoolSize}, ReuseRate={ReuseRate:F1}%";
        }
    }
}
