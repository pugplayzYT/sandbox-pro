using System.Collections.Concurrent;
using System.Threading.Tasks;
using SocketIOClient;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections;
using Newtonsoft.Json.Linq;

public class AnnouncementClient : MonoBehaviour
{
    [Header("Server Settings")]
    [Tooltip("The URL of the Flask-SocketIO server. e.g., https://ourselves-oscar-pioneer-saying.trycloudflare.com")]
    public string serverUrl = "https://concerning-normal-monster-writers.trycloudflare.com";
    public string adminAnnounceKey = "penislol";

    public UIManager uiManager;
    public EventManager eventManager; // ADD THIS
    public SocketIOUnity client;
    private bool isServerAuthenticated = false;
    private bool isClientReadyToEmit = false;
    private bool needsToAuthenticate = false;

    public ConcurrentQueue<string> announcementQueue = new ConcurrentQueue<string>();
    public ConcurrentQueue<float> moneyQueue = new ConcurrentQueue<float>();
    public ConcurrentQueue<List<string>> deviceIdsQueue = new ConcurrentQueue<List<string>>();

    // NEW EVENT QUEUES
    public ConcurrentQueue<JObject> eventStartQueue = new ConcurrentQueue<JObject>();
    public ConcurrentQueue<object> eventEndQueue = new ConcurrentQueue<object>();


    void Start()
    {
        if (uiManager == null || eventManager == null) // ADD eventManager NULL CHECK
        {
            Debug.LogError($"[{DateTime.Now:HH:mm:ss}] AnnouncementClient is missing a link to UIManager or EventManager!");
            return;
        }
        SetupClient();
    }

    void Update()
    {
        Debug.Log($"[AnnouncementClient] Update is running. Event Queue Count: {eventStartQueue.Count}");
        if (needsToAuthenticate)
        {
            needsToAuthenticate = false;
            StartCoroutine(AuthenticateAfterDelay());
        }

        if (announcementQueue.TryDequeue(out string message))
        {
            uiManager.ShowAnnouncementBanner(message);
        }

        if (moneyQueue.TryDequeue(out float amount))
        {
            uiManager.AddMoney(amount);
            uiManager.LogToConsole($"You just received ${amount:F2} from a dev!");
        }

        // NEW: Handle event start
        if (eventStartQueue.TryDequeue(out JObject data))
        {
            string eventName = data["eventName"].ToString();
            float timeRemaining = data["timeRemaining"].ToObject<float>();
            EventData eventData = eventManager.GetEventByName(eventName);
            if (eventData != null)
            {
                eventManager.StartEvent(eventData, timeRemaining);
            }
        }

        // NEW: Handle event end
        if (eventEndQueue.TryDequeue(out _))
        {
            eventManager.EndEvent();
        }
    }

    private void SetupClient()
    {
        client = new SocketIOUnity(serverUrl, new SocketIOOptions
        {
            Query = new Dictionary<string, string> { { "token", "my_token" } }
        });

        client.OnConnected += (sender, e) =>
        {
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Connection successful. Flagging for authentication.");
            isClientReadyToEmit = true;
            needsToAuthenticate = true;
        };

        client.OnDisconnected += (sender, e) =>
        {
            isServerAuthenticated = false;
            isClientReadyToEmit = false;
            needsToAuthenticate = false;
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Disconnected from server: {e}");
        };

        client.OnError += (sender, e) =>
        {
            Debug.LogError($"[{DateTime.Now:HH:mm:ss}] SocketIO Error: {e}");
        };

        client.On("global_announcement", (response) =>
        {
            var data = response.GetValue<Dictionary<string, object>>();
            if (data.TryGetValue("message", out object messageObject))
            {
                announcementQueue.Enqueue(messageObject.ToString());
            }
        });

        client.On("add_money", (response) =>
        {
            var data = response.GetValue<Dictionary<string, object>>();
            if (data.TryGetValue("amount", out object amountObject))
            {
                if (float.TryParse(amountObject.ToString(), out float amount))
                {
                    moneyQueue.Enqueue(amount);
                }
            }
        });

        client.On("device_ids_list", (response) =>
        {
            var ids = response.GetValue<List<string>>();
            deviceIdsQueue.Enqueue(ids);
        });

        // NEW: Event handlers for global events
        client.On("start_event", (response) =>
        {
            try
            {
                // Parse the response string directly to handle the nested array structure
                string rawResponseStr = response.ToString();
                Debug.Log($"[{DateTime.Now:HH:mm:ss}] Raw start_event response: {rawResponseStr}");

                // Parse using Newtonsoft.Json since we're already using it
                var outerArray = Newtonsoft.Json.JsonConvert.DeserializeObject<List<List<Dictionary<string, object>>>>(rawResponseStr);

                if (outerArray != null && outerArray.Count > 0 &&
                    outerArray[0] != null && outerArray[0].Count > 0)
                {
                    var eventData = outerArray[0][0];

                    // Convert the Dictionary to JObject for consistency with the rest of your code
                    var jObject = JObject.FromObject(eventData);

                    Debug.Log($"[{DateTime.Now:HH:mm:ss}] Successfully parsed start_event: {jObject}");
                    eventStartQueue.Enqueue(jObject);
                }
                else
                {
                    Debug.LogError($"Failed to parse nested array structure from: {rawResponseStr}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing 'start_event': {ex.Message}\nStack: {ex.StackTrace}\nRaw: {response}");
            }
        });

        client.On("end_event", (response) =>
        {
            eventEndQueue.Enqueue(null);
        });

        client.On("auth_success", (response) =>
        {
            isServerAuthenticated = true;
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Server authentication confirmed.");
        });

        client.On("auth_error", (response) =>
        {
            isServerAuthenticated = false;
            Debug.LogWarning($"[{DateTime.Now:HH:mm:ss}] Server denied authentication.");
        });

        Debug.Log($"[{DateTime.Now:HH:mm:ss}] Connecting to server...");
        client.Connect();
    }

    private IEnumerator AuthenticateAfterDelay()
    {
        // Using a coroutine is the Unity-native way to wait.
        yield return new WaitForSeconds(0.5f);
        AuthenticateClient();
    }
    public bool IsAuthenticated()
    {
        return isServerAuthenticated;
    }

    private async void AuthenticateClient()
    {
        if (!isClientReadyToEmit)
        {
            Debug.LogError("Client not ready to emit. Authentication aborted.");
            return;
        }
        try
        {
            string deviceID = SystemInfo.deviceUniqueIdentifier;
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Authenticating with device ID: {deviceID}");
            await client.EmitAsync("client_auth", new { device_id = deviceID });
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] 'client_auth' event has been sent to the server.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to emit 'client_auth'. Exception: {ex.Message}");
        }
    }

    public async void ConnectToServer()
    {
        if (client != null && !client.Connected)
        {
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Attempting to connect via ConnectToServer()...");
            await client.ConnectAsync();
        }
    }

    public async void SendAnnouncement(string message)
    {
        if (client == null || !client.Connected || !isServerAuthenticated)
        {
            uiManager.LogToConsole("Cannot send announcement. Not connected or authenticated.");
            return;
        }
        await client.EmitAsync("announce", new { message = message, secret_key = adminAnnounceKey });
        uiManager.LogToConsole("Announcement sent.");
    }

    public async void RequestDeviceIDs()
    {
        if (client == null || !client.Connected || !isServerAuthenticated)
        {
            uiManager.LogToConsole("Cannot request device IDs. Not connected or authenticated.");
            return;
        }
        await client.EmitAsync("get_device_ids", new { secret_key = adminAnnounceKey });
    }


    public async void GiveMoney(string targetDeviceID, float amount)
    {
        if (client == null || !client.Connected || !isServerAuthenticated)
        {
            uiManager.LogToConsole("Cannot give money. Not connected or authenticated.");
            return;
        }
        await client.EmitAsync("give_money_to_player", new { device_id = targetDeviceID, amount = amount, secret_key = adminAnnounceKey });
    }

    // NEW: Method to trigger a global event
    public async void TriggerEvent(string eventName)
    {
        if (client == null || !client.Connected || !isServerAuthenticated)
        {
            uiManager.LogToConsole("Cannot start event. Not connected or authenticated.");
            return;
        }
        await client.EmitAsync("trigger_event", new { event_name = eventName, secret_key = adminAnnounceKey });
        uiManager.LogToConsole($"Sent request to start event: {eventName}");
    }
}