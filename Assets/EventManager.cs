using UnityEngine;
using System.Collections.Generic;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }

    public UIManager uiManager;
    public AnnouncementClient announcementClient;
    public List<EventData> allEvents;

    public EventData currentEvent { get; private set; }
    public float eventTimeRemaining { get; private set; }
    public bool isEventActive { get; private set; }

    private AudioSource eventAudioSource;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            eventAudioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Update()
    {
        if (isEventActive)
        {
            eventTimeRemaining -= Time.deltaTime;
            uiManager.UpdateEventTimer(eventTimeRemaining);

            if (eventTimeRemaining <= 0)
            {
                EndEvent();
            }
        }
    }

    public void StartEvent(EventData eventData, float serverTimeRemaining)
    {
        if (eventData == null)
        {
            Debug.LogError("StartEvent called with null eventData.");
            return;
        }

        currentEvent = eventData;
        eventTimeRemaining = serverTimeRemaining;
        isEventActive = true;

        // --- NEW BULLETPROOF AUDIO CODE ---
        if (eventAudioSource == null)
        {
            Debug.LogWarning("eventAudioSource was null. Getting or adding a new one.");
            eventAudioSource = GetComponent<AudioSource>();
            if (eventAudioSource == null)
            {
                eventAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (currentEvent.eventAudio != null)
        {
            Debug.Log("Playing event audio clip!");
            eventAudioSource.PlayOneShot(currentEvent.eventAudio);
        }
        else
        {
            Debug.LogWarning("Event started, but it has no audio clip assigned.");
        }
        // --- END OF NEW CODE ---

        uiManager.ShowEventUI(currentEvent);
    }

    public void EndEvent()
    {
        isEventActive = false;
        currentEvent = null;
        eventTimeRemaining = 0;
        uiManager.HideEventUI();
    }

    public EventData GetEventByName(string name)
    {
        // --- START OF THE "SNITCH" CODE ---
        Debug.Log($"[EventManager] Searching for event with name: '{name}'");
        if (allEvents.Count == 0)
        {
            Debug.LogWarning("[EventManager] The 'allEvents' list is empty! Did you add events in the Inspector?");
            return null;
        }

        foreach (var e in allEvents)
        {
            // This log will show us every name it's checking against.
            // Pay close attention to extra spaces or spelling!
            Debug.Log($"[EventManager] ...comparing against: '{e.eventName}'");

            // Using OrdinalIgnoreCase to be safe, but still good to check the logs.
            if (e.eventName.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[EventManager] Match found! Returning '{e.eventName}'.");
                return e;
            }
        }

        Debug.LogError($"[EventManager] NO MATCH FOUND for '{name}'. Check spelling and ensure it's in the 'allEvents' list in the Inspector.");
        // --- END OF THE "SNITCH" CODE ---

        return null; // This will now only be reached if the loop fails.
    }
}