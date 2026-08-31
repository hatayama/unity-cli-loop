using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests
{

/// <summary>
/// Test support type used by editor and play mode fixtures.
/// </summary>
public class GameState : MonoBehaviour
{
    public int hp = 100;
    public float moveSpeed = 5.0f;
    public string playerName = "Hero";
    public bool isAlive = true;

    [SerializeField]
    private int score = 0;

    [SerializeField]
    private float armor = 25.0f;

    public int Score => score;
    public float Armor => armor;
}
}
