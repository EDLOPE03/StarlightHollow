using UnityEngine;

public class EncounterZone : MonoBehaviour
{
    [Header("Settings")]
    public string enemyName = "Enemy";
    public bool   hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        // Only trigger once and only for the player
        if (hasTriggered) return;
        if (!other.CompareTag("Player")) return;
        if (GameLoop.Instance == null) return;
        if (GameLoop.Instance.CurrentPhase != GamePhase.DayExploration) return;

        hasTriggered = true;

        // Hide the visual indicator
        var renderer = GetComponentInChildren<MeshRenderer>();
        if (renderer) renderer.enabled = false;

        // Force combat to start
        Debug.Log($"[EncounterZone] Player entered zone — triggering combat with {enemyName}");
        GameLoop.Instance.TriggerEncounterCombat(enemyName);
    }

    // Reset zone for new day
    public void ResetZone()
    {
        hasTriggered = false;
        var renderer = GetComponentInChildren<MeshRenderer>();
        if (renderer) renderer.enabled = true;
    }
}