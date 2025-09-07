using UnityEngine;
using System.IO;
using System.Collections.Generic;
// why the fuck am i writing this shit cant the players just write the data down or some shit bru
public static class SaveManager
{
    private const string SaveKey = "SandboxSaveData";

    public static void SaveGame(float money, HashSet<int> unlockedParticles)
    {
        SaveData data = new SaveData();
        data.money = money;
        data.unlockedParticleIds = new List<int>(unlockedParticles);

        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(SaveKey, json);
        PlayerPrefs.Save();
        Debug.Log("Game saved.");
    }

    public static SaveData LoadGame()
    {
        if (PlayerPrefs.HasKey(SaveKey))
        {
            string json = PlayerPrefs.GetString(SaveKey);
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            Debug.Log("Game loaded.");
            return data;
        }
        else
        {
            Debug.Log("No save data found. Starting a new game.");
            return new SaveData(); // Return an empty data object
        }
    }
}