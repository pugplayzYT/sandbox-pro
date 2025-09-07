using UnityEngine;

[CreateAssetMenu(fileName = "New Particle Data", menuName = "2D Physics Sim/Particle Data")]
public class ParticleData : ScriptableObject
{
    [Header("Particle Info")]
    public string particleName;
    public int id;

    [Header("Visuals")]
    public Color color;

    // In ParticleData.cs
    [Header("Physics Behavior")]
    public bool isSolid = true;
    public bool isLiquid = false;
    public bool isGas = false;
    public float density = 5.0f;
    public bool isGravityAffected = true; // <-- NEW LINE HERE

    [Header("Interactions")]
    public bool isFlammable = false;
    public ParticleData burnsInto;
    public float burnChance = 0.1f;
    public bool isHeatSource = false;

    // --- NEW STUFF HERE ---
    [Header("Shop & Economy")]
    public bool isShopItem = false;
    public float price = 100.0f;
}