using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// In-process stand-in for one direction of a process pipe: a reader that blocks on
    /// <see cref="ReadLine"/> until a line is pushed or the pipe is completed (then returns null),
    /// so serve-loop and host tests can drive the protocol without a child process.
    /// </summary>
    internal sealed class BlockingLinePipe : TextReader
    {
        private readonly BlockingCollection<string> _lines = new BlockingCollection<string>();

        public int PendingLineCount
        {
            get { return _lines.Count; }
        }

        public bool IsCompleted
        {
            get { return _lines.IsAddingCompleted; }
        }

        public void Push(string line)
        {
            if (_lines.IsAddingCompleted)
            {
                throw new IOException("pipe closed");
            }

            _lines.Add(line);
        }

        /// <summary>Signals end of stream: pending lines are still delivered, then ReadLine returns null.</summary>
        public void Complete()
        {
            if (!_lines.IsAddingCompleted)
            {
                _lines.CompleteAdding();
            }
        }

        public override string ReadLine()
        {
            if (_lines.TryTake(out string line, Timeout.Infinite))
            {
                return line;
            }

            return null;
        }

        /// <summary>Waits up to <paramref name="timeoutMilliseconds"/> for the next line; null on timeout or end.</summary>
        public string ReadLineWithin(int timeoutMilliseconds)
        {
            if (_lines.TryTake(out string line, timeoutMilliseconds))
            {
                return line;
            }

            return null;
        }

        /// <summary>
        /// Same wait off the calling thread, so an EditMode test can await a line instead of blocking
        /// the Editor's main thread on it.
        /// </summary>
        public Task<string> ReadLineWithinAsync(int timeoutMilliseconds)
        {
            return Task.Run(() => ReadLineWithin(timeoutMilliseconds));
        }
    }

    /// <summary>
    /// TextWriter that turns each completed line into a push on a <see cref="BlockingLinePipe"/>.
    /// </summary>
    internal sealed class BlockingLineWriter : TextWriter
    {
        private readonly BlockingLinePipe _target;
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly object _lock = new object();

        public BlockingLineWriter(BlockingLinePipe target)
        {
            _target = target;
        }

        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }

        public override void Write(char value)
        {
            lock (_lock)
            {
                if (value == '\r')
                {
                    return;
                }

                if (value != '\n')
                {
                    _pending.Append(value);
                    return;
                }

                string line = _pending.ToString();
                _pending.Clear();
                _target.Push(line);
            }
        }

        public override void Write(string value)
        {
            if (value == null)
            {
                return;
            }

            foreach (char character in value)
            {
                Write(character);
            }
        }
    }
}
