using UnityEngine;

/// <summary>
/// Provides the Unity component behavior for Make Dont Destroy On Load Scene.
/// </summary>
public class MakeDontDestroyOnLoadScene : MonoBehaviour
{
    private void Start()
    {
        DontDestroyOnLoad(gameObject);
    }
}
