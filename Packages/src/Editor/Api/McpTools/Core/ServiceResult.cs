namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Generic data model representing service execution result
    /// Used in Application Service Layer
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    /// <typeparam name="T">Type of result data</typeparam>
    public class ServiceResult<T>
    {
        /// <summary>
        /// Whether execution was successful
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Data of execution result
        /// </summary>
        public T Data { get; }

        /// <summary>
        /// Error message when execution fails
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Create ServiceResult
        /// </summary>
        /// <param name="success">Execution result</param>
        /// <param name="data">Result data</param>
        /// <param name="errorMessage">Error message (null on success)</param>
        public ServiceResult(bool success, T data = default, string errorMessage = null)
        {
            Success = success;
            Data = data;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Create success result
        /// </summary>
        /// <param name="data">Result data</param>
        /// <returns>ServiceResult representing success</returns>
        public static ServiceResult<T> SuccessResult(T data) => new(true, data);

        /// <summary>
        /// Create failure result
        /// </summary>
        /// <param name="errorMessage">Error message</param>
        /// <returns>ServiceResult representing failure</returns>
        public static ServiceResult<T> FailureResult(string errorMessage) => new(false, default, errorMessage);
    }
}