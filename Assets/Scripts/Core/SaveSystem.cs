using UnityEngine;
using System;
using System.IO;

[System.Serializable]
public class SaveData
{
    public int    maxDifficulty          = 7;
    public bool   trueEnding             = false;
    public bool   ngPlus                 = false;
    public int    totalRuns              = 0;
    public int    highestDifficultyClear = 0;
    public string lastPlayed             = "";
    public int    lastSceneBuildIndex    = 1;
    public int    lastDifficulty         = 1;
}

public static class SaveSystem
{
    private const  string SAVE_FILE  = "starlight_save.json";
    private const  string PREFS_KEY  = "StarlightHollow_Backup";
    private static string SavePath   => Path.Combine(Application.persistentDataPath, SAVE_FILE);

    // Save to file + PlayerPrefs backup
    public static void Save(SaveData data)
    {
        data.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
            PlayerPrefs.SetString(PREFS_KEY, json);
            PlayerPrefs.Save();
            Debug.Log($"[Save] Saved to: {SavePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Save] Failed to save: {e.Message}");
        }
    }

    // Load: try file first, then PlayerPrefs backup
    public static SaveData Load()
    {
        // Try main file
        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                Debug.Log("[Save] Loaded from file.");
                return JsonUtility.FromJson<SaveData>(json);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Save] File load failed ({e.Message}). Trying backup...");
        }

        // Try PlayerPrefs backup
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            try
            {
                string json = PlayerPrefs.GetString(PREFS_KEY);
                Debug.Log("[Save] Loaded from PlayerPrefs backup.");
                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Backup load also failed: {e.Message}");
            }
        }

        Debug.Log("[Save] No save found. Starting fresh.");
        return new SaveData();
    }

    public static bool HasSave() =>
        File.Exists(SavePath) || PlayerPrefs.HasKey(PREFS_KEY);

    public static void DeleteSave()
    {
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            PlayerPrefs.DeleteKey(PREFS_KEY);
            PlayerPrefs.Save();
            Debug.Log("[Save] Save deleted.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Save] Failed to delete: {e.Message}");
        }
    }
}

