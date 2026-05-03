namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response schema for SetClientName tool
    /// Confirms client name registration
    /// </summary>
    public class SetClientNameResponse : BaseToolResponse
    {
        /// <summary>
        /// Success status message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Registered client name
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// Create a new SetClientNameResponse
        /// </summary>
        /// <param name="message">Success message</param>
        /// <param name="clientName">Registered client name</param>
        public SetClientNameResponse(string message, string clientName)
        {
            Message = message;
            ClientName = clientName;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public SetClientNameResponse()
        {
            Message = string.Empty;
            ClientName = string.Empty;
        }
    }
}