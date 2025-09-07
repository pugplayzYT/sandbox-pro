using System.Collections.Generic;

[System.Serializable]
public class SaveData
{
    public float money;
    public List<int> unlockedParticleIds;

    public SaveData()
    {
        money = 0.0f;
        unlockedParticleIds = new List<int>();
    }
}