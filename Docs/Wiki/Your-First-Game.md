# Your First Game with Archon Engine

This tutorial walks you through creating a minimal grand strategy game from scratch. By the end, you'll have:

- Country selection screen
- Clickable map with province info
- Simple economy (gold per province)
- Building construction

**Time estimate**: 1-2 hours

## Prerequisites

- Completed [Getting Started](Getting-Started.md)
- Archon Engine set up in your Unity project
- A working map (use the StarterKit map or your own)

## Step 1: Create Your Game Folder

Create the following structure:

```
Assets/Game/
├── MyGameInitializer.cs
├── Systems/
├── Commands/
├── UI/
└── Data/
```

## Step 2: Create the Initializer

The Initializer is your game's entry point. Create `Assets/Game/MyGameInitializer.cs`:

```csharp
using UnityEngine;
using System.Collections;
using Core;
using Engine;

namespace MyGame
{
    public class MyGameInitializer : MonoBehaviour
    {
        public static MyGameInitializer Instance { get; private set; }

        // Your systems (plain C# classes, not MonoBehaviours)
        private PlayerState playerState;
        private EconomySystem economySystem;

        public PlayerState PlayerState => playerState;
        public EconomySystem EconomySystem => economySystem;

        public bool IsInitialized { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        IEnumerator Start()
        {
            Debug.Log("MyGame: Waiting for ENGINE...");

            // Wait for ArchonEngine to initialize
            while (ArchonEngine.Instance == null || !ArchonEngine.Instance.IsInitialized)
                yield return null;

            Debug.Log("MyGame: ENGINE ready, initializing game systems...");

            // Get the central GameState from ArchonEngine
            var gameState = ArchonEngine.Instance.GameState;

            // Create your systems
            playerState = new PlayerState(gameState);
            economySystem = new EconomySystem(gameState, playerState);

            // Subscribe to time events for economy
            gameState.EventBus.Subscribe<MonthlyTickEvent>(economySystem.OnMonthlyTick);

            IsInitialized = true;
            Debug.Log("MyGame: Initialization complete!");
        }

        void OnDestroy()
        {
            economySystem?.Dispose();
            if (Instance == this)
                Instance = null;
        }
    }
}
```

## Step 3: Create PlayerState

Create `Assets/Game/Systems/PlayerState.cs`:

```csharp
using Core;
using UnityEngine;

namespace MyGame
{
    /// <summary>
    /// Tracks which country the player controls.
    /// </summary>
    public class PlayerState
    {
        private readonly GameState gameState;

        public ushort PlayerCountryId { get; private set; }
        public bool HasSelectedCountry => PlayerCountryId != 0;

        public PlayerState(GameState gameState)
        {
            this.gameState = gameState;
        }

        public void SelectCountry(ushort countryId)
        {
            if (countryId == 0)
            {
                Debug.LogWarning("Cannot select country ID 0");
                return;
            }

            PlayerCountryId = countryId;

            // Notify other systems
            gameState.EventBus.Emit(new PlayerCountrySelectedEvent
            {
                CountryId = countryId
            });

            string tag = gameState.CountryQueries.GetTag(countryId);
            Debug.Log($"Player selected: {tag}");
        }

        public bool IsPlayerCountry(ushort countryId)
        {
            return HasSelectedCountry && countryId == PlayerCountryId;
        }
    }

    /// <summary>
    /// Event emitted when player selects their country.
    /// </summary>
    public struct PlayerCountrySelectedEvent : IGameEvent
    {
        public ushort CountryId;
        public float TimeStamp { get; set; }
    }
}
```

## Step 4: Create EconomySystem

Create `Assets/Game/Systems/EconomySystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using Core;
using Core.Data;
using Core.Events;

namespace MyGame
{
    /// <summary>
    /// Simple economy: 1 gold per province per month.
    /// </summary>
    public class EconomySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;

        // Gold storage per country (using FixedPoint64 for determinism)
        private Dictionary<ushort, FixedPoint64> countryGold = new();

        public EconomySystem(GameState gameState, PlayerState playerState)
        {
            this.gameState = gameState;
            this.playerState = playerState;
        }

        /// <summary>
        /// Get gold for a country.
        /// </summary>
        public int GetGold(ushort countryId)
        {
            return countryGold.TryGetValue(countryId, out var gold)
                ? gold.ToInt()
                : 0;
        }

        /// <summary>
        /// Get player's gold (convenience method).
        /// </summary>
        public int PlayerGold => GetGold(playerState.PlayerCountryId);

        /// <summary>
        /// Called each month to collect income.
        /// </summary>
        public void OnMonthlyTick(MonthlyTickEvent evt)
        {
            CollectIncomeForAllCountries();
        }

        private void CollectIncomeForAllCountries()
        {
            // Get all countries
            var countries = gameState.Countries.GetAllCountryIds();
            try
            {
                foreach (ushort countryId in countries)
                {
                    int provinceCount = gameState.ProvinceQueries
                        .GetCountryProvinceCount(countryId);

                    if (provinceCount > 0)
                    {
                        // 1 gold per province
                        FixedPoint64 income = FixedPoint64.FromInt(provinceCount);
                        AddGold(countryId, income);
                    }
                }
            }
            finally
            {
                countries.Dispose(); // NativeArray must be disposed
            }
        }

        public void AddGold(ushort countryId, FixedPoint64 amount)
        {
            if (!countryGold.ContainsKey(countryId))
                countryGold[countryId] = FixedPoint64.Zero;

            FixedPoint64 oldGold = countryGold[countryId];
            countryGold[countryId] = oldGold + amount;

            // Emit event for UI updates
            gameState.EventBus.Emit(new GoldChangedEvent
            {
                CountryId = countryId,
                OldValue = oldGold.ToInt(),
                NewValue = countryGold[countryId].ToInt()
            });
        }

        public bool TrySpendGold(ushort countryId, int amount)
        {
            int current = GetGold(countryId);
            if (current < amount)
                return false;

            AddGold(countryId, FixedPoint64.FromInt(-amount));
            return true;
        }

        public void Dispose()
        {
            countryGold.Clear();
        }
    }

    /// <summary>
    /// Event emitted when a country's gold changes.
    /// </summary>
    public struct GoldChangedEvent : IGameEvent
    {
        public ushort CountryId;
        public int OldValue;
        public int NewValue;
        public float TimeStamp { get; set; }
    }
}
```

## Step 5: Create Country Selection UI

Create `Assets/Game/UI/CountrySelectionUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Map.Interaction;

namespace MyGame.UI
{
    /// <summary>
    /// Simple country selection: click a province to select its owner.
    /// </summary>
    public class CountrySelectionUI : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private GameState gameState;
        private PlayerState playerState;
        private ProvinceSelector provinceSelector;

        private Label promptLabel;
        private VisualElement panel;

        public void Initialize(GameState gameState, PlayerState playerState,
            ProvinceSelector provinceSelector)
        {
            this.gameState = gameState;
            this.playerState = playerState;
            this.provinceSelector = provinceSelector;

            SetupUI();
            Show();

            // Listen for province clicks
            provinceSelector.OnProvinceClicked += OnProvinceClicked;
        }

        void SetupUI()
        {
            var root = uiDocument.rootVisualElement;

            panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = 100;
            panel.style.left = 100;
            panel.style.backgroundColor = new Color(0, 0, 0, 0.8f);
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            panel.style.paddingLeft = 30;
            panel.style.paddingRight = 30;

            promptLabel = new Label("Click a province to select your country");
            promptLabel.style.color = Color.white;
            promptLabel.style.fontSize = 24;

            panel.Add(promptLabel);
            root.Add(panel);
        }

        void OnProvinceClicked(ushort provinceId)
        {
            if (playerState.HasSelectedCountry)
                return; // Already selected

            // Get the owner of the clicked province
            var state = gameState.Provinces.GetState(provinceId);
            ushort countryId = state.ownerID;

            if (countryId == 0)
            {
                promptLabel.text = "That province has no owner. Click another.";
                return;
            }

            // Select this country
            playerState.SelectCountry(countryId);
            Hide();
        }

        public void Show() => panel.style.display = DisplayStyle.Flex;
        public void Hide() => panel.style.display = DisplayStyle.None;

        void OnDestroy()
        {
            if (provinceSelector != null)
                provinceSelector.OnProvinceClicked -= OnProvinceClicked;
        }
    }
}
```

## Step 6: Create Resource Bar UI

Create `Assets/Game/UI/ResourceBarUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;
using Core;

namespace MyGame.UI
{
    /// <summary>
    /// Shows player's gold at the top of the screen.
    /// </summary>
    public class ResourceBarUI : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private GameState gameState;
        private PlayerState playerState;
        private EconomySystem economySystem;
        private CompositeDisposable subscriptions = new();

        private Label goldLabel;
        private VisualElement panel;

        public void Initialize(GameState gameState, PlayerState playerState,
            EconomySystem economySystem)
        {
            this.gameState = gameState;
            this.playerState = playerState;
            this.economySystem = economySystem;

            SetupUI();

            // Subscribe to gold changes
            subscriptions.Add(
                gameState.EventBus.Subscribe<GoldChangedEvent>(OnGoldChanged)
            );

            // Subscribe to country selection
            subscriptions.Add(
                gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(OnCountrySelected)
            );

            // Hide until country is selected
            Hide();
        }

        void SetupUI()
        {
            var root = uiDocument.rootVisualElement;

            panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = 10;
            panel.style.right = 10;
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.backgroundColor = new Color(0, 0, 0, 0.7f);
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;

            goldLabel = new Label("Gold: 0");
            goldLabel.style.color = new Color(1f, 0.84f, 0f); // Gold color
            goldLabel.style.fontSize = 18;

            panel.Add(goldLabel);
            root.Add(panel);
        }

        void OnCountrySelected(PlayerCountrySelectedEvent evt)
        {
            Show();
            RefreshDisplay();
        }

        void OnGoldChanged(GoldChangedEvent evt)
        {
            // Only update if it's the player's gold
            if (evt.CountryId == playerState.PlayerCountryId)
                RefreshDisplay();
        }

        void RefreshDisplay()
        {
            goldLabel.text = $"Gold: {economySystem.PlayerGold}";
        }

        public void Show() => panel.style.display = DisplayStyle.Flex;
        public void Hide() => panel.style.display = DisplayStyle.None;

        void OnDestroy()
        {
            subscriptions.Dispose();
        }
    }
}
```

## Step 7: Create Province Info UI

Create `Assets/Game/UI/ProvinceInfoUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Map.Interaction;

namespace MyGame.UI
{
    /// <summary>
    /// Shows info about the selected province.
    /// </summary>
    public class ProvinceInfoUI : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private GameState gameState;
        private PlayerState playerState;
        private ProvinceSelector provinceSelector;

        private VisualElement panel;
        private Label nameLabel;
        private Label ownerLabel;
        private Label developmentLabel;

        public void Initialize(GameState gameState, PlayerState playerState,
            ProvinceSelector provinceSelector)
        {
            this.gameState = gameState;
            this.playerState = playerState;
            this.provinceSelector = provinceSelector;

            SetupUI();
            Hide();

            // Only enable after country selection
            gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(evt =>
            {
                provinceSelector.OnProvinceClicked += OnProvinceClicked;
            });
        }

        void SetupUI()
        {
            var root = uiDocument.rootVisualElement;

            panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.bottom = 20;
            panel.style.left = 20;
            panel.style.width = 250;
            panel.style.backgroundColor = new Color(0, 0, 0, 0.8f);
            panel.style.paddingTop = 15;
            panel.style.paddingBottom = 15;
            panel.style.paddingLeft = 15;
            panel.style.paddingRight = 15;

            nameLabel = new Label("Province Name");
            nameLabel.style.color = Color.white;
            nameLabel.style.fontSize = 20;
            nameLabel.style.marginBottom = 10;

            ownerLabel = new Label("Owner: ---");
            ownerLabel.style.color = Color.white;

            developmentLabel = new Label("Development: 0");
            developmentLabel.style.color = Color.white;

            panel.Add(nameLabel);
            panel.Add(ownerLabel);
            panel.Add(developmentLabel);
            root.Add(panel);
        }

        void OnProvinceClicked(ushort provinceId)
        {
            if (!playerState.HasSelectedCountry)
                return;

            ShowProvince(provinceId);
        }

        void ShowProvince(ushort provinceId)
        {
            var state = gameState.Provinces.GetState(provinceId);
            string provinceName = gameState.ProvinceQueries.GetName(provinceId);
            string ownerTag = state.ownerID > 0
                ? gameState.CountryQueries.GetTag(state.ownerID)
                : "Unowned";

            nameLabel.text = provinceName;
            ownerLabel.text = $"Owner: {ownerTag}";
            developmentLabel.text = $"Development: {state.development}";

            Show();
        }

        public void Show() => panel.style.display = DisplayStyle.Flex;
        public void Hide() => panel.style.display = DisplayStyle.None;
    }
}
```

## Step 8: Set Up the Scene

1. **Drag ArchonEngine prefab** into your scene from `Assets/Archon-Engine/Prefabs/ArchonEngine.prefab`
2. **Create a Map Quad** - GameObject with MeshRenderer using one of the Archon map shaders/materials
3. **Assign references** on ArchonEngine:
   - Set `Map Mesh Renderer` to your map quad's MeshRenderer
   - Optionally set `Map Camera` to your camera
4. **Add MyGameInitializer** component to a new GameObject
5. **Add UIDocument** component for each UI panel
6. **Wire up UI references** in the Inspector

Or copy the StarterKit scene (`Assets/Archon-Engine/Scenes/StarterKit.unity`) and modify it.

## Step 9: Test Your Game

1. Press Play in Unity
2. Wait for "MyGame: Initialization complete!" in console
3. Click a province to select your country
4. Watch gold accumulate each month
5. Click provinces to see info

## What You've Built

You now have a minimal grand strategy game with:

- ✅ ENGINE integration (waits for initialization)
- ✅ Player country selection
- ✅ Event-driven communication (EventBus)
- ✅ Deterministic economy (FixedPoint64)
- ✅ UI that reacts to game events

## Next Steps

Extend your game with:

1. **Buildings** - See [Cookbook: Add a Building Type](Cookbook.md#add-a-building-type)
2. **Units** - See [Cookbook: Create a Unit System](Cookbook.md#create-a-unit-system)
3. **Custom Map Modes** - See [Cookbook: Add a Map Mode](Cookbook.md#add-a-map-mode)
4. **AI** - See [Cookbook: Add AI Goals](Cookbook.md#add-ai-goals)

## Full Example

For a complete implementation, study the **StarterKit**:
- `Assets/Archon-Engine/Scripts/StarterKit/`

It includes everything above plus: buildings, units, AI, save/load, and more.
