namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response returned by the Focus Unity Window CLI tool.
    /// Indicates whether the foregrounding request succeeded and includes details for optional UI display.
    /// </summary>
    public class FocusUnityWindowResponse : BaseToolResponse
    {
        /// <summary>
        /// Gets whether the Unity Editor window was successfully brought to the foreground.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a message describing the outcome in human-readable form.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an error description, populated only when <see cref="Success"/> is false.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a successful response.
        /// </summary>
        public FocusUnityWindowResponse(string message)
        {
            Success = true;
            Message = message;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Initializes a failed response with error details.
        /// </summary>
        public FocusUnityWindowResponse(string message, string errorMessage)
        {
            Success = false;
            Message = message;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Initializes an empty response, primarily for JSON deserialization.
        /// </summary>
        public FocusUnityWindowResponse()
        {
        }
    }
}
