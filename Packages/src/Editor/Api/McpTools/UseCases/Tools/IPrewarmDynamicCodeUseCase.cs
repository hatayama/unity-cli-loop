using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    internal interface IPrewarmDynamicCodeUseCase
    {
        void Request();

        Task RequestAsync();
    }
}
