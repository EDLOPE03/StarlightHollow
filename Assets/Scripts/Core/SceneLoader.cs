using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    // Scene indexes matching enabled Build Settings order.
    public const int SCENE_MAIN_MENU       = 0;
    public const int SCENE_EMBERLEAF_GROVE = 1;
    public const int SCENE_DUSKFALL        = 2;
    public const int SCENE_STARLIGHT_VAULT = 3;
    public const int SCENE_ENDING          = 4;

    [SerializeField] private float fadeTime = 0.5f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void LoadNextZone()
    {
        int current = SceneManager.GetActiveScene().buildIndex;
        int next = current + 1;

        if (next >= SCENE_ENDING)
        {
            LoadEnding();
            return;
        }

        StartCoroutine(LoadWithFade(next));
    }

    public void LoadMainMenu() =>
        StartCoroutine(LoadWithFade(SCENE_MAIN_MENU));

    public void LoadEnding() =>
        StartCoroutine(LoadWithFade(SCENE_ENDING));

    public void LoadZone(int sceneIndex) =>
        StartCoroutine(LoadWithFade(sceneIndex));

    public static string GetCurrentZoneName()
    {
        var scene = SceneManager.GetActiveScene();
        string sceneName = scene.name.ToLowerInvariant();

        // Prefer scene-name matching so level/zone labels stay correct even if build indices change.
        if (sceneName.Contains("emberleaf")) return "Emberleaf Grove";
        if (sceneName.Contains("dusk"))      return "Duskfall";
        if (sceneName.Contains("starlight") && sceneName.Contains("vault"))
            return "Starlight Vault";

        return scene.buildIndex switch
        {
            SCENE_EMBERLEAF_GROVE => "Emberleaf Grove",
            SCENE_DUSKFALL        => "Duskfall",
            SCENE_STARLIGHT_VAULT => "Starlight Vault",
            _                     => "Unknown Zone"
        };
    }

    public static int GetCurrentLevel()
    {
        var scene = SceneManager.GetActiveScene();
        string sceneName = scene.name.ToLowerInvariant();

        // Prefer scene-name matching so HUD level stays correct if Build Settings order differs.
        if (sceneName.Contains("emberleaf")) return 1;
        if (sceneName.Contains("dusk"))      return 2;
        if (sceneName.Contains("starlight") && sceneName.Contains("vault"))
            return 3;

        return scene.buildIndex switch
        {
            SCENE_EMBERLEAF_GROVE => 1,
            SCENE_DUSKFALL        => 2,
            SCENE_STARLIGHT_VAULT => 3,
            _                     => 0
        };
    }

    private IEnumerator LoadWithFade(int index)
    {
        yield return new WaitForSeconds(fadeTime);
        SceneManager.LoadScene(index);
    }
}
