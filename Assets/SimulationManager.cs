using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System.Linq;

public class Chunk
{
    public int x, y;
    public Chunk(int x, int y) { this.x = x; this.y = y; }
}

[System.Serializable]
public class ParticleInteraction
{
    public string interactionName;
    public ParticleData particleA;
    public ParticleData particleB;
    public ParticleData outcomeForA;
    public ParticleData outcomeForB;
    [Range(0, 1)]
    public float chance = 1.0f;
}

public class SimulationManager : MonoBehaviour
{
    private const int CHUNK_SIZE = 16;
    private Chunk[,] chunks;
    private int numChunksX, numChunksY;
    private List<Chunk> activeChunks = new List<Chunk>();
    private HashSet<Chunk> chunksToActivateNextFrame = new HashSet<Chunk>();

    [Header("Setup")]
    public bool autoConfigureScene = true;

    [Header("Required Links")]
    public List<ParticleData> particleTypes;
    public UIManager uiManager;
    public EventManager eventManager; // ADD THIS

    [Header("Interactions")]
    public List<ParticleInteraction> interactions;

    [Header("Sizing & Quality")]
    public float simulationWorldHeight = 10f;
    public int verticalResolution = 240;

    [Header("Simulation Settings")]
    public float updateInterval = 0.05f;
    public int brushSize = 5;
    public float liquidShimmerAmount = 0.05f;

    [Header("Advanced Liquid Physics")]
    [Tooltip("How many times to run the liquid flow logic per frame. Higher is faster/smoother.")]
    public int liquidFlowPasses = 5;
    [Tooltip("The amount of money earned per particle placed.")]
    public float moneyPerParticle = 0.0001f;

    public static int currentBrushId = 1;
    private Vector2 lastMouseGridPosition = -Vector2.one;

    private Camera mainCamera;
    private SpriteRenderer screenRenderer;
    private int gridWidth, gridHeight;
    private Texture2D texture;
    private Color32[] colors;
    private int[,] grid;
    private Dictionary<int, ParticleData> particleDict = new Dictionary<int, ParticleData>();
    private float timeSinceLastUpdate = 0f;
    private Sprite _runtimeSprite;
    private bool[,] updatedThisFrame;

    void Awake()
    {
        if (autoConfigureScene)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) { GameObject camObj = new GameObject("Auto-Generated Camera"); mainCamera = camObj.AddComponent<Camera>(); camObj.tag = "MainCamera"; }
            mainCamera.orthographic = true; mainCamera.transform.position = new Vector3(0, 0, -10); mainCamera.backgroundColor = Color.black; mainCamera.clearFlags = CameraClearFlags.SolidColor; mainCamera.orthographicSize = simulationWorldHeight / 2.0f;
            GameObject screenObj = GameObject.Find("Auto-Generated Screen");
            if (screenObj == null) { screenObj = new GameObject("Auto-Generated Screen"); screenRenderer = screenObj.AddComponent<SpriteRenderer>(); } else { screenRenderer = screenObj.GetComponent<SpriteRenderer>(); }
            screenRenderer.transform.position = Vector3.zero; screenRenderer.transform.rotation = Quaternion.identity; screenRenderer.sprite = null;
        }

        screenRenderer.transform.localScale = Vector3.one;
        gridHeight = verticalResolution;
        gridWidth = Mathf.RoundToInt(verticalResolution * mainCamera.aspect);
        grid = new int[gridWidth, gridHeight];
        texture = new Texture2D(gridWidth, gridHeight);
        colors = new Color32[gridWidth * gridHeight];
        updatedThisFrame = new bool[gridWidth, gridHeight];

        numChunksX = Mathf.CeilToInt((float)gridWidth / CHUNK_SIZE);
        numChunksY = Mathf.CeilToInt((float)gridHeight / CHUNK_SIZE);
        chunks = new Chunk[numChunksX, numChunksY];
        for (int y = 0; y < numChunksY; y++) { for (int x = 0; x < numChunksX; x++) { chunks[x, y] = new Chunk(x, y); } }
    }

    void Start()
    {
        if (uiManager == null) { Debug.LogError("Simulation Manager is missing a link to UIManager!"); return; }

        texture.filterMode = FilterMode.Point;
        particleDict.Clear();
        foreach (var pType in particleTypes) { if (pType != null && !particleDict.ContainsKey(pType.id)) { particleDict.Add(pType.id, pType); } }
        particleDict.Add(0, null);
        ClearGrid();
        ApplyTexture();
    }

    void Update()
    {
        HandleMouseInput();
        timeSinceLastUpdate += Time.deltaTime;

        if (timeSinceLastUpdate >= updateInterval)
        {
            activeChunks.Clear();
            foreach (var chunk in chunksToActivateNextFrame) { activeChunks.Add(chunk); }
            chunksToActivateNextFrame.Clear();

            StepSimulation();
            for (int i = 0; i < liquidFlowPasses; i++) { StepLiquids(); }

            UpdateTexture();
            ApplyTexture();

            timeSinceLastUpdate = 0f;
        }
    }

    private void Paint(int gridX, int gridY, int particleId)
    {
        for (int x = -brushSize; x <= brushSize; x++)
        {
            for (int y = -brushSize; y <= brushSize; y++)
            {
                if (x * x + y * y > brushSize * brushSize) continue;
                int drawX = gridX + x;
                int drawY = gridY + y;
                if (drawX >= 0 && drawX < gridWidth && drawY >= 0 && drawY < gridHeight)
                {
                    if (particleId != 0 && grid[drawX, drawY] == 0)
                    {
                        // MODIFIED: Apply event multiplier
                        float moneyToAdd = moneyPerParticle;
                        if (eventManager != null && eventManager.isEventActive)
                        {
                            moneyToAdd *= eventManager.currentEvent.moneyMultiplier;
                        }
                        uiManager.AddMoney(moneyToAdd);
                    }

                    if (particleId == 0 || grid[drawX, drawY] == 0)
                    {
                        grid[drawX, drawY] = particleId;
                        WakeChunkAt(drawX, drawY);
                    }
                }
            }
        }
    }

    public List<string> GetParticleTypeNames()
    {
        List<string> names = new List<string>();
        foreach (var pType in particleTypes)
        {
            names.Add(pType.particleName.ToLower());
        }
        return names;
    }

    public void ConsoleClearAll(string particleNameToClear = null)
    {
        int idToClear = -1;
        if (!string.IsNullOrEmpty(particleNameToClear))
        {
            foreach (var pType in particleTypes)
            {
                if (pType.particleName.Equals(particleNameToClear, System.StringComparison.OrdinalIgnoreCase))
                {
                    idToClear = pType.id;
                    break;
                }
            }
            if (idToClear == -1)
            {
                uiManager.LogToConsole($"Console: Particle type '{particleNameToClear}' not found.");
                return;
            }
        }

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (idToClear == -1)
                {
                    if (grid[x, y] != 0)
                    {
                        grid[x, y] = 0;
                        WakeChunkAt(x, y);
                    }
                }
                else
                {
                    if (grid[x, y] == idToClear)
                    {
                        grid[x, y] = 0;
                        WakeChunkAt(x, y);
                    }
                }
            }
        }
        uiManager.LogToConsole(idToClear == -1 ? "Console: Cleared all particles." : $"Console: Cleared all '{particleNameToClear}' particles.");
    }

    public void ConsolePrintAllItems()
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (var pType in particleTypes)
        {
            counts.Add(pType.particleName, 0);
        }

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int id = grid[x, y];
                if (id != 0 && particleDict.TryGetValue(id, out var data))
                {
                    counts[data.particleName]++;
                }
            }
        }

        StringBuilder sb = new StringBuilder("--- Particle Counts ---\n");
        int total = 0;
        foreach (var pair in counts)
        {
            if (pair.Value > 0)
            {
                sb.AppendLine($"{pair.Key}: {pair.Value}");
                total += pair.Value;
            }
        }
        sb.AppendLine($"Total Particles: {total}");
        uiManager.LogToConsole(sb.ToString());
    }

    public void ConsoleHelp(UIManager uiManager)
    {
        StringBuilder sb = new StringBuilder("--- Available Commands ---\n");

        foreach (string command in uiManager.commandList)
        {
            sb.AppendLine(command);
        }

        uiManager.LogToConsole(sb.ToString());
    }

    public void ConsoleVersion()
    {
        uiManager.LogToConsole("Sandbox Simulation - Version 1.0.0");
    }

    private void WakeChunkAt(int gridX, int gridY)
    {
        if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight) return;
        int chunkX = gridX / CHUNK_SIZE;
        int chunkY = gridY / CHUNK_SIZE;
        for (int y = -1; y <= 1; y++) { for (int x = -1; x <= 1; x++) { int cX = chunkX + x; int cY = chunkY + y; if (cX >= 0 && cX < numChunksX && cY >= 0 && cY < numChunksY) { chunksToActivateNextFrame.Add(chunks[cX, cY]); } } }
    }

    void SwapCells(int x1, int y1, int x2, int y2)
    {
        int temp = grid[x1, y1]; grid[x1, y1] = grid[x2, y2]; grid[x2, y2] = temp; WakeChunkAt(x1, y1); WakeChunkAt(x2, y2);
    }

    void StepLiquids()
    {
        foreach (var chunk in activeChunks)
        {
            int startX = Mathf.Max(1, chunk.x * CHUNK_SIZE);
            int endX = Mathf.Min(gridWidth - 1, (chunk.x + 1) * CHUNK_SIZE);
            int startY = Mathf.Max(1, chunk.y * CHUNK_SIZE);
            int endY = Mathf.Min(gridHeight, (chunk.y + 1) * CHUNK_SIZE);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (updatedThisFrame[x, y]) continue;
                    int particleId = grid[x, y];
                    if (particleId == 0) continue;
                    if (!particleDict.TryGetValue(particleId, out ParticleData data) || data == null || !data.isLiquid) continue;

                    bool hasReacted = CheckAndPerformInteractions(x, y, data);
                    if (hasReacted)
                    {
                        updatedThisFrame[x, y] = true;
                        continue;
                    }

                    int dir = (Random.value > 0.5f) ? 1 : -1;
                    if (GetCell(x, y - 1) == 0)
                    {
                        SwapCells(x, y, x, y - 1);
                        updatedThisFrame[x, y - 1] = true;
                    }
                    else if (GetCell(x + dir, y - 1) == 0)
                    {
                        SwapCells(x, y, x + dir, y - 1);
                        updatedThisFrame[x + dir, y - 1] = true;
                    }
                    else if (GetCell(x - dir, y - 1) == 0)
                    {
                        SwapCells(x, y, x - dir, y - 1);
                        updatedThisFrame[x - dir, y - 1] = true;
                    }
                    else if (GetCell(x + dir, y) == 0)
                    {
                        SwapCells(x, y, x + dir, y);
                        updatedThisFrame[x + dir, y] = true;
                    }
                    else if (GetCell(x - dir, y) == 0)
                    {
                        SwapCells(x, y, x - dir, y);
                        updatedThisFrame[x - dir, y] = true;
                    }
                    else
                    {
                        updatedThisFrame[x, y] = true;
                    }
                }
            }
        }
    }

    void StepSimulation()
    {
        System.Array.Clear(updatedThisFrame, 0, updatedThisFrame.Length);
        var chunksToProcess = new List<Chunk>(activeChunks);
        foreach (var chunk in chunksToProcess)
        {
            int startX = chunk.x * CHUNK_SIZE;
            int endX = Mathf.Min(gridWidth, (chunk.x + 1) * CHUNK_SIZE);
            int startY = chunk.y * CHUNK_SIZE;
            int endY = Mathf.Min(gridHeight, (chunk.y + 1) * CHUNK_SIZE);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (updatedThisFrame[x, y]) continue;
                    int particleId = grid[x, y];
                    if (particleId == 0) continue;

                    if (!particleDict.TryGetValue(particleId, out ParticleData data) || data == null) continue;

                    if (data.isLiquid) continue;

                    bool hasReacted = CheckAndPerformInteractions(x, y, data);
                    if (hasReacted)
                    {
                        updatedThisFrame[x, y] = true;
                        continue;
                    }

                    bool moved = false;

                    if (data.isSolid && data.isGravityAffected)
                    {
                        int belowId = GetCell(x, y - 1);
                        if (belowId == 0)
                        {
                            SwapCells(x, y, x, y - 1);
                            updatedThisFrame[x, y - 1] = true;
                            moved = true;
                        }
                    }
                    else if (data.isGas)
                    {
                        int aboveId = GetCell(x, y + 1);
                        if (aboveId == 0)
                        {
                            SwapCells(x, y, x, y + 1);
                            updatedThisFrame[x, y + 1] = true;
                            moved = true;
                        }
                    }

                    if (!moved)
                    {
                        updatedThisFrame[x, y] = true;
                    }
                }
            }
        }
    }

    void UpdateTexture()
    {
        for (int y = 0; y < gridHeight; y++) { for (int x = 0; x < gridWidth; x++) { int particleId = grid[x, y]; if (particleDict.TryGetValue(particleId, out ParticleData data) && data != null) { if (data.isLiquid) { float shimmer = 1.0f + Random.Range(-liquidShimmerAmount, liquidShimmerAmount); Color finalColor = data.color * shimmer; finalColor.a = data.color.a; colors[y * gridWidth + x] = finalColor; } else { colors[y * gridWidth + x] = data.color; } } else { colors[y * gridWidth + x] = Color.black; } } }
    }
    private Vector2 GetGridPositionFromScreen(Vector2 screenPos)
    {
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(screenPos);
        float worldHeight = mainCamera.orthographicSize * 2.0f;
        float worldWidth = worldHeight * mainCamera.aspect;

        float normX = (worldPos.x + worldWidth / 2.0f) / worldWidth;
        float normY = (worldPos.y + worldHeight / 2.0f) / worldHeight;
        normX = Mathf.Clamp01(normX);
        normY = Mathf.Clamp01(normY);

        return new Vector2(Mathf.FloorToInt(normX * gridWidth), Mathf.FloorToInt(normY * gridHeight));
    }
    void HandleMouseInput()
    {
        // If we're over the UI, reset the last position to prevent drawing lines from the UI.
        if (UIManager.IsPointerOverUI)
        {
            lastMouseGridPosition = -Vector2.one;
            return;
        }

#if UNITY_ANDROID || UNITY_IOS
        // --- Touch-Specific Logic for Mobile ---
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            Vector2 currentMouseGridPos = GetGridPositionFromScreen(touch.position);

            // On a new touch, just paint a single dot. No lines.
            if (touch.phase == TouchPhase.Began)
            {
                Paint((int)currentMouseGridPos.x, (int)currentMouseGridPos.y, currentBrushId);
                lastMouseGridPosition = currentMouseGridPos;
            }
            // If dragging, now we draw a line.
            else if (touch.phase == TouchPhase.Moved)
            {
                if (lastMouseGridPosition != -Vector2.one)
                {
                    PaintLine((int)lastMouseGridPosition.x, (int)lastMouseGridPosition.y, (int)currentMouseGridPos.x, (int)currentMouseGridPos.y, currentBrushId);
                }
                lastMouseGridPosition = currentMouseGridPos;
            }
            // When the touch ends, reset everything.
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                lastMouseGridPosition = -Vector2.one;
            }
        }
#else
    // --- Mouse Logic for PC (now it's fixed too!) ---
    Vector2 currentMouseGridPos = GetGridPositionFromScreen(Input.mousePosition);

    // On the first frame of a click, just paint one spot and set the last position
    if (Input.GetMouseButtonDown(0))
    {
        Paint((int)currentMouseGridPos.x, (int)currentMouseGridPos.y, currentBrushId);
        lastMouseGridPosition = currentMouseGridPos;
    }
    else if (Input.GetMouseButtonDown(1))
    {
        Paint((int)currentMouseGridPos.x, (int)currentMouseGridPos.y, 0);
        lastMouseGridPosition = currentMouseGridPos;
    }
    // If the mouse is HELD, then we draw a line
    else if (Input.GetMouseButton(0))
    {
        if (lastMouseGridPosition != -Vector2.one)
        {
            PaintLine((int)lastMouseGridPosition.x, (int)lastMouseGridPosition.y, (int)currentMouseGridPos.x, (int)currentMouseGridPos.y, currentBrushId);
        }
        lastMouseGridPosition = currentMouseGridPos;
    }
    else if (Input.GetMouseButton(1))
    {
        if (lastMouseGridPosition != -Vector2.one)
        {
            PaintLine((int)lastMouseGridPosition.x, (int)lastMouseGridPosition.y, (int)currentMouseGridPos.x, (int)currentMouseGridPos.y, 0);
        }
        lastMouseGridPosition = currentMouseGridPos;
    }
    // If no button is held, reset
    else
    {
        lastMouseGridPosition = -Vector2.one;
    }
#endif

        // Handle keyboard shortcuts for brush selection (mostly for PC)
        for (int i = 0; i < particleTypes.Count; i++)
        {
            if (i < 9 && Input.GetKeyDown(KeyCode.Alpha1 + i)) { currentBrushId = particleTypes[i].id; }
        }
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.E)) { currentBrushId = 0; }
    }

    void PaintLine(int x0, int y0, int x1, int y1, int particleId)
    {
        int dx = Mathf.Abs(x1 - x0); int dy = -Mathf.Abs(y1 - y0); int sx = x0 < x1 ? 1 : -1; int sy = y0 < y1 ? 1 : -1; int err = dx + dy; while (true) { Paint(x0, y0, particleId); if (x0 == x1 && y0 == y1) break; int e2 = 2 * err; if (e2 >= dy) { err += dy; x0 += sx; } if (e2 <= dx) { err += dx; y0 += sy; } }
    }

    bool CheckAndPerformInteractions(int x, int y, ParticleData data)
    {
        for (int ny = -1; ny <= 1; ny++)
        {
            for (int nx = -1; nx <= 1; nx++)
            {
                if (nx == 0 && ny == 0) continue;
                int neighborX = x + nx;
                int neighborY = y + ny;
                if (neighborX < 0 || neighborX >= gridWidth || neighborY < 0 || neighborY >= gridHeight) continue;
                if (updatedThisFrame[neighborX, neighborY]) continue;
                int neighborId = grid[neighborX, neighborY];
                if (neighborId > 0)
                {
                    if (!particleDict.ContainsKey(neighborId)) continue;
                    ParticleData neighborData = particleDict[neighborId];

                    foreach (var interaction in interactions)
                    {
                        if ((data == interaction.particleA && neighborData == interaction.particleB) || (data == interaction.particleB && neighborData == interaction.particleA))
                        {
                            if (Random.value < interaction.chance)
                            {
                                bool isCurrentParticleA = (data == interaction.particleA);
                                int outcomeIdForCurrent = (isCurrentParticleA ? interaction.outcomeForA?.id : interaction.outcomeForB?.id) ?? 0;
                                int outcomeIdForNeighbor = (isCurrentParticleA ? interaction.outcomeForB?.id : interaction.outcomeForA?.id) ?? 0;

                                grid[x, y] = outcomeIdForCurrent;
                                grid[neighborX, neighborY] = outcomeIdForNeighbor;

                                updatedThisFrame[x, y] = true;
                                updatedThisFrame[neighborX, neighborY] = true;

                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    void ApplyTexture()
    {
        texture.SetPixels32(colors); texture.Apply(); if (_runtimeSprite != null) { Destroy(_runtimeSprite); }
        float pixelsPerUnit = gridHeight / simulationWorldHeight; _runtimeSprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit); screenRenderer.sprite = _runtimeSprite;
    }

    int GetCell(int x, int y)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) return -1; return grid[x, y];
    }

    void ClearGrid()
    {
        System.Array.Clear(grid, 0, grid.Length); chunksToActivateNextFrame.Clear(); activeChunks.Clear();
    }
}