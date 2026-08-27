using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public int score = 0;

    public void AddScore(int points)
    {
        score += points;
        Debug.Log("Ring Collected! Current Score: " + score);
    }
}