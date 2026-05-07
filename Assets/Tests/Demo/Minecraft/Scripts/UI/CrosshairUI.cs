using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        [SerializeField] private Image crosshairImage;

        private void Start()
        {
            Debug.Assert(crosshairImage != null, "crosshairImage must be assigned in Inspector");
            crosshairImage.color = new Color(1f, 1f, 1f, 0.8f);
        }
    }
}
