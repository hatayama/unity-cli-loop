using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Tests for McpServerController port management functionality
    /// Validates automatic port adjustment and port availability checking
    /// </summary>
    public class McpServerPortTests
    {
        [Test]
        public void IsPortInUse_ReturnsTrue_WhenPortIsOccupied()
        {
            // Arrange
            int testPort = 7450; // Use a different port to avoid conflicts
            var testServer = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, testPort);
            
            try
            {
                testServer.Start();
                
                // Act
                bool result = McpBridgeServer.IsPortInUse(testPort);
                
                // Assert
                Assert.IsTrue(result, $"Port {testPort} should be detected as in use");
            }
            finally
            {
                testServer?.Stop();
            }
        }

        [Test]
        public void IsPortInUse_ReturnsFalse_WhenPortIsAvailable()
        {
            // Arrange
            int testPort = 7451; // Use a port that should be available
            
            // Act
            bool result = McpBridgeServer.IsPortInUse(testPort);
            
            // Assert
            Assert.IsFalse(result, $"Port {testPort} should be detected as available");
        }

        [Test]
        public void FindAvailablePort_ReturnsStartPort_WhenAvailable()
        {
            // Arrange - Find a port that should be available
            int startPort = 7452;
            while (McpBridgeServer.IsPortInUse(startPort) && startPort < 7500)
            {
                startPort++;
            }
            
            // Act - Use reflection to access private method
            var method = typeof(McpServerController).GetMethod("FindAvailablePort", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            int result = (int)method.Invoke(null, new object[] { startPort });
            
            // Assert
            Assert.AreEqual(startPort, result, "Should return the start port when it's available");
        }

        [Test]
        public void FindAvailablePort_ReturnsNextPort_WhenStartPortOccupied()
        {
            // Arrange
            int startPort = 7453;
            var testServer = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, startPort);
            
            try
            {
                testServer.Start();
                
                // Act - Use reflection to access private method
                var method = typeof(McpServerController).GetMethod("FindAvailablePort", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                int result = (int)method.Invoke(null, new object[] { startPort });
                
                // Assert
                Assert.Greater(result, startPort, "Should return a port greater than the occupied start port");
                Assert.IsFalse(McpBridgeServer.IsPortInUse(result), "Returned port should be available");
            }
            finally
            {
                testServer?.Stop();
            }
        }

        [Test]
        public void FindAvailablePort_ThrowsException_WhenNoPortsAvailable()
        {
            // Arrange - Use port number that will definitely exceed 65535 
            int startPort = 65530;
            
            // Create multiple servers to occupy the remaining ports
            var servers = new System.Collections.Generic.List<System.Net.Sockets.TcpListener>();
            
            try
            {
                // Occupy ports 65530-65535 to force exception
                for (int port = 65530; port <= 65535; port++)
                {
                    try
                    {
                        var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                        server.Start();
                        servers.Add(server);
                    }
                    catch
                    {
                        // Port might already be in use, continue
                    }
                }
                
                // Act & Assert - Use reflection to access private method
                var method = typeof(McpServerController).GetMethod("FindAvailablePort", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                try
                {
                    method.Invoke(null, new object[] { startPort });
                    Assert.Fail("Expected InvalidOperationException to be thrown");
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    Assert.IsInstanceOf<InvalidOperationException>(ex.InnerException, 
                        "Should throw InvalidOperationException when no ports available");
                }
            }
            finally
            {
                // Clean up servers
                foreach (var server in servers)
                {
                    server?.Stop();
                }
            }
        }

        [Test]
        public void FindAvailablePort_SkipsSystemPorts()
        {
            // Arrange - Test with port 7480 then verify system ports are skipped
            int startPort = 7480;
            
            // Act - Use reflection to access private method
            var method = typeof(McpServerController).GetMethod("FindAvailablePort", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            int result = (int)method.Invoke(null, new object[] { startPort });
            
            // Assert - Verify it doesn't return common system ports in normal operation
            Assert.AreNotEqual(80, result, "Should not return port 80 (system port)");
            Assert.AreNotEqual(443, result, "Should not return port 443 (system port)");
            Assert.AreNotEqual(22, result, "Should not return port 22 (system port)");
            Assert.GreaterOrEqual(result, startPort, "Should return port >= start port");
        }

        [Test]
        public void ValidatePortRange_ValidPorts()
        {
            // Test valid port ranges (above reserved threshold)
            Assert.IsTrue(McpPortValidator.ValidatePort(1024, "test"), "Port 1024 should be valid");
            Assert.IsTrue(McpPortValidator.ValidatePort(7400, "test"), "Port 7400 should be valid");
            Assert.IsTrue(McpPortValidator.ValidatePort(65535, "test"), "Port 65535 should be valid");
        }

        [Test]
        public void ValidatePortRange_InvalidPorts()
        {
            // Test invalid port ranges
            Assert.IsFalse(McpPortValidator.ValidatePort(0, "test"), "Port 0 should be invalid");
            Assert.IsFalse(McpPortValidator.ValidatePort(-1, "test"), "Negative port should be invalid");
            Assert.IsFalse(McpPortValidator.ValidatePort(65536, "test"), "Port 65536 should be invalid");
        }

        [Test]
        public void ClearStartupProtection_ResetsProtectionWindow()
        {
            Type controllerType = typeof(McpServerController);
            FieldInfo field = controllerType.GetField("startupProtectionUntilTicks", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo method = controllerType.GetMethod("ClearStartupProtection", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field, "startupProtectionUntilTicks field should exist");
            Assert.IsNotNull(method, "ClearStartupProtection method should exist");

            try
            {
                long futureTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                field.SetValue(null, futureTicks);

                Assert.IsTrue(McpServerController.IsStartupProtectionActive(), "Startup protection should be active after setting future ticks");

                method.Invoke(null, null);

                Assert.IsFalse(McpServerController.IsStartupProtectionActive(), "Startup protection should be cleared by recovery path");
            }
            finally
            {
                field.SetValue(null, 0L);
            }
        }

        [Test]
        public void OnBeforeAssemblyReload_ShouldClearStartupProtectionBeforeRecovery()
        {
            Type controllerType = typeof(McpServerController);
            FieldInfo field = controllerType.GetField("startupProtectionUntilTicks", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo serverField = controllerType.GetField("mcpServer", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo method = controllerType.GetMethod("OnBeforeAssemblyReload", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field, "startupProtectionUntilTicks field should exist");
            Assert.IsNotNull(serverField, "mcpServer field should exist");
            Assert.IsNotNull(method, "OnBeforeAssemblyReload method should exist");

            object originalServer = serverField.GetValue(null);
            bool originalIsServerRunning = McpEditorSettings.GetIsServerRunning();
            bool originalIsAfterCompile = McpEditorSettings.GetIsAfterCompile();
            bool originalIsDomainReloadInProgress = McpEditorSettings.GetIsDomainReloadInProgress();
            bool originalIsReconnecting = McpEditorSettings.GetIsReconnecting();
            bool originalShowReconnectingUi = McpEditorSettings.GetShowReconnectingUI();
            bool originalShowPostCompileReconnectingUi = McpEditorSettings.GetShowPostCompileReconnectingUI();

            try
            {
                serverField.SetValue(null, new McpBridgeServer());
                long futureTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                field.SetValue(null, futureTicks);

                Assert.IsTrue(McpServerController.IsStartupProtectionActive(), "Startup protection should be active before reload");

                method.Invoke(null, null);

                Assert.IsFalse(
                    McpServerController.IsStartupProtectionActive(),
                    "Assembly reload recovery should clear startup protection so the server can restart"
                );
            }
            finally
            {
                serverField.SetValue(null, originalServer);
                McpEditorSettings.SetIsServerRunning(originalIsServerRunning);
                McpEditorSettings.SetIsAfterCompile(originalIsAfterCompile);
                McpEditorSettings.SetIsDomainReloadInProgress(originalIsDomainReloadInProgress);
                McpEditorSettings.SetIsReconnecting(originalIsReconnecting);
                McpEditorSettings.SetShowReconnectingUI(originalShowReconnectingUi);
                McpEditorSettings.SetShowPostCompileReconnectingUI(originalShowPostCompileReconnectingUi);
                DomainReloadDetectionService.DeleteLockFile();
                field.SetValue(null, 0L);
            }
        }

        [Test]
        public async System.Threading.Tasks.Task StopServerWithUseCaseAsync_ShouldClearStartupProtectionBeforeShutdown()
        {
            Type controllerType = typeof(McpServerController);
            FieldInfo field = controllerType.GetField("startupProtectionUntilTicks", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo serverField = controllerType.GetField("mcpServer", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo method = controllerType.GetMethod("StopServerWithUseCaseAsync", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field, "startupProtectionUntilTicks field should exist");
            Assert.IsNotNull(serverField, "mcpServer field should exist");
            Assert.IsNotNull(method, "StopServerWithUseCaseAsync method should exist");

            object originalServer = serverField.GetValue(null);
            bool originalIsServerRunning = McpEditorSettings.GetIsServerRunning();
            bool originalIsAfterCompile = McpEditorSettings.GetIsAfterCompile();
            bool originalIsDomainReloadInProgress = McpEditorSettings.GetIsDomainReloadInProgress();
            bool originalIsReconnecting = McpEditorSettings.GetIsReconnecting();
            bool originalShowReconnectingUi = McpEditorSettings.GetShowReconnectingUI();
            bool originalShowPostCompileReconnectingUi = McpEditorSettings.GetShowPostCompileReconnectingUI();

            try
            {
                serverField.SetValue(null, new McpBridgeServer());
                long futureTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                field.SetValue(null, futureTicks);

                Assert.IsTrue(McpServerController.IsStartupProtectionActive(), "Startup protection should be active before shutdown");

                System.Threading.Tasks.Task task = (System.Threading.Tasks.Task)method.Invoke(null, null);
                await task;

                Assert.IsFalse(
                    McpServerController.IsStartupProtectionActive(),
                    "Shutdown path should clear startup protection so recovery can restart the server"
                );
            }
            finally
            {
                serverField.SetValue(null, originalServer);
                McpEditorSettings.SetIsServerRunning(originalIsServerRunning);
                McpEditorSettings.SetIsAfterCompile(originalIsAfterCompile);
                McpEditorSettings.SetIsDomainReloadInProgress(originalIsDomainReloadInProgress);
                McpEditorSettings.SetIsReconnecting(originalIsReconnecting);
                McpEditorSettings.SetShowReconnectingUI(originalShowReconnectingUi);
                McpEditorSettings.SetShowPostCompileReconnectingUI(originalShowPostCompileReconnectingUi);
                field.SetValue(null, 0L);
            }
        }

    }
}
