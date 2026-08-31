namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Represents a platform operation result exposed through public tool contracts.
    /// </summary>
    public class ServiceResult<T>
    {
        public bool Success { get; }

        public T Data { get; }

        public string ErrorMessage { get; }

        public ServiceResult(bool success, T data = default, string errorMessage = null)
        {
            Success = success;
            Data = data;
            ErrorMessage = errorMessage;
        }

        public static ServiceResult<T> SuccessResult(T data) => new(true, data);

        public static ServiceResult<T> FailureResult(string errorMessage) => new(false, default, errorMessage);
    }
}
