using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem.UI;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    // Everything that gets disabled on pause
    private List<MonoBehaviour> _pauseables = new();
    private bool _isPaused = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Find and register everything that should stop on pause
        RefreshPauseables();
    }

    public void RefreshPauseables()
    {
        _pauseables.Clear();

        // Player input and movement
        var playerInput = FindFirstObjectByType<RPGCharacterAnims.RPGCharacterInputController>();
        if (playerInput != null) _pauseables.Add(playerInput);

        var playerMovement = FindFirstObjectByType<RPGCharacterAnims.RPGCharacterMovementController>();
        if (playerMovement != null) _pauseables.Add(playerMovement);

        var playerNav = FindFirstObjectByType<RPGCharacterAnims.RPGCharacterNavigationController>();
        if (playerNav != null) _pauseables.Add(playerNav);

        // SuperCharacterController
        var superChar = FindFirstObjectByType<SuperCharacterController>();
        if (superChar != null) _pauseables.Add(superChar);

        // Combat system
        var combat = FindFirstObjectByType<CombatSystem>();
        if (combat != null) _pauseables.Add(combat);

        // Catch enemy and animal movement scripts
        var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in allBehaviours)
        {
            string typeName = mb.GetType().Name;

            // NEVER disable these - they are needed for UI clicks to work
            if (mb is UIManager) continue;
            if (mb is PauseManager) continue;
            if (mb is InputSystemUIInputModule) continue;
            if (mb is UnityEngine.EventSystems.EventSystem) continue;
            if (mb is UnityEngine.EventSystems.StandaloneInputModule) continue;

            if (typeName.Contains("Move") || 
                typeName.Contains("Enemy") || 
                typeName.Contains("AI") ||
                typeName.Contains("Animal"))
            {
                if (!_pauseables.Contains(mb))
                    _pauseables.Add(mb);
            }
        }

        Debug.Log($"[PauseManager] Registered {_pauseables.Count} pauseable components");
    }

    public void Pause()
    {
        if (_isPaused) return;
        _isPaused = true;

        // Freeze time
        Time.timeScale = 0f;

        // Disable all registered components
        foreach (var mb in _pauseables)
            if (mb != null) mb.enabled = false;

        // Zero out all rigidbodies
        var rigidbodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        foreach (var rb in rigidbodies)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Debug.Log("[PauseManager] Game Paused");
    }

    public void Resume()
    {
        if (!_isPaused) return;
        _isPaused = false;

        // Restore time
        Time.timeScale = 1f;

        // Re-enable all registered components
        foreach (var mb in _pauseables)
            if (mb != null) mb.enabled = true;

        Debug.Log("[PauseManager] Game Resumed");
    }

    public bool IsPaused => _isPaused;
}