# Vortex Analytics — Unity SDK

Unity SDK for [Vortex Analytics](https://vortexanalytics.io). Supports immediate and batched event tracking, automatic session management, and graceful flush on app quit.

**Minimum Unity version:** 2021.3  
**Package name:** `io.vortexanalytics.unity-sdk`

---

## Installation

### Option 1 — Git URL (recommended)

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL…**
3. Enter:
   ```
   https://github.com/Vortex-Analytics-IO/Unity-SDK.git
   ```

### Option 2 — Local disk

1. Clone or download this repository
2. Open **Window → Package Manager**
3. Click **+** → **Add package from disk…**
4. Select the `package.json` at the root of this repo

> **Note:** `Newtonsoft.Json` (`com.unity.nuget.newtonsoft-json ≥ 3.2.1`) is listed as a dependency and will be installed automatically when using either method above.

---

## Quick Start

1. Open **Tools → Vortex Analytics → Add Analytics Manager to Scene**  
   *(or add the `AnalyticsManager` component to any persistent GameObject manually)*
2. In the Inspector, fill in **Tenant ID** and **Platform**
3. Press Play — the SDK validates your tenant and starts tracking

---

## Initialization

### Automatic Initialization (Recommended)

Attach the `AnalyticsManager` component to a GameObject and configure:

- **Initialize On Awake**: If true, `Initialize()` is called automatically on startup.
- **Enable Analytics**: Master switch. If false, the system initializes but **does not send any data** (all tracking calls are ignored).
- **Tenant ID**: Your unique project identifier.
- **Url**: The endpoint URL (default: `https://in.vortexanalytics.io`).
- **Platform**: The platform string (e.g., "STEAM", "IOS").

When `Initialize On Start` is enabled, the system initializes automatically during `Awake()`.

### Manual Initialization

If you need to initialize at runtime (e.g. after login or consent flow):

```csharp
using Vortex.Analytics;

AnalyticsManager.Instance.Init(
    tenantId: "mygame",
    url: "https://in.vortexanalytics.io",
    platform: "STEAM"
);
```

> Disable **Initialize On Awake** in the Inspector when using this path.

### Internal Behavior

On initialization, the system:
1. Generates or loads a persistent device identifier
2. Creates a new session ID
3. Performs a server health check
4. Enables or disables analytics based on server availability

If the server is unreachable, events are safely queued until connectivity is restored.

## Runtime Control
You can enable or disable analytics at runtime (e.g., for GDPR consent or user opt-out options).

### Toggling Analytics

```csharp
// Disable analytics (stops all tracking and background routines)
AnalyticsManager.Instance.SetAnalyticsEnabled(false);

// Enable analytics (restarts health checks and flushing routines)
AnalyticsManager.Instance.SetAnalyticsEnabled(true);
```

### Behavior when disabled:

- All TrackEvent and BatchedTrackEvent calls are ignored immediately.
- Background flush routines are stopped to save resources.
- Server health checks are paused.

## Tracking Events

### Simple Event

```csharp
AnalyticsManager.Instance.TrackEvent("app_started");
```

### Event with String Payload

```csharp
AnalyticsManager.Instance.TrackEvent("menu_opened", "settings");
```

### Event with Structured Data

```csharp
AnalyticsManager.Instance.TrackEvent("level_completed", new Dictionary<string, object>
{
    { "level", 5 },
    { "difficulty", "Hard" },
    { "time", 123.4f }
});
```

### Manual Batching
Manual batching allows you to explicitly control when analytics events are sent.

#### Add Events to Batch

```csharp
AnalyticsManager.Instance.BatchedTrackEvent("EnemyKilled");

AnalyticsManager.Instance.BatchedTrackEvent(
    "ItemCrafted",
    new Dictionary<string, object>
    {
        { "item", "MagicSword" },
        { "rarity", "Epic" }
    }
);
```

#### Send Batched Events

```csharp
AnalyticsManager.Instance.FlushManualBatch();
```

All queued events will be sent in a single request.

## Automatic Batching

When Auto Batching is enabled:

- Events are queued automatically
- The system sends batches every Auto Flush Interval seconds

If the server is unreachable, events remain queued.

## Custom Data

Custom data allows you to attach a JSON object to **every** analytics event sent by the manager.

### Setting Custom Data

```csharp
AnalyticsManager.Instance.SetCustomData(new Dictionary<string, object>
{
    { "region", "EU" },
    { "premium", true },
    { "user_level", 10 }
});
```

The custom data is automatically included in all subsequent events (both immediate and batched).

### Clearing Custom Data

To remove the custom data:

```csharp
AnalyticsManager.Instance.ClearCustomData();
```

### Behavior

- **Empty custom data** is not sent in requests (to minimize payload size)
- **Non-empty custom data** is included in every tracking event
- Changing custom data affects only **new** events; previously sent events are not modified

## Namespace

All public types live in the `Vortex.Analytics` namespace. Add the following `using` directive at the top of any script that references the SDK:

```csharp
using Vortex.Analytics;
```

## Lifecycle Handling

Analytics are flushed automatically when:
- The application loses focus (mobile background)
- The application is paused (mobile)
- The application is quitting

This ensures minimal data loss.

---

## Samples

A **Basic Usage** sample is bundled with the package.  
Import it via **Window → Package Manager → Vortex Analytics → Samples → Basic Usage → Import**.

---

## License

[MIT](LICENSE)