using System.Collections.Concurrent;
using System.Threading.Tasks;
using SocketIOClient;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections;

public class AnnouncementClient : MonoBehaviour
{
    [Header("Server Settings")]
    [Tooltip("The URL of the Flask-SocketIO server. e.g., https://ourselves-oscar-pioneer-saying.trycloudflare.com")]
    public string serverUrl = "https://concerning-normal-monster-writers.trycloudflare.com";
    public string adminAnnounceKey = "penislol";

    public UIManager uiManager;
    public SocketIOUnity client;
    private bool isServerAuthenticated = false;
    private bool isClientReadyToEmit = false;
    private bool needsToAuthenticate = false;

    // --- FIX: Thread-safe queues for ALL incoming server events ---
    // These will hold data from the server until the main thread can process it.
    public ConcurrentQueue<string> announcementQueue = new ConcurrentQueue<string>();
    public ConcurrentQueue<float> moneyQueue = new ConcurrentQueue<float>();
    // NEW: A queue for incoming device IDs
    public ConcurrentQueue<List<string>> deviceIdsQueue = new ConcurrentQueue<List<string>>();


    void Start()
    {
        if (uiManager == null)
        {
            Debug.LogError($"[{DateTime.Now:HH:mm:ss}] AnnouncementClient is missing a link to UIManager!");
            return;
        }
        SetupClient();
    }

    void Update()
    {
        // Handle the one-time authentication after connecting
        if (needsToAuthenticate)
        {
            needsToAuthenticate = false;
            StartCoroutine(AuthenticateAfterDelay());
        }

        // --- FIX: Process any queued announcements on the main thread ---
        if (announcementQueue.TryDequeue(out string message))
        {
            // Now it's safe to call the UIManager
            uiManager.ShowAnnouncementBanner(message);
        }

        // --- FIX: Process any queued money on the main thread ---
        if (moneyQueue.TryDequeue(out float amount))
        {
            // Now it's safe to call the UIManager and update the game state
            uiManager.AddMoney(amount);
            uiManager.LogToConsole($"You just received ${amount:F2} from a dev!");
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

        // --- FIX: Event handlers now ONLY add data to the queues. No Unity logic here! ---

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

        // NEW: Event handler for receiving device IDs
        client.On("device_ids_list", (response) =>
        {
            var ids = response.GetValue<List<string>>();
            deviceIdsQueue.Enqueue(ids);
        });

        client.On("auth_success", (response) =>
        {
            isServerAuthenticated = true;
            Debug.Log($"[{DateTime.Now:HH:mm:ss}] Server authentication confirmed.");
            // We can't update UI from here, so we'll queue a console message instead.
            // This is just an example of good practice.
            // For now, we let UIManager handle the message in its own Update loop.
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

    // --- FIX: Re-added this method to resolve the compile error in UIManager ---
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

    // NEW: Method to request device IDs from the server
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
}