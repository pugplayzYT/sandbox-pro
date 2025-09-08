using UnityEngine;
using System.Collections.Generic;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }
    public UIManager uiManager;
    public List<EventData> allEvents;
    public EventData currentEvent { get; private set; }
    private float eventEndTime; // The REAL-WORLD time the event will end
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
            CheckAndUpdateEventStatus();
        }
    }

    // NEW: Handle when application gains/loses focus
    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && isEventActive)
        {
            // Check event status immediately when regaining focus
            CheckAndUpdateEventStatus();
        }
    }

    // NEW: Handle when application is paused/unpaused (mobile)
    void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus && isEventActive)
        {
            // Check event status when unpaused
            CheckAndUpdateEventStatus();
        }
    }

    // NEW: Extracted event checking logic
    private void CheckAndUpdateEventStatus()
    {
        float timeRemaining = eventEndTime - Time.realtimeSinceStartup;
        if (timeRemaining > 0)
        {
            uiManager.UpdateEventTimer(timeRemaining);
        }
        else
        {
            // Event has expired
            Debug.Log("[EventManager] Event timer expired locally.");
            isEventActive = false;
            currentEvent = null;
            uiManager.HideEventUI();
        }
    }

    public void StartEvent(EventData eventData, float serverTimeRemaining)
    {
        if (eventData == null)
        {
            Debug.LogError("StartEvent called with null eventData.");
            return;
        }
        Debug.Log($"[EventManager] Starting event '{eventData.eventName}' with {serverTimeRemaining}s remaining.");
        currentEvent = eventData;
        eventEndTime = Time.realtimeSinceStartup + serverTimeRemaining;
        isEventActive = true;

        if (eventAudioSource == null) eventAudioSource = gameObject.AddComponent<AudioSource>();
        if (currentEvent.eventAudio != null)
        {
            eventAudioSource.PlayOneShot(currentEvent.eventAudio);
        }
        uiManager.ShowEventUI(currentEvent);
    }

    public void EndEvent()
    {
        Debug.Log("[EventManager] Received end_event from server. Ending event officially.");
        isEventActive = false;
        currentEvent = null;
        uiManager.HideEventUI();
    }

    public EventData GetEventByName(string name)
    {
        foreach (var e in allEvents)
        {
            if (e.eventName.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }
        Debug.LogError($"[EventManager] Event with name '{name}' not found in the allEvents list!");
        return null;
    }
}