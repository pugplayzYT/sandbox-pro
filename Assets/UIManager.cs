using UnityEngine;
using System.IO;
using System.Collections.Generic;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;

public class UIManager : MonoBehaviour
{
    [Header("Required Links")]
    public UIDocument uiDocument;
    public SimulationManager simulationManager;
    public AnnouncementClient announcementClient;
    public EventManager eventManager;

    public static bool IsPointerOverUI { get; private set; }

    private Label fpsLabel;
    private float pollingTime = 0.5f;
    private float time;
    private int frameCount;

    private List<Button> particleButtons = new List<Button>();
    private Button eraserButton;
    private int lastKnownBrushId = -1;

    private VisualElement consoleContainer;
    private TextField consoleInput;
    private VisualElement autocompleteBox;
    private ScrollView consoleOutputScrollView;
    private VisualElement consoleOutputContainer;
    private bool isConsoleOpen = false;
    public List<string> commandList = new List<string> { "clearall", "printallitems", "help", "version", "announce", "givemoney", "getids", "clearallmoney", "startevent" };
    private List<string> commandHistory = new List<string>();
    private int historyIndex = -1;
    private List<string> connectedDeviceIDs = new List<string>();

    private VisualElement announcementBanner;
    private Label announcementLabel;

    private VisualElement eventContainer;
    private Label eventNameLabel;
    private Label eventTimerLabel;
    private VisualElement eventTooltip;
    private Label eventMultiplierLabel;

    private bool isAuthorized = false;
    private string consoleAuthFilePath;

    [Header("Economy")]
    [SerializeField] private Label moneyLabel;
    public float currentMoney = 10.00f;
    public HashSet<int> unlockedParticles = new HashSet<int>();
    private const float SaveInterval = 30f;
    private float timeSinceLastSave = 0f;

    void Start()
    {
        if (uiDocument == null || simulationManager == null || eventManager == null)
        {
            Debug.LogError("UI Manager is missing links!");
            return;
        }

        LoadGameData();

        SetupUI();
        SetupConsole();
        SetupAnnouncementBanner();
        SetupEventUI();
        CheckAuthorization();

        if (announcementClient != null)
        {
            announcementClient.ConnectToServer();
        }
        else
        {
            Debug.LogError("UI Manager is missing a link to AnnouncementClient!");
        }
    }

    void Update()
    {
        if (announcementClient != null)
        {
            if (announcementClient.deviceIdsQueue.TryDequeue(out List<string> ids))
            {
                UpdateConnectedDeviceIDs(ids);
            }
        }

        timeSinceLastSave += Time.deltaTime;
        if (timeSinceLastSave >= SaveInterval)
        {
            SaveManager.SaveGame(currentMoney, unlockedParticles);
            timeSinceLastSave = 0f;
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            ToggleConsole();
        }

        var sidebar = uiDocument.rootVisualElement.Q("sidebar-container");
        Vector2 mousePos = Input.mousePosition;
        mousePos.y = Screen.height - mousePos.y;
        bool overSidebar = sidebar != null && sidebar.worldBound.Contains(mousePos);
        bool overConsole = isConsoleOpen && consoleContainer != null && consoleContainer.worldBound.Contains(mousePos);
        IsPointerOverUI = overSidebar || overConsole;

        if (SimulationManager.currentBrushId != lastKnownBrushId)
        {
            UpdateSelectedButtonUI();
        }

        if (fpsLabel != null)
        {
            time += Time.deltaTime;
            frameCount++;
            if (time >= pollingTime)
            {
                int frameRate = Mathf.RoundToInt(frameCount / time);
                fpsLabel.text = "FPS: " + frameRate.ToString();
                time -= pollingTime;
                frameCount = 0;
            }
        }
    }

    private void OnApplicationQuit()
    {
        SaveManager.SaveGame(currentMoney, unlockedParticles);
    }

    private void LoadGameData()
    {
        SaveData data = SaveManager.LoadGame();
        currentMoney = data.money;
        unlockedParticles = new HashSet<int>(data.unlockedParticleIds);
        UpdateMoneyLabel();
    }

    private void UpdateMoneyLabel()
    {
        if (moneyLabel != null)
        {
            moneyLabel.text = $"Money: ${currentMoney:F2}";
        }
    }

    public void AddMoney(float amount)
    {
        currentMoney += amount;
        UpdateMoneyLabel();
    }

    public void BuyParticle(int particleId, float price)
    {
        if (unlockedParticles.Contains(particleId))
        {
            LogToConsole("You already own this item.");
            return;
        }

        if (currentMoney >= price)
        {
            currentMoney -= price;
            unlockedParticles.Add(particleId);
            LogToConsole($"Item purchased! You now have ${currentMoney:F2}");
            UpdateMoneyLabel();

            RebuildParticleButtons();
            SimulationManager.currentBrushId = particleId;
            SaveManager.SaveGame(currentMoney, unlockedParticles);
        }
        else
        {
            LogToConsole("Not enough money, bro.");
        }
    }

    void SetupUI()
    {
        var root = uiDocument.rootVisualElement;
        fpsLabel = root.Q<Label>("fps-label");
        moneyLabel = root.Q<Label>("money-label");
        RebuildParticleButtons();
        UpdateSelectedButtonUI();
        UpdateMoneyLabel();
    }

    void SetupEventUI()
    {
        var root = uiDocument.rootVisualElement;
        eventContainer = root.Q<VisualElement>("event-container");
        eventNameLabel = root.Q<Label>("event-name-label");
        eventTimerLabel = root.Q<Label>("event-timer-label");
        eventTooltip = root.Q<VisualElement>("event-tooltip");
        eventMultiplierLabel = root.Q<Label>("event-multiplier-label");

        if (eventContainer != null)
        {
            eventContainer.RegisterCallback<PointerEnterEvent>(evt => eventTooltip.style.display = DisplayStyle.Flex);
            eventContainer.RegisterCallback<PointerLeaveEvent>(evt => eventTooltip.style.display = DisplayStyle.None);
            HideEventUI();
        }
    }

    public void ShowEventUI(EventData data)
    {
        if (eventContainer == null) return;
        eventNameLabel.text = data.eventName;
        eventMultiplierLabel.text = $"x{data.moneyMultiplier:F2} Money";
        eventContainer.style.display = DisplayStyle.Flex;
    }

    public void HideEventUI()
    {
        if (eventContainer == null) return;
        eventContainer.style.display = DisplayStyle.None;
    }

    public void UpdateEventTimer(float timeRemaining)
    {
        if (eventTimerLabel == null || !eventManager.isEventActive) return;
        eventTimerLabel.text = $"Time Left: {Mathf.CeilToInt(timeRemaining)}s";
    }

    private void RebuildParticleButtons()
    {
        var buttonContainer = uiDocument.rootVisualElement.Q<VisualElement>("button-list");
        if (buttonContainer == null) { Debug.LogError("UXML MISMATCH: 'button-list' not found."); return; }

        buttonContainer.Clear();
        particleButtons.Clear();

        foreach (var particle in simulationManager.particleTypes)
        {
            Button button = new Button { text = particle.particleName };
            button.AddToClassList("particle-button");
            int particleId = particle.id;
            if (particle.isShopItem && !unlockedParticles.Contains(particleId))
            {
                button.text = $"{particle.particleName} (${particle.price:F2})";
                button.clicked += () => { BuyParticle(particleId, particle.price); };
                button.AddToClassList("shop-item");
            }
            else
            {
                button.clicked += () => { SimulationManager.currentBrushId = particleId; };
            }
            buttonContainer.Add(button);
            particleButtons.Add(button);
        }

        if (eraserButton == null)
        {
            eraserButton = new Button { text = "Eraser" };
            eraserButton.AddToClassList("particle-button");
            eraserButton.clicked += () => { SimulationManager.currentBrushId = 0; };
        }
        buttonContainer.Add(eraserButton);
    }


    void UpdateSelectedButtonUI()
    {
        for (int i = 0; i < particleButtons.Count; i++)
        {
            if (i < simulationManager.particleTypes.Count)
            {
                bool isSelected = simulationManager.particleTypes[i].id == SimulationManager.currentBrushId;
                particleButtons[i].EnableInClassList("selected", isSelected);
            }
        }
        if (eraserButton != null)
        {
            eraserButton.EnableInClassList("selected", SimulationManager.currentBrushId == 0);
        }
        lastKnownBrushId = SimulationManager.currentBrushId;
    }

    private void SetupAnnouncementBanner()
    {
        var root = uiDocument.rootVisualElement;
        announcementBanner = root.Q<VisualElement>("announcement-banner");
        announcementLabel = root.Q<Label>("announcement-label");
        if (announcementBanner == null || announcementLabel == null)
        {
            Debug.LogError("UXML MISMATCH: Announcement banner elements not found.");
        }
    }

    public void ShowAnnouncementBanner(string message)
    {
        Debug.Log($"Yo, the banner method got called with: {message}");
        if (announcementBanner != null)
        {
            announcementLabel.text = message;
            StopAllCoroutines();
            StartCoroutine(ShowAndHideBannerCoroutine());
        }
    }

    private IEnumerator ShowAndHideBannerCoroutine()
    {
        announcementBanner.style.display = DisplayStyle.Flex;
        announcementBanner.style.top = new StyleLength(new Length(0, LengthUnit.Pixel));
        yield return new WaitForSeconds(3.0f);
        announcementBanner.style.top = new StyleLength(new Length(-100, LengthUnit.Pixel));
        yield return new WaitForSeconds(0.5f);
        announcementBanner.style.display = DisplayStyle.None;
    }

    #region Console Logic
    void SetupConsole()
    {
        var root = uiDocument.rootVisualElement;
        consoleContainer = root.Q<VisualElement>("console-container");
        consoleOutputScrollView = root.Q<ScrollView>("console-output-scrollview");
        consoleOutputContainer = root.Q<VisualElement>("console-output-container");
        consoleInput = root.Q<TextField>("console-input");
        autocompleteBox = root.Q<VisualElement>("autocomplete-box");
        if (consoleContainer == null || consoleInput == null || autocompleteBox == null || consoleOutputScrollView == null || consoleOutputContainer == null)
        {
            Debug.LogError("UXML MISMATCH: Console elements not found.");
            return;
        }
        autocompleteBox.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.15f, 0.85f));
        autocompleteBox.style.paddingLeft = 5;
        autocompleteBox.style.paddingTop = 2;
        autocompleteBox.style.paddingBottom = 2;
        var textInputElement = consoleInput.Q(TextField.textInputUssName);
        if (textInputElement != null)
        {
            textInputElement.style.color = new StyleColor(Color.white);
            textInputElement.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.9f));
        }
        consoleInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        consoleInput.RegisterValueChangedCallback(OnInputValueChanged);
    }

    private void CheckAuthorization()
    {
        string deviceID = SystemInfo.deviceUniqueIdentifier;
        consoleAuthFilePath = "Assets/Resources/console_auth.json";
        Debug.Log($"Your device ID is: {deviceID}. Copy this and paste it into '{consoleAuthFilePath}' to gain access to the console.");
        TextAsset jsonTextAsset = Resources.Load<TextAsset>("console_auth");
        if (jsonTextAsset == null)
        {
            LogToConsole("Yo, the console auth file is missing. Add 'console_auth.json' to a Resources folder. Path: " + consoleAuthFilePath);
            return;
        }
        try
        {
            ConsoleAuthData authData = JsonUtility.FromJson<ConsoleAuthData>(jsonTextAsset.text);
            if (authData.allowedDeviceIDs.Contains(deviceID))
            {
                isAuthorized = true;
                LogToConsole($"Welcome, you're in the squad. Device ID: {deviceID}");
            }
            else
            {
                isAuthorized = false;
                LogToConsole($"You're not in the thing. Path to get in: {consoleAuthFilePath}");
                LogToConsole("To verify yourself, copy your device ID from the Unity Console (Ctrl+Shift+C).");
            }
        }
        catch (System.Exception e)
        {
            LogToConsole($"Something went wrong with the auth file. It's a skill issue. Check the format. Error: {e.Message}");
        }
    }

    public void LogToConsole(string message)
    {
        if (consoleOutputContainer == null || consoleOutputScrollView == null) return;
        var label = new Label(message);
        label.AddToClassList("console-output-label");
        label.style.color = Color.cyan;
        consoleOutputContainer.Add(label);
        consoleOutputScrollView.schedule.Execute(() => consoleOutputScrollView.ScrollTo(consoleOutputContainer.Children().LastOrDefault()));
    }

    private void UpdateConnectedDeviceIDs(List<string> ids)
    {
        connectedDeviceIDs = ids;
        LogToConsole("Received list of connected device IDs from server.");
    }

    private void ToggleConsole()
    {
        isConsoleOpen = !isConsoleOpen;
        consoleContainer.style.display = isConsoleOpen ? DisplayStyle.Flex : DisplayStyle.None;
        if (isConsoleOpen)
        {
            consoleInput.Focus();
            historyIndex = commandHistory.Count;
        }
        else
        {
            consoleInput.Blur();
        }
    }

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            ProcessCommand(consoleInput.text);
            consoleInput.SetValueWithoutNotify("");
            autocompleteBox.Clear();
            evt.StopImmediatePropagation();
        }
        else if (evt.keyCode == KeyCode.Tab)
        {
            evt.StopPropagation();
            consoleInput.focusController.IgnoreEvent(evt);
            var firstSuggestion = autocompleteBox.Q<Label>();
            if (firstSuggestion != null)
            {
                var words = consoleInput.text.Split(' ');
                string completedText;
                if (words.Length > 1)
                {
                    words[words.Length - 1] = firstSuggestion.text;
                    completedText = string.Join(" ", words) + " ";
                }
                else
                {
                    completedText = firstSuggestion.text + " ";
                }

                consoleInput.value = completedText;
                consoleInput.schedule.Execute(() =>
                {
                    consoleInput.Focus();
                    consoleInput.cursorIndex = completedText.Length;
                    consoleInput.selectIndex = completedText.Length;
                });
            }
        }
        else if (evt.keyCode == KeyCode.UpArrow)
        {
            if (commandHistory.Count > 0)
            {
                historyIndex = Mathf.Max(0, historyIndex - 1);
                string historyText = commandHistory[historyIndex];
                consoleInput.SetValueWithoutNotify(historyText);
                consoleInput.schedule.Execute(() => consoleInput.cursorIndex = historyText.Length);
            }
        }
        else if (evt.keyCode == KeyCode.DownArrow)
        {
            if (commandHistory.Count > 0)
            {
                historyIndex = Mathf.Min(commandHistory.Count, historyIndex + 1);
                if (historyIndex < commandHistory.Count)
                {
                    string historyText = commandHistory[historyIndex];
                    consoleInput.SetValueWithoutNotify(historyText);
                    consoleInput.schedule.Execute(() => consoleInput.cursorIndex = historyText.Length);
                }
                else
                {
                    consoleInput.SetValueWithoutNotify("");
                }
            }
        }
    }

    private void OnInputValueChanged(ChangeEvent<string> evt)
    {
        UpdateAutocomplete(evt.newValue);
    }

    // --- THIS IS THE COMPLETELY REWRITTEN AND FIXED METHOD ---
    private void UpdateAutocomplete(string currentText)
    {
        autocompleteBox.Clear();
        if (string.IsNullOrEmpty(currentText)) return;

        var parts = currentText.Split(' ');

        // --- Part 1: Handle Command Autocomplete ---
        if (parts.Length == 1)
        {
            string currentCommandPart = parts[0];
            var suggestions = commandList
                .Where(c => c.StartsWith(currentCommandPart, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            ShowSuggestions(suggestions, isCommand: true);
            return;
        }

        // --- Part 2: Handle Argument Autocomplete ---
        if (parts.Length >= 2 && !currentText.EndsWith(" "))
        {
            string command = parts[0].ToLower();
            string currentArgumentPart = parts.Last();
            List<string> suggestions = new List<string>();

            switch (command)
            {
                case "clearall":
                    suggestions = simulationManager.GetParticleTypeNames()
                        .Where(p => p.StartsWith(currentArgumentPart, System.StringComparison.OrdinalIgnoreCase)).ToList();
                    break;
                case "givemoney":
                    if (parts.Length == 2) // Only suggest for the device ID part
                    {
                        suggestions = connectedDeviceIDs
                            .Where(id => id.StartsWith(currentArgumentPart, System.StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    break;
                case "startevent":
                    if (parts.Length == 2) // Only suggest for the event name part
                    {
                        suggestions = eventManager.allEvents
                            .Select(e => e.eventName)
                            .Where(name => name.StartsWith(currentArgumentPart, System.StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    break;
            }
            ShowSuggestions(suggestions, isCommand: false, commandPart: parts[0]);
        }
    }

    private void ShowSuggestions(List<string> suggestions, bool isCommand, string commandPart = "")
    {
        foreach (var suggestion in suggestions.Take(5))
        {
            var label = new Label(suggestion);
            label.AddToClassList("autocomplete-label");
            label.style.color = Color.cyan;
            label.RegisterCallback<PointerDownEvent>(evt =>
            {
                string completedText = isCommand ? suggestion : $"{commandPart} {suggestion}";
                completedText += " ";

                consoleInput.SetValueWithoutNotify(completedText);
                consoleInput.schedule.Execute(() => {
                    consoleInput.Focus();
                    consoleInput.cursorIndex = completedText.Length;
                    consoleInput.selectIndex = completedText.Length;
                });
                autocompleteBox.Clear();
            });
            autocompleteBox.Add(label);
        }
    }


    private void ProcessCommand(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return;
        LogToConsole($"> {commandText}");

        if (!isAuthorized)
        {
            LogToConsole("You're not on the list. You can't run commands.");
            return;
        }
        if (!announcementClient.IsAuthenticated())
        {
            LogToConsole("Cannot run command. Not authenticated with server.");
            return;
        }

        if (commandHistory.Count == 0 || commandHistory.Last() != commandText)
        {
            commandHistory.Add(commandText);
        }
        historyIndex = commandHistory.Count;
        var parts = commandText.Trim().Split(' ');
        var command = parts[0].ToLower();
        var args = parts.Skip(1).ToArray();
        switch (command)
        {
            case "clearall":
                string particleName = args.Length > 0 ? args[0] : null;
                simulationManager.ConsoleClearAll(particleName);
                break;
            case "printallitems":
                simulationManager.ConsolePrintAllItems();
                break;
            case "help":
                simulationManager.ConsoleHelp(this);
                break;
            case "version":
                simulationManager.ConsoleVersion();
                break;
            case "announce":
                if (args.Length > 0)
                {
                    string message = string.Join(" ", args);
                    LogToConsole($"Sending announcement: {message}");
                    announcementClient.SendAnnouncement(message);
                }
                else
                {
                    LogToConsole("Usage: announce <message>");
                }
                break;
            case "givemoney":
                GiveMoneyCommand(args);
                break;
            case "getids":
                GetDeviceIDsCommand();
                break;
            case "clearallmoney":
                ClearAllMoneyCommand();
                break;
            case "startevent":
                if (args.Length > 0)
                {
                    string eventName = args[0];
                    announcementClient.TriggerEvent(eventName);
                }
                else
                {
                    LogToConsole("Usage: startevent <EventName>");
                }
                break;
            default:
                LogToConsole($"Console: Unknown command '{command}'");
                break;
        }
    }

    private void GetDeviceIDsCommand()
    {
        LogToConsole("Requesting device IDs from server...");
        announcementClient.RequestDeviceIDs();
    }

    private void ClearAllMoneyCommand()
    {
        currentMoney = 0.00f;
        unlockedParticles.Clear();
        UpdateMoneyLabel();
        RebuildParticleButtons();
        SaveManager.SaveGame(currentMoney, unlockedParticles);
        LogToConsole("Bet. Wiped all money and purchased items. Back to zero.");
    }

    private void GiveMoneyCommand(string[] args)
    {
        if (args.Length < 2)
        {
            LogToConsole("Usage: givemoney <deviceID> <amount>");
            return;
        }
        string targetDeviceID = args[0];
        if (!float.TryParse(args[1], out float amount))
        {
            LogToConsole("Invalid amount. It must be a number.");
            return;
        }
        announcementClient.GiveMoney(targetDeviceID, amount);
        LogToConsole($"Attempting to give ${amount:F2} to device ID: {targetDeviceID}.");
    }
    #endregion
}