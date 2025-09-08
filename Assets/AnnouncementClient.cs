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
    [Tooltip("The URL of the Flask-SocketIO server.")]
    public string serverUrl = "https://concerning-normal-monster-writers.trycloudflare.com";
    public string adminAnnounceKey = "penislol";

    public UIManager uiManager;
    public EventManager eventManager;
    public SocketIOUnity client;
    private bool isServerAuthenticated = false;
    private bool isClientReadyToEmit = false;
    private bool needsToAuthenticate = false;

    public ConcurrentQueue<string> announcementQueue = new ConcurrentQueue<string>();
    public ConcurrentQueue<float> moneyQueue = new ConcurrentQueue<float>();
    public ConcurrentQueue<List<string>> deviceIdsQueue = new ConcurrentQueue<List<string>>();

    public ConcurrentQueue<JObject> eventStartQueue = new ConcurrentQueue<JObject>();
    public ConcurrentQueue<object> eventEndQueue = new ConcurrentQueue<object>();

    void Start()
    {
        if (uiManager == null || eventManager == null)
        {
            Debug.LogError($"[{DateTime.Now:HH:mm:ss}] AnnouncementClient is missing a link to UIManager or EventManager!");
            return;
        }
        SetupClient();
    }

    void Update()
    {
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

        if (eventStartQueue.TryDequeue(out JObject data))
        {
            // SNITCH LOG #3
            Debug.Log($"<color=lime>DEQUEUED EVENT:</color> {data}. Telling EventManager to start.");
            string eventName = data["eventName"].ToString();
            float timeRemaining = data["timeRemaining"].ToObject<float>();
            EventData eventData = eventManager.GetEventByName(eventName);
            if (eventData != null)
            {
                eventManager.StartEvent(eventData, timeRemaining);
            }
        }

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
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Connection successful.");
            isClientReadyToEmit = true;
            needsToAuthenticate = true;
        };

        client.OnDisconnected += (sender, e) =>
        {
            isServerAuthenticated = false; isClientReadyToEmit = false; needsToAuthenticate = false;
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Disconnected from server: {e}");
        };

        client.OnError += (sender, e) => Debug.LogError($"[{DateTime.Now:HH:mm:ss}] SocketIO Error: {e}");

        client.On("global_announcement", (response) =>
        {
            var data = response.GetValue<Dictionary<string, object>>();
            if (data.TryGetValue("message", out object messageObject))
                announcementQueue.Enqueue(messageObject.ToString());
        });

        client.On("add_money", (response) =>
        {
            var data = response.GetValue<Dictionary<string, object>>();
            if (data.TryGetValue("amount", out object amountObject) && float.TryParse(amountObject.ToString(), out float amount))
                moneyQueue.Enqueue(amount);
        });

        client.On("device_ids_list", (response) => deviceIdsQueue.Enqueue(response.GetValue<List<string>>()));

        client.On("start_event", (response) =>
        {
            // SNITCH LOG #1: This tells us if the client is even hearing the broadcast.
            Debug.Log("<color=cyan>start_event HANDLER FIRED!</color>");
            try
            {
                var eventArray = response.GetValue<JArray>();
                if (eventArray != null && eventArray.Count > 0)
                {
                    var jObject = eventArray[0].ToObject<JObject>();
                    // SNITCH LOG #2: This tells us if the data was parsed correctly.
                    Debug.Log($"<color=yellow>EVENT PARSED:</color> {jObject}. Enqueuing now.");
                    eventStartQueue.Enqueue(jObject);
                }
                else { Debug.LogError($"'start_event' payload was null or empty. Raw: {response}"); }
            }
            catch (Exception ex) { Debug.LogError($"Error processing 'start_event': {ex.Message}\nRaw: {response}"); }
        });

        client.On("end_event", (response) => eventEndQueue.Enqueue(null));

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

    // The rest of your script is fine and doesn't need changes.
    private IEnumerator AuthenticateAfterDelay() { yield return new WaitForSeconds(0.5f); AuthenticateClient(); }
    public bool IsAuthenticated() { return isServerAuthenticated; }
    private async void AuthenticateClient() { if (!isClientReadyToEmit) { Debug.LogError("Client not ready to emit."); return; } try { string deviceID = SystemInfo.deviceUniqueIdentifier; Debug.Log($"[{DateTime.Now:HH:mm:ss}] Authenticating with device ID: {deviceID}"); await client.EmitAsync("client_auth", new { device_id = deviceID }); } catch (Exception ex) { Debug.LogError($"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to emit 'client_auth'. Exception: {ex.Message}"); } }
    public async void ConnectToServer() { if (client != null && !client.Connected) { Debug.Log($"[{DateTime.Now:HH:mm:ss}] Attempting to connect..."); await client.ConnectAsync(); } }
    public async void SendAnnouncement(string message) { if (client == null || !client.Connected || !isServerAuthenticated) { uiManager.LogToConsole("Not connected/authed."); return; } await client.EmitAsync("announce", new { message, secret_key = adminAnnounceKey }); uiManager.LogToConsole("Announcement sent."); }
    public async void RequestDeviceIDs() { if (client == null || !client.Connected || !isServerAuthenticated) { uiManager.LogToConsole("Not connected/authed."); return; } await client.EmitAsync("get_device_ids", new { secret_key = adminAnnounceKey }); }
    public async void GiveMoney(string targetDeviceID, float amount) { if (client == null || !client.Connected || !isServerAuthenticated) { uiManager.LogToConsole("Not connected/authed."); return; } await client.EmitAsync("give_money_to_player", new { device_id = targetDeviceID, amount, secret_key = adminAnnounceKey }); }
    public async void TriggerEvent(string eventName) { if (client == null || !client.Connected || !isServerAuthenticated) { uiManager.LogToConsole("Not connected/authed."); return; } await client.EmitAsync("trigger_event", new { event_name = eventName, secret_key = adminAnnounceKey }); uiManager.LogToConsole($"Sent request to start event: {eventName}"); }
}