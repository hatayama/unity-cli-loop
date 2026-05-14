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

                TcpClient acceptedClient = await acceptTask;

                Assert.IsNull(acceptedClient, "Cancelled accept should complete without leaving a blocked task");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task StopServer_ShouldWaitForTrackedClientTasks()
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

                Assert.AreEqual(
                    0,
                    server.GetActiveClientTaskCountForTests(),
                    "Normal shutdown should wait for tracked client tasks to finish");
            }
            finally
            {
                client.Close();
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
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (predicate())
                {
                    return true;
                }

                await Task.Yield();
            }

            return predicate();
        }
    }
}
