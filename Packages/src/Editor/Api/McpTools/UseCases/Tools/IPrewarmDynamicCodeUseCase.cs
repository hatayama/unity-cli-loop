using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    internal interface IPrewarmDynamicCodeUseCase
    {
        void Request();

        Task RequestAsync();
    }
}
