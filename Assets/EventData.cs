using UnityEngine;

[CreateAssetMenu(fileName = "New Event", menuName = "Events/Event Data")]
public class EventData : ScriptableObject
{
    public string eventName;
    public AudioClip eventAudio;
    public float moneyMultiplier = 1.5f;

    public float Duration => eventAudio != null ? eventAudio.length : 0f;
}