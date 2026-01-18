# UI Patterns

The UI system uses the Presenter pattern for complex panels, separating view coordination from presentation logic, user actions, and event management.

## Architecture

```
UI Presenter Pattern (4-5 Components)
├── View (MonoBehaviour)     - UI creation, coordination
├── Presenter (Static)       - Data formatting, display logic
├── ActionHandler (Static)   - User action validation/execution
├── EventSubscriber (Class)  - Event subscription management
└── UIBuilder (Static)       - Optional: UI element creation
```

**Key Principles:**
- View coordinates, doesn't contain business logic
- Presenter/Handler are stateless (testable)
- EventSubscriber centralizes event management
- UIBuilder extracted when UI creation >150 lines

## When to Use

### Use UI Presenter Pattern When:

1. **Panel exceeds 500 lines** - Too large, multiple concerns mixed
2. **Multiple user actions** - 3+ button handlers with complex logic
3. **Complex display logic** - Formatting requires queries/calculations
4. **Interactive panel** - User performs actions (build, recruit, etc.)

### Don't Use When:

1. **Simple display (<200 lines)** - Static information only
2. **Single action** - Only one button, minimal logic
3. **Temporary/debug UI** - Not worth the overhead

## 4-Component Pattern

Use when UI creation <150 lines:

```
PanelView.cs (View)           - ~500 lines max
PanelPresenter.cs (Presenter) - Stateless display logic
PanelActionHandler.cs         - Stateless user actions
PanelEventSubscriber.cs       - Event management
```

### View

```csharp
public class ProvinceInfoPanel : MonoBehaviour
{
    private Label provinceNameLabel;
    private Button buildButton;
    private ProvinceEventSubscriber eventSubscriber;

    private void InitializeUI()
    {
        // Create UI elements (UI Toolkit)
        provinceNameLabel = CreateLabel("province-name");
        buildButton = CreateButton("build", OnBuildClicked);
    }

    public void Initialize(GameState gameState)
    {
        // Set up event subscriber
        eventSubscriber = new ProvinceEventSubscriber(gameState);
        eventSubscriber.OnProvinceChanged = RefreshPanel;
        eventSubscriber.Subscribe();
    }

    public void ShowProvince(ushort provinceId)
    {
        // DELEGATE: Format and display data
        ProvinceInfoPresenter.UpdatePanelData(
            provinceId, gameState, provinceNameLabel, ...);
    }

    private void OnBuildClicked()
    {
        // DELEGATE: Execute action
        if (ProvinceActionHandler.TryBuildBuilding(provinceId, buildingId, ...))
        {
            RefreshPanel();
        }
    }

    private void OnDestroy()
    {
        eventSubscriber?.Unsubscribe();
    }
}
```

### Presenter

```csharp
public static class ProvinceInfoPresenter
{
    // All methods static - stateless, testable
    public static void UpdatePanelData(
        ushort provinceId,
        GameState gameState,
        Label nameLabel,
        Label ownerLabel,
        Label developmentLabel)
    {
        // Query game state
        var province = gameState.Provinces.GetProvince(provinceId);
        var owner = gameState.Countries.GetCountry(province.OwnerID);

        // Format and update UI
        nameLabel.text = GetProvinceName(provinceId);
        ownerLabel.text = owner?.Name ?? "Unowned";
        developmentLabel.text = $"Development: {province.Development}";
    }

    public static void UpdateBuildingInfo(
        ushort provinceId,
        BuildingSystem buildingSystem,
        Label buildingsLabel,
        Button buildButton)
    {
        var buildings = buildingSystem.GetBuildings(provinceId);
        buildingsLabel.text = $"Buildings: {buildings.Count}";

        // Determine button visibility
        buildButton.SetEnabled(CanBuild(provinceId, buildingSystem));
    }
}
```

### ActionHandler

```csharp
public static class ProvinceActionHandler
{
    // All methods static - validate and execute
    public static bool TryBuildBuilding(
        ushort provinceId,
        uint buildingId,
        GameState gameState)
    {
        // Validate
        if (!CanAffordBuilding(buildingId, gameState))
            return false;

        if (!IsBuildingAllowed(provinceId, buildingId, gameState))
            return false;

        // Execute command
        gameState.ExecuteCommand(new BuildBuildingCommand
        {
            ProvinceId = provinceId,
            BuildingId = buildingId
        });

        return true;
    }

    public static bool TryRecruitUnit(
        ushort provinceId,
        ushort unitTypeId,
        GameState gameState)
    {
        // Validate and execute
        // ...
    }
}
```

### EventSubscriber

```csharp
public class ProvinceEventSubscriber
{
    private readonly GameState gameState;
    private CompositeDisposable subscriptions;

    // Callbacks set by view
    public Action OnProvinceChanged { get; set; }
    public Action OnBuildingCompleted { get; set; }

    public ProvinceEventSubscriber(GameState gameState)
    {
        this.gameState = gameState;
        subscriptions = new CompositeDisposable();
    }

    public void Subscribe()
    {
        subscriptions.Add(gameState.EventBus.Subscribe<ProvinceOwnerChangedEvent>(
            e => OnProvinceChanged?.Invoke()));

        subscriptions.Add(gameState.EventBus.Subscribe<BuildingCompletedEvent>(
            e => OnBuildingCompleted?.Invoke()));
    }

    public void Unsubscribe()
    {
        subscriptions.Dispose();
    }
}
```

## 5-Component Pattern

Use when UI creation >150 lines. Adds UIBuilder:

```
PanelView.cs           - ~40 lines UI setup (delegates to builder)
PanelPresenter.cs      - Display logic
PanelActionHandler.cs  - User actions
PanelEventSubscriber.cs - Events
PanelUIBuilder.cs      - UI element creation (~200+ lines)
```

### UIBuilder

```csharp
public static class CountryUIBuilder
{
    public struct UIElements
    {
        public VisualElement Root;
        public Label NameLabel;
        public Label ProvinceCountLabel;
        public Button DeclareWarButton;
        // ... all UI elements
    }

    public struct StyleConfig
    {
        public Color BackgroundColor;
        public int Padding;
        public int FontSize;
    }

    public static UIElements BuildUI(
        StyleConfig config,
        Action onDeclareWarClicked,
        Action onAllianceClicked)
    {
        var elements = new UIElements();

        // Create root container
        elements.Root = new VisualElement();
        elements.Root.style.backgroundColor = config.BackgroundColor;
        elements.Root.style.padding = config.Padding;

        // Create header section
        elements.NameLabel = new Label();
        elements.NameLabel.style.fontSize = config.FontSize;
        elements.Root.Add(elements.NameLabel);

        // Create province count
        elements.ProvinceCountLabel = new Label();
        elements.Root.Add(elements.ProvinceCountLabel);

        // Create buttons
        elements.DeclareWarButton = new Button(onDeclareWarClicked);
        elements.DeclareWarButton.text = "Declare War";
        elements.Root.Add(elements.DeclareWarButton);

        // ... more UI creation

        return elements;
    }
}
```

### View with UIBuilder

```csharp
public class CountryInfoPanel : MonoBehaviour
{
    private CountryUIBuilder.UIElements ui;

    private void InitializeUI()
    {
        // Delegate UI creation to builder (~40 lines vs ~150+ inline)
        var config = new CountryUIBuilder.StyleConfig
        {
            BackgroundColor = Color.black,
            Padding = 10,
            FontSize = 14
        };

        ui = CountryUIBuilder.BuildUI(
            config,
            onDeclareWarClicked: OnDeclareWarClicked,
            onAllianceClicked: OnAllianceClicked
        );

        rootVisualElement.Add(ui.Root);
    }

    public void ShowCountry(ushort countryId)
    {
        // DELEGATE: Update display
        CountryInfoPresenter.UpdateCountryData(
            countryId, gameState,
            ui.NameLabel, ui.ProvinceCountLabel, ...);
    }
}
```

## Adding New Features

### Adding New Action

```csharp
// 1. Add method to ActionHandler
public static bool TryNewAction(params) { ... }

// 2. Add button in View.InitializeUI()
newButton = CreateButton("new", OnNewClicked);

// 3. Add click handler in View
private void OnNewClicked()
{
    if (PanelActionHandler.TryNewAction(...))
        RefreshPanel();
}
// View stays ~200 lines, handler grows linearly
```

### Adding New Display Field

```csharp
// 1. Add label in View.InitializeUI()
newLabel = CreateLabel("new-field");

// 2. Add update in Presenter
public static void UpdateNewField(data, label) { ... }

// 3. Call from View.RefreshPanel()
Presenter.UpdateNewField(data, newLabel);
```

### Adding New Event

```csharp
// 1. Add callback property in EventSubscriber
public Action OnNewEvent { get; set; }

// 2. Add subscription in Subscribe()
subscriptions.Add(eventBus.Subscribe<NewEvent>(
    e => OnNewEvent?.Invoke()));

// 3. Set callback in View.Initialize()
subscriber.OnNewEvent = HandleNewEvent;
```

## Pattern Selection

| Condition | Pattern |
|-----------|---------|
| Panel <200 lines | No pattern needed |
| Panel 200-500 lines | Consider pattern |
| Panel >500 lines | Use 4-component |
| UI creation >150 lines | Use 5-component |
| 3+ user actions | Use pattern |
| Complex display logic | Use pattern |

## Benefits

- **Testability** - Stateless presenters/handlers easy to test
- **Scalability** - Adding features doesn't bloat view
- **Maintainability** - Clear separation of concerns
- **Debugging** - Easy to find relevant code

## File Size Guidelines

| Component | Target Size |
|-----------|-------------|
| View | <500 lines |
| Presenter | <400 lines |
| ActionHandler | <300 lines |
| EventSubscriber | <200 lines |
| UIBuilder | <300 lines |

## API Reference

- UI Toolkit documentation
- EventBus subscription patterns
- Command pattern for actions
