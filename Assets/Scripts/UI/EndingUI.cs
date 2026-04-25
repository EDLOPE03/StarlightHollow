using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class EndingUI : MonoBehaviour
{
    [Header("Text References")]
    [SerializeField] private TextMeshProUGUI endingTitle;
    [SerializeField] private TextMeshProUGUI endingSubtitle;
    [SerializeField] private TextMeshProUGUI endingBody;
    [SerializeField] private TextMeshProUGUI survivorCount;
    [SerializeField] private TextMeshProUGUI characterLines;

    [Header("Objects")]
    [SerializeField] private GameObject ngPlusUnlocked;

    void Start()
    {
        SaveData save = SaveSystem.Load();
        ShowEnding(save);
    }

    private void ShowEnding(SaveData save)
    {
        if (ngPlusUnlocked)
            ngPlusUnlocked.SetActive(save.trueEnding);

        if (TeamManager.Instance == null) return;

        int alive = TeamManager.Instance.GetAliveCount();

        if (survivorCount)
            survivorCount.text = $"Survivors: {alive} / 7";

        if (save.trueEnding)
            SetEnding(
                "✦ TRUE ENDING ✦",
                "The Last Spark",
                "Astra alone remained. The Grand Spark pulses " +
                "with renewed light. Starlight Hollow breathes again.",
                Color.yellow);

        else if (alive >= 4)
            SetEnding(
                "STILL STANDING",
                "The Spark endures",
                "Battered but unbroken, the team watches as the " +
                "Spark flickers back to life.",
                new Color(0.4f, 1f, 0.4f));

        else if (alive >= 2)
            SetEnding(
                "QUIET PATHS",
                "Some lights fade",
                "The survivors carry the weight of those who fell, " +
                "pressing forward on quiet uncertain paths.",
                new Color(0.6f, 0.6f, 1f));

        else if (alive == 1)
            SetEnding(
                "THE WATCHER",
                "One flame remains",
                "One soul stands alone. They will carry the memory " +
                "of the others forward.",
                new Color(1f, 0.6f, 0.2f));

        else
            SetEnding(
                "LIGHTS OUT",
                "The spark is gone",
                "The park falls silent. No one remains to tell " +
                "the tale of Starlight Hollow.",
                new Color(0.5f, 0.5f, 0.5f));

        ShowCharacterLines();
    }

    private void SetEnding(string title, string subtitle,
                           string body, Color titleColor)
    {
        if (endingTitle)
        {
            endingTitle.text  = title;
            endingTitle.color = titleColor;
        }
        if (endingSubtitle) endingSubtitle.text = subtitle;
        if (endingBody)     endingBody.text     = body;
    }

    private void ShowCharacterLines()
    {
        if (characterLines == null || TeamManager.Instance == null)
            return;

        string lines = "";
        foreach (var ch in TeamManager.Instance.GetAliveCharacters())
        {
            string line = ch.name switch
            {
                "Forest"  => "\"If the spark lives... so do we.\"",
                "Monty"   => "\"Not bad for a day's work.\"",
                "Phoenix" => "\"Statistically improbable. But here we are.\"",
                "Astra"   => "\"I can still see the light.\"",
                "Coral"   => "\"Pain means alive. I'll take it.\"",
                "Jade"    => "\"We'll make it through. We always do.\"",
                "Winter"  => "\"Systems offline. Heart still running.\"",
                _         => ""
            };
            lines += $"{ch.name}: {line}\n\n";
        }
        characterLines.text = lines;
    }

    public void OnPlayAgainPressed() => SceneManager.LoadScene(0);
    public void OnMainMenuPressed()  => SceneManager.LoadScene(0);
}
