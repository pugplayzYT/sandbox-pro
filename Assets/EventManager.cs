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
        if (eventData == null) return;

        currentEvent = eventData;
        eventTimeRemaining = serverTimeRemaining;
        isEventActive = true;

        eventAudioSource.PlayOneShot(currentEvent.eventAudio);
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
        return allEvents.Find(e => e.eventName.Equals(name, System.StringComparison.OrdinalIgnoreCase));
    }
}