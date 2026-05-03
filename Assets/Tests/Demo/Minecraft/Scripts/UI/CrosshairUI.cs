using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
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
