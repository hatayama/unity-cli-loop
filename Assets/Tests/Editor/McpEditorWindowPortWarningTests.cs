using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Tests for McpEditorWindow port warning functionality
    /// Validates port warning detection and UI data creation
    /// </summary>
    public class McpEditorWindowPortWarningTests
    {
        [Test]
        public void ServerControlsData_PortWarning_WhenPortOccupied()
        {
            // Arrange
            int testPort = 7460;
            bool hasPortWarning = false;
            string portWarningMessage = null;
            
            // Create a server to occupy the port
            var testServer = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, testPort);
            
            try
            {
                testServer.Start();
                
                // Simulate the warning detection logic
                if (McpBridgeServer.IsPortInUse(testPort))
                {
                    hasPortWarning = true;
                    portWarningMessage = $"Port {testPort} is already in use. Server will automatically find an available port when started.";
                }
                
                // Act
                var controlsData = new ServerControlsData(
                    customPort: testPort,
                    isServerRunning: false,
                    portEditable: true,
                    hasPortWarning: hasPortWarning,
                    portWarningMessage: portWarningMessage
                );
                
                // Assert
                Assert.IsTrue(controlsData.HasPortWarning, "Should detect port warning when port is occupied");
                Assert.IsNotNull(controlsData.PortWarningMessage, "Warning message should not be null");
                Assert.That(controlsData.PortWarningMessage, Does.Contain($"Port {testPort} is already in use"),
                    "Warning message should mention the occupied port");
            }
            finally
            {
                testServer?.Stop();
            }
        }

        [Test]
        public void ServerControlsData_NoPortWarning_WhenPortAvailable()
        {
            // Arrange
            int testPort = 7461;
            
            // Ensure port is available
            while (McpBridgeServer.IsPortInUse(testPort) && testPort < 7500)
            {
                testPort++;
            }
            
            bool hasPortWarning = false;
            string portWarningMessage = null;
            
            // Simulate the warning detection logic
            if (McpBridgeServer.IsPortInUse(testPort))
            {
                hasPortWarning = true;
                portWarningMessage = $"Port {testPort} is already in use. Server will automatically find an available port when started.";
            }
            
            // Act
            var controlsData = new ServerControlsData(
                customPort: testPort,
                isServerRunning: false,
                portEditable: true,
                hasPortWarning: hasPortWarning,
                portWarningMessage: portWarningMessage
            );
            
            // Assert
            Assert.IsFalse(controlsData.HasPortWarning, "Should not detect port warning when port is available");
            Assert.IsNull(controlsData.PortWarningMessage, "Warning message should be null when no warning");
        }

        [Test]
        public void ServerControlsData_DefaultValues()
        {
            // Act
            var controlsData = new ServerControlsData(
                customPort: 7400,
                isServerRunning: false,
                portEditable: true
            );

            // Assert
            Assert.AreEqual(7400, controlsData.CustomPort, "Custom port should be set correctly");
            Assert.IsFalse(controlsData.IsServerRunning, "Server running state should be set correctly");
            Assert.IsTrue(controlsData.PortEditable, "Port editable state should be set correctly");
            Assert.IsFalse(controlsData.HasPortWarning, "Default should have no warning");
            Assert.IsNull(controlsData.PortWarningMessage, "Default warning message should be null");
        }

    }
}
