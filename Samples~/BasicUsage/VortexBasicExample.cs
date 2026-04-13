using System.Collections.Generic;
using UnityEngine;
using Vortex.Analytics;

/// <summary>
/// Drop this onto any GameObject to see how Vortex Analytics works.
/// Requires an <see cref="AnalyticsManager"/> in the scene (Tools → Vortex Analytics → Add Analytics Manager).
/// </summary>
public class VortexBasicExample : MonoBehaviour
{
    private void Start()
    {
        // ── Simple event ─────────────────────────────────────────────────────
        AnalyticsManager.Instance.TrackEvent("level_start");

        // ── Event with a string value ─────────────────────────────────────────
        AnalyticsManager.Instance.TrackEvent("ui_opened", "settings_panel");

        // ── Event with structured properties ─────────────────────────────────
        AnalyticsManager.Instance.TrackEvent("level_start", new Dictionary<string, object>
        {
            { "level",      1        },
            { "difficulty", "normal" }
        });

        // ── Attach metadata to all subsequent events ──────────────────────────
        AnalyticsManager.Instance.SetCustomData(new Dictionary<string, object>
        {
            { "region",     "EU"  },
            { "premium",    false },
            { "user_level", 1     }
        });

        // ── Manual batch: queue multiple events, then flush together ──────────
        AnalyticsManager.Instance.BatchedTrackEvent("tutorial_step_1_seen");
        AnalyticsManager.Instance.BatchedTrackEvent("tutorial_step_2_seen");
        AnalyticsManager.Instance.FlushManualBatch();
    }

    // Called from a UI button, etc.
    public void OnPlayerDied()
    {
        AnalyticsManager.Instance.TrackEvent("player_death");
    }

    public void OnLevelComplete(int level, float completionTime)
    {
        AnalyticsManager.Instance.TrackEvent("level_complete", new Dictionary<string, object>
        {
            { "level", level           },
            { "time",  completionTime  }
        });
    }

    public void OnPurchase(string itemId, float price)
    {
        AnalyticsManager.Instance.TrackEvent("iap_purchased", new Dictionary<string, object>
        {
            { "item_id", itemId },
            { "price",   price  }
        });
    }
}
