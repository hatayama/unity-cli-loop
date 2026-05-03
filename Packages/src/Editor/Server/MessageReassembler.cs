using System;
using System.Collections.Generic;
using System.Text;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Reassembles fragmented bridge messages using Content-Length framing.
    /// Handles partial frames and maintains state across multiple data chunks.
    /// </summary>
    public class MessageReassembler : IDisposable
    {
        private readonly DynamicBufferManager _bufferManager;
        private byte[] _assemblyBuffer;
        private int _currentDataLength = 0;
        private bool _disposed = false;
        
        // Frame parsing state
        private int _expectedContentLength = -1;
        private int _headerLength = -1;
        private bool _headerParsed = false;
        
        // Statistics
        private int _totalMessagesReassembled = 0;
        private int _totalDataChunksProcessed = 0;
        
        /// <summary>
        /// Initializes a new instance of the MessageReassembler.
        /// </summary>
        /// <param name="bufferManager">The buffer manager to use for memory management</param>
        public MessageReassembler(DynamicBufferManager bufferManager = null)
        {
            this._bufferManager = bufferManager ?? new DynamicBufferManager();
            this._assemblyBuffer = this._bufferManager.GetBuffer(BufferConfig.INITIAL_BUFFER_SIZE);
        }
        
        /// <summary>
        /// Gets whether there is incomplete data waiting for more chunks.
        /// </summary>
        public bool HasIncompleteData => _currentDataLength > 0;
        
        /// <summary>
        /// Gets the current amount of data in the assembly buffer.
        /// </summary>
        public int CurrentDataLength => _currentDataLength;
        
        /// <summary>
        /// Gets whether the header has been parsed and we're waiting for content.
        /// </summary>
        public bool IsWaitingForContent => _headerParsed && _expectedContentLength > 0;
        
        /// <summary>
        /// Adds new data to the reassembly buffer.
        /// </summary>
        /// <param name="data">The data chunk to add</param>
        /// <param name="length">The length of valid data in the chunk</param>
        public void AddData(byte[] data, int length)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MessageReassembler));
            }
            
            if (data == null || length <= 0)
            {
                return;
            }
            
            if (length > data.Length)
            {
                throw new ArgumentException("Length cannot exceed data array size", nameof(length));
            }
            
            _totalDataChunksProcessed++;
            
            // Ensure we have enough space in the assembly buffer
            int requiredSize = _currentDataLength + length;
            if (requiredSize > _assemblyBuffer.Length)
            {
                _bufferManager.ResizeBuffer(ref _assemblyBuffer, _currentDataLength, requiredSize);
            }
            
            // Copy new data to assembly buffer
            Array.Copy(data, 0, _assemblyBuffer, _currentDataLength, length);
            _currentDataLength += length;
            
        }
        
        /// <summary>
        /// Attempts to extract complete messages from the assembly buffer.
        /// </summary>
        /// <returns>Array of complete JSON messages, or empty array if none available</returns>
        public string[] ExtractCompleteMessages()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MessageReassembler));
            }
            
            var completeMessages = new List<string>();
            
            while (_currentDataLength > 0)
            {
                string extractedMessage = TryExtractSingleMessage();
                if (extractedMessage == null)
                {
                    // No complete message available
                    break;
                }
                
                completeMessages.Add(extractedMessage);
                _totalMessagesReassembled++;
            }
            
            return completeMessages.ToArray();
        }
        
        /// <summary>
        /// Attempts to extract a single complete message from the buffer.
        /// </summary>
        /// <returns>The extracted JSON message, or null if not complete</returns>
        private string TryExtractSingleMessage()
        {
            // If we haven't parsed the header yet, try to parse it
            if (!_headerParsed)
            {
                if (!TryParseHeader())
                {
                    // Header not complete yet
                    return null;
                }
            }
            
            // Early detection of abnormal states - throw exceptions immediately
            if (_expectedContentLength < 0)
            {
                throw new InvalidOperationException($"Invalid Content-Length value: {_expectedContentLength}. Message framing is corrupted.");
            }
            
            if (_headerLength < 0)
            {
                throw new InvalidOperationException($"Invalid header length: {_headerLength}. Message framing is corrupted.");
            }
            
            if (_expectedContentLength > BufferConfig.MAX_MESSAGE_SIZE)
            {
                throw new InvalidOperationException($"Content-Length {_expectedContentLength} exceeds maximum message size {BufferConfig.MAX_MESSAGE_SIZE}.");
            }
            
            // Check if we have the complete message
            int expectedTotalLength = _headerLength + _expectedContentLength;
            if (_currentDataLength < expectedTotalLength)
            {
                // Message not complete yet
                return null;
            }
            
            // Validate buffer bounds before extraction
            if (_headerLength + _expectedContentLength > _currentDataLength)
            {
                throw new InvalidOperationException($"Buffer underflow: trying to read {_expectedContentLength} bytes at offset {_headerLength}, but only {_currentDataLength} bytes available.");
            }
            
            // Extract the JSON content with proper error handling
            string jsonContent;
            try
            {
                jsonContent = Encoding.UTF8.GetString(_assemblyBuffer, _headerLength, _expectedContentLength);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Invalid UTF-8 sequence detected in message content at offset {_headerLength}, length {_expectedContentLength}: {ex.Message}", ex);
            }
            
            // Log before resetting state
            
            // Remove the processed message from the buffer
            RemoveProcessedData(expectedTotalLength);
            
            // Reset parsing state for next message
            ResetParsingState();
            
            return jsonContent;
        }
        
        /// <summary>
        /// Attempts to parse the Content-Length header from the current buffer.
        /// </summary>
        /// <returns>True if header was successfully parsed, false otherwise</returns>
        private bool TryParseHeader()
        {
            var frameParser = new FrameParser();
            
            bool parseResult = frameParser.TryParseFrame(_assemblyBuffer, _currentDataLength, 
                out _expectedContentLength, out _headerLength);
            
            if (parseResult)
            {
                // Early validation of parsed values
                if (_expectedContentLength < 0)
                {
                    throw new InvalidOperationException($"FrameParser returned invalid Content-Length: {_expectedContentLength}");
                }
                
                if (_headerLength < 0)
                {
                    throw new InvalidOperationException($"FrameParser returned invalid header length: {_headerLength}");
                }
                
                _headerParsed = true;
            }
            
            return parseResult;
        }
        
        /// <summary>
        /// Removes processed data from the beginning of the assembly buffer.
        /// </summary>
        /// <param name="bytesToRemove">Number of bytes to remove from the beginning</param>
        private void RemoveProcessedData(int bytesToRemove)
        {
            if (bytesToRemove <= 0 || bytesToRemove > _currentDataLength)
            {
                return;
            }
            
            // Move remaining data to the beginning of the buffer
            int remainingDataLength = _currentDataLength - bytesToRemove;
            if (remainingDataLength > 0)
            {
                Array.Copy(_assemblyBuffer, bytesToRemove, _assemblyBuffer, 0, remainingDataLength);
            }
            
            _currentDataLength = remainingDataLength;
            
        }
        
        /// <summary>
        /// Resets the parsing state for the next message.
        /// </summary>
        private void ResetParsingState()
        {
            _headerParsed = false;
            _expectedContentLength = -1;
            _headerLength = -1;
        }
        
        /// <summary>
        /// Clears all data from the assembly buffer.
        /// Useful for error recovery or connection reset.
        /// </summary>
        public void Clear()
        {
            if (_disposed)
            {
                return;
            }
            
            _currentDataLength = 0;
            ResetParsingState();
        }
        
        /// <summary>
        /// Gets statistics about the reassembler's performance.
        /// </summary>
        /// <returns>Statistics about message reassembly</returns>
        public MessageReassemblerStats GetStats()
        {
            return new MessageReassemblerStats
            {
                TotalMessagesReassembled = _totalMessagesReassembled,
                TotalDataChunksProcessed = _totalDataChunksProcessed,
                CurrentBufferSize = _assemblyBuffer?.Length ?? 0,
                CurrentDataLength = _currentDataLength,
                HasIncompleteData = HasIncompleteData,
                IsWaitingForContent = IsWaitingForContent,
                ExpectedContentLength = _expectedContentLength,
                HeaderLength = _headerLength
            };
        }
        
        /// <summary>
        /// Validates the current state of the reassembler.
        /// </summary>
        /// <returns>True if state is valid, false if cleanup is needed</returns>
        public bool ValidateState()
        {
            if (_disposed)
            {
                return false;
            }
            
            // Check for reasonable buffer size - throw exception instead of silently clearing
            if (_currentDataLength > BufferConfig.MAX_MESSAGE_SIZE)
            {
                throw new InvalidOperationException($"Buffer size {_currentDataLength} exceeds maximum message size {BufferConfig.MAX_MESSAGE_SIZE}. Data corruption detected.");
            }
            
            // Check for stale incomplete data - throw exception for corrupted state
            if (_headerParsed && _expectedContentLength > 0)
            {
                int expectedTotalLength = _headerLength + _expectedContentLength;
                if (expectedTotalLength > BufferConfig.MAX_MESSAGE_SIZE)
                {
                    throw new InvalidOperationException($"Expected message size {expectedTotalLength} exceeds maximum {BufferConfig.MAX_MESSAGE_SIZE}. Message framing is corrupted.");
                }
            }
            
            // Validate consistency of parsing state
            if (_headerParsed && (_expectedContentLength < 0 || _headerLength < 0))
            {
                throw new InvalidOperationException($"Inconsistent parsing state: headerParsed={_headerParsed}, expectedContentLength={_expectedContentLength}, headerLength={_headerLength}");
            }
            
            return true;
        }
        
        /// <summary>
        /// Gets a preview of the current buffer content for debugging.
        /// </summary>
        /// <param name="maxLength">Maximum length of preview</param>
        /// <returns>String representation of buffer content</returns>
        public string GetBufferPreview(int maxLength = 100)
        {
            if (_disposed || _currentDataLength == 0)
            {
                return string.Empty;
            }
            
            // Safe UTF-8 decoding with proper error handling
            int previewLength = Math.Min(_currentDataLength, maxLength);
            string preview;
            try
            {
                preview = Encoding.UTF8.GetString(_assemblyBuffer, 0, previewLength);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Invalid UTF-8 sequence detected in buffer preview (length {previewLength}): {ex.Message}", ex);
            }
            
            if (_currentDataLength > maxLength)
            {
                preview += "...";
            }
            
            return preview;
        }
        
        /// <summary>
        /// Releases all resources used by the MessageReassembler.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_assemblyBuffer != null)
                {
                    _bufferManager.ReturnBuffer(_assemblyBuffer);
                    _assemblyBuffer = null;
                }
                
                _bufferManager?.Dispose();
                _disposed = true;
                
            }
        }
    }
    
    /// <summary>
    /// Statistics about message reassembler performance and state.
    /// </summary>
    public class MessageReassemblerStats
    {
        /// <summary>
        /// Total number of complete messages reassembled.
        /// </summary>
        public int TotalMessagesReassembled { get; set; }
        
        /// <summary>
        /// Total number of data chunks processed.
        /// </summary>
        public int TotalDataChunksProcessed { get; set; }
        
        /// <summary>
        /// Current size of the assembly buffer.
        /// </summary>
        public int CurrentBufferSize { get; set; }
        
        /// <summary>
        /// Current amount of data in the buffer.
        /// </summary>
        public int CurrentDataLength { get; set; }
        
        /// <summary>
        /// Whether there is incomplete data in the buffer.
        /// </summary>
        public bool HasIncompleteData { get; set; }
        
        /// <summary>
        /// Whether the reassembler is waiting for content after parsing header.
        /// </summary>
        public bool IsWaitingForContent { get; set; }
        
        /// <summary>
        /// Expected content length from parsed header.
        /// </summary>
        public int ExpectedContentLength { get; set; }
        
        /// <summary>
        /// Length of the parsed header.
        /// </summary>
        public int HeaderLength { get; set; }
        
        /// <summary>
        /// Returns a formatted string representation of the statistics.
        /// </summary>
        public override string ToString()
        {
            return $"MessageReassembler Stats: Messages={TotalMessagesReassembled}, " +
                   $"Chunks={TotalDataChunksProcessed}, BufferSize={CurrentDataLength}/{CurrentBufferSize}, " +
                   $"Incomplete={HasIncompleteData}, WaitingForContent={IsWaitingForContent}";
        }
    }
}
