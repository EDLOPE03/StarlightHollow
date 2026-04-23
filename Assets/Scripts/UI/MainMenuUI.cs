using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject ngPlusBadge;
    [SerializeField] private UnityEngine.UI.Button continueButton;

    void Start()
    {
        SaveData save = SaveSystem.Load();

        // Show NG+ badge if unlocked
        if (ngPlusBadge)
            ngPlusBadge.SetActive(save.ngPlus);

        // Grey out continue button if no save exists
        if (continueButton)
            continueButton.interactable = SaveSystem.HasSave();
    }

    public void OnNewGamePressed()
    {
        SaveSystem.DeleteSave();
        SceneManager.LoadScene(1);
    }

    public void OnContinuePressed()
    {
        SceneManager.LoadScene(1);
    }

    public void OnQuitPressed()
    {
        Application.Quit();
        Debug.Log("[Menu] Quit pressed");
    }
}
