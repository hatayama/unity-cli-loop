using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    public class McpBridgeServerShutdownTests
    {
        [Test]
        public async Task AcceptTcpClientAsyncForTests_ShouldComplete_WhenCancellationIsRequested()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            listener.Start();

            try
            {
                Task<TcpClient> acceptTask = McpBridgeServer.AcceptTcpClientAsyncForTests(
                    listener,
                    cancellationTokenSource.Token);

                cancellationTokenSource.Cancel();

                Task completedTask = await Task.WhenAny(acceptTask, Task.Delay(1000));
                Assert.AreSame(acceptTask, completedTask, "Cancelled accept should complete before the timeout");

                TcpClient acceptedClient = await acceptTask;

                Assert.IsNull(acceptedClient, "Cancelled accept should complete without leaving a blocked task");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task StopServer_ShouldObserveTrackedClientTasksAfterShutdown()
        {
            int port = GetFreePort();
            McpBridgeServer server = new McpBridgeServer();
            TcpClient client = new TcpClient();

            try
            {
                server.StartServer(port);
                await client.ConnectAsync(IPAddress.Loopback, port);

                bool taskTracked = await WaitUntilAsync(
                    () => server.GetActiveClientTaskCountForTests() == 1);
                Assert.IsTrue(taskTracked, "Accepted client should be tracked before shutdown");

                server.StopServer();

                bool taskRemoved = await WaitUntilAsync(
                    () => server.GetActiveClientTaskCountForTests() == 0);
                Assert.IsTrue(taskRemoved, "Normal shutdown should keep observing tracked client tasks after returning");
            }
            finally
            {
                client.Close();
                server.Dispose();
            }
        }

        [Test]
        public void StopServerImmediatelyAfterStartServer_ShouldNotThrowOrReportUnexpectedLoopExit()
        {
            int unexpectedExitCount = 0;
            void CountUnexpectedExit()
            {
                Interlocked.Increment(ref unexpectedExitCount);
            }

            McpBridgeServer.OnServerLoopExited += CountUnexpectedExit;
            try
            {
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    McpBridgeServer server = new McpBridgeServer();
                    try
                    {
                        server.StartServer(GetFreePort());
                        Assert.DoesNotThrow(server.StopServer);
                    }
                    finally
                    {
                        server.Dispose();
                    }
                }

                Assert.AreEqual(
                    0,
                    unexpectedExitCount,
                    "Immediate normal shutdown should not be reported as an unexpected server loop exit");
            }
            finally
            {
                McpBridgeServer.OnServerLoopExited -= CountUnexpectedExit;
            }
        }

        [Test]
        public void ShouldTreatLoopExitAsUnexpectedForTests_ShouldIgnoreCanceledLoop_WhenServerIsRunning()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            bool canceledLoopResult = McpBridgeServer.ShouldTreatLoopExitAsUnexpectedForTests(
                true,
                cancellationTokenSource.Token);
            bool activeLoopResult = McpBridgeServer.ShouldTreatLoopExitAsUnexpectedForTests(
                true,
                CancellationToken.None);

            Assert.IsFalse(
                canceledLoopResult,
                "Canceled server loops should not trigger unexpected-exit recovery");
            Assert.IsTrue(
                activeLoopResult,
                "Running non-canceled server loops should still trigger unexpected-exit recovery");
        }

        [Test]
        public async Task StopServerOnMainThread_ShouldNotBlockWaitingForClientTasks()
        {
            int port = GetFreePort();
            McpBridgeServer server = new McpBridgeServer();
            Task delayedClientTask = Task.Delay(1000);

            try
            {
                server.StartServer(port);
                server.TrackClientTaskForTests(delayedClientTask);

                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                server.StopServer();
                stopwatch.Stop();

                Assert.Less(
                    stopwatch.ElapsedMilliseconds,
                    500,
                    "Main-thread shutdown should observe client tasks asynchronously instead of blocking");

                await delayedClientTask;
                bool taskRemoved = await WaitUntilAsync(
                    () => server.GetActiveClientTaskCountForTests() == 0);
                Assert.IsTrue(taskRemoved, "Tracked task should still be observed and removed after completion");
            }
            finally
            {
                server.Dispose();
            }
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            TimeSpan timeout = TimeSpan.FromSeconds(1);
            while (stopwatch.Elapsed < timeout)
            {
                if (predicate())
                {
                    return true;
                }

                await Task.Delay(10);
            }

            return predicate();
        }
    }
}
