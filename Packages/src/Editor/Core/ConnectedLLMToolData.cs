using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Connected LLM Tool data structure for persistence
    /// Related classes:
    /// - ConnectedClient: Connected client information
    /// - McpEditorWindow: UI tool display control
    /// - McpServerController: Server lifecycle management
    /// - McpBridgeServer: Actual client connection management
    /// </summary>
    [Serializable]
    public class ConnectedLLMToolData
    {
        [SerializeField] public string Name;
        [SerializeField] public string Endpoint;
        [SerializeField] public string ConnectedAtString;
        
        // Unity doesn't serialize DateTime directly, so we use a string representation
        public DateTime ConnectedAt 
        { 
            get => string.IsNullOrEmpty(ConnectedAtString) ? DateTime.Now : 
                   DateTime.TryParse(ConnectedAtString, out DateTime result) ? result : DateTime.Now;
            set => ConnectedAtString = value.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        }

        public ConnectedLLMToolData()
        {
            // Default constructor for Unity serialization
        }

        public ConnectedLLMToolData(string name, string endpoint, DateTime connectedAt)
        {
            Name = name;
            Endpoint = endpoint;
            ConnectedAt = connectedAt;
        }
    }
}
