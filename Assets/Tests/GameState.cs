using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{

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
