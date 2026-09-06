using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    public class EditorTickPumpTests
    {
        /// <summary>
        /// What: a started pump invokes the signal callback repeatedly at roughly the requested interval.
        /// </summary>
        [Test]
        public async Task Start_InvokesSignalRepeatedly()
        {
            int count = 0;
            using (EditorTickPump pump = new EditorTickPump(() => Interlocked.Increment(ref count), 5))
            {
                await Task.Delay(200);
                Assert.That(Volatile.Read(ref count), Is.GreaterThanOrEqualTo(5));
            }
        }

        /// <summary>
        /// What: after Dispose returns, no further signal callback runs, so a torn-down fixture
        /// cannot signal the Editor from a background thread.
        /// </summary>
        [Test]
        public async Task Dispose_StopsSignalsBeforeReturning()
        {
            int count = 0;
            EditorTickPump pump = new EditorTickPump(() => Interlocked.Increment(ref count), 5);
            await Task.Delay(100);
            pump.Dispose();
            int countAtDispose = Volatile.Read(ref count);
            await Task.Delay(100);
            Assert.That(Volatile.Read(ref count), Is.EqualTo(countAtDispose));
        }

        /// <summary>
        /// What: Dispose is idempotent so OneTimeTearDown and a reload hook can both call it.
        /// </summary>
        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            EditorTickPump pump = new EditorTickPump(() => { }, 5);
            pump.Dispose();
            Assert.DoesNotThrow(() => pump.Dispose());
        }

        /// <summary>
        /// What: the constructor rejects a null signal callback.
        /// </summary>
        [Test]
        public void Constructor_NullSignal_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EditorTickPump(null, 5));
        }

        /// <summary>
        /// What: the constructor rejects a non-positive interval, which would spin a thread.
        /// </summary>
        [Test]
        public void Constructor_NonPositiveInterval_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EditorTickPump(() => { }, 0));
        }
    }
}
