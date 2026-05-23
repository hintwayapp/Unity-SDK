using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace Hintway
{
    /// <summary>
    /// Singleton MonoBehaviour that tracks analytics events and forwards them to the Hintway backend.
    /// Add it to a persistent GameObject in your first scene and configure Tenant ID, URL, and Platform
    /// in the Inspector, or call <see cref="Init"/> at runtime.
    /// </summary>
    public class AnalyticsManager : MonoBehaviour
    {
        [System.Serializable]
        public class ValueWrapper { public string data; }

        [System.Serializable]
        public class TrackingData
        {
            public string name;
            public string value;
            public string identity;
            public string session_id;
            public string platform;
            public string app_version;
            public string custom;
            public string timestamp;
        }

        [System.Serializable]
        public class Tracking
        {
            public string tenant_id;
            public TrackingData tracking;
        }

        [System.Serializable]
        public class BatchedTracks
        {
            public List<Tracking> tracks = new List<Tracking>();
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Singleton
        // ──────────────────────────────────────────────────────────────────────────

        public static AnalyticsManager Instance { get; private set; }

        // ──────────────────────────────────────────────────────────────────────────
        //  Inspector fields
        // ──────────────────────────────────────────────────────────────────────────

        [Header("Initialisation")]
        [SerializeField] private bool _initializeOnAwake = true;
        [SerializeField] private bool _enableAnalytics = true;
        [SerializeField] private bool _verbose = false;

        [Header("Connection")]
        [SerializeField] private string _tenantId;
        [SerializeField] private string _url = "https://in.hintway.app";
        [SerializeField] private string _platform;

        [Header("Auto-Flush Settings")]
        [Tooltip("Queue all events until a manual Flush() or the timed flush fires.")]
        [SerializeField] private bool _autoBatching = false;
        [SerializeField] private float _autoFlushInterval = 10f;

        // ──────────────────────────────────────────────────────────────────────────
        //  Private state
        // ──────────────────────────────────────────────────────────────────────────

        private string _identity;
        private string _sessionId;
        private string _appVersion;
        private string _customData = "";

        private bool _isServerChecked;
        private bool _serverAlive;
        private bool _initialized;

        private bool _isQuitting;
        private bool _flushCompleted;

        private readonly List<Tracking> _internalQueue = new List<Tracking>();
        private BatchedTracks _manualBatchedTracks = new BatchedTracks();

        private readonly object _lock = new object();
        private Coroutine _autoFlushRoutine;

        // ──────────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.wantsToQuit += OnWantsToQuit;

            if (_initializeOnAwake)
                Initialize();
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Initialisation
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Programmatic initialisation. Call this if <c>Initialize On Awake</c> is disabled
        /// or when credentials become available at runtime (e.g. after login).
        /// </summary>
        public void Init(string tenantId, string url, string platform)
        {
            if (_initialized) return;

            _tenantId = tenantId;
            _url = url;
            _platform = platform;

            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            _initialized = true;
            InitSession();

            if (_enableAnalytics)
                StartCoroutine(CheckServerAvailability());

            TrackEvent("app_started");
        }

        private void InitSession()
        {
            _identity = PlayerPrefs.GetString("device_identity", Guid.NewGuid().ToString());
            PlayerPrefs.SetString("device_identity", _identity);
            _sessionId = Guid.NewGuid().ToString();
            _appVersion = Application.version;
            HintwayLog("Session initialised — Identity: {0}, SessionId: {1}, AppVersion: {2}",
                _identity, _sessionId, _appVersion);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Runtime control
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>Enables or disables all analytics tracking at runtime.</summary>
        public void SetAnalyticsEnabled(bool enabled)
        {
            _enableAnalytics = enabled;

            if (!enabled)
            {
                if (_autoFlushRoutine != null)
                {
                    StopCoroutine(_autoFlushRoutine);
                    _autoFlushRoutine = null;
                }
            }
            else
            {
                if (!_isServerChecked)
                    StartCoroutine(CheckServerAvailability());
                else if (_serverAlive && _autoBatching && _autoFlushRoutine == null)
                    _autoFlushRoutine = StartCoroutine(AutoFlushRoutine());
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Custom data
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attaches a JSON payload to every subsequent event.
        /// Pass <c>null</c> or an empty dictionary to clear.
        /// </summary>
        public void SetCustomData(Dictionary<string, object> customData)
        {
            if (customData == null || customData.Count == 0)
            {
                _customData = "";
                return;
            }

            _customData = JsonConvert.SerializeObject(customData);
        }

        /// <summary>Removes any previously set custom data.</summary>
        public void ClearCustomData()
        {
            _customData = "";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Public tracking API
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>Tracks a named event with an optional string payload.</summary>
        public void TrackEvent(string eventName, string props = "")
        {
            if (!_enableAnalytics) return;
            if (!_serverAlive && _isServerChecked && !_autoBatching) return;

            string wrapped = string.IsNullOrEmpty(props)
                ? ""
                : JsonConvert.SerializeObject(new ValueWrapper { data = props });

            ProcessTrackEvent(eventName, wrapped);
        }

        /// <summary>Tracks a named event with a structured property dictionary.</summary>
        public void TrackEvent(string eventName, Dictionary<string, object> props)
        {
            if (!_enableAnalytics) return;
            if (!_serverAlive && _isServerChecked && !_autoBatching) return;

            ProcessTrackEvent(eventName, JsonConvert.SerializeObject(props));
        }

        /// <summary>Adds an event to the manual batch queue. Call <see cref="FlushManualBatch"/> to send.</summary>
        public void BatchedTrackEvent(string eventName, string props = "")
        {
            if (!_enableAnalytics) return;
            if (!_serverAlive) return;

            lock (_lock)
                _manualBatchedTracks.tracks.Add(CreateTracking(eventName, props));
        }

        /// <summary>Adds an event with structured properties to the manual batch queue.</summary>
        public void BatchedTrackEvent(string eventName, Dictionary<string, object> props)
        {
            if (!_enableAnalytics) return;
            if (!_serverAlive) return;

            lock (_lock)
                _manualBatchedTracks.tracks.Add(CreateTracking(eventName, JsonConvert.SerializeObject(props)));
        }

        /// <summary>Sends all events queued via <see cref="BatchedTrackEvent"/> in a single request.</summary>
        public void FlushManualBatch()
        {
            if (!_enableAnalytics) return;
            StartCoroutine(PostBatchRoutine());
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Internal helpers
        // ──────────────────────────────────────────────────────────────────────────

        private void ProcessTrackEvent(string eventName, string value)
        {
            Tracking tracking = CreateTracking(eventName, value);
            lock (_lock)
            {
                if (!_isServerChecked || _autoBatching)
                    _internalQueue.Add(tracking);
                else
                    StartCoroutine(PostSingle(tracking));
            }
        }

        private Tracking CreateTracking(string name, string value)
        {
            var data = new TrackingData
            {
                name = name,
                value = value,
                identity = _identity,
                session_id = _sessionId,
                platform = _platform,
                app_version = _appVersion,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            if (!string.IsNullOrEmpty(_customData))
                data.custom = _customData;

            return new Tracking { tenant_id = _tenantId, tracking = data };
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Networking
        // ──────────────────────────────────────────────────────────────────────────

        private IEnumerator CheckServerAvailability()
        {
            if (string.IsNullOrEmpty(_url)) yield break;

            if (string.IsNullOrEmpty(_tenantId))
            {
                HintwayLog("Tenant ID is not set. Analytics disabled.");
                _serverAlive = false;
                _isServerChecked = true;
                yield break;
            }

            string validateUrl = _url + "/validate?tenant_id=" + UnityWebRequest.EscapeURL(_tenantId);
            HintwayLog("Validating tenant at {0}", validateUrl);

            using UnityWebRequest request = UnityWebRequest.Get(validateUrl);
            request.timeout = 5;
            yield return request.SendWebRequest();

            _serverAlive = request.result == UnityWebRequest.Result.Success;
            bool tenantValid = _serverAlive && request.responseCode == 200;
            _isServerChecked = true;

            if (!_serverAlive)
            {
                HintwayLog("Server unreachable. Analytics disabled.");
                StopAutoFlush();
                yield break;
            }

            if (!tenantValid)
            {
                HintwayLog("Tenant '{0}' invalid or unauthorised (HTTP {1}). Analytics disabled.",
                    _tenantId, request.responseCode);
                _serverAlive = false;
                StopAutoFlush();
                yield break;
            }

            HintwayLog("Tenant '{0}' validated successfully.", _tenantId);

            if (_autoBatching && _autoFlushRoutine == null)
                _autoFlushRoutine = StartCoroutine(AutoFlushRoutine());

            if (!_autoBatching && _internalQueue.Count > 0)
                StartCoroutine(FlushInternalQueue());
        }

        private IEnumerator PostSingle(Tracking tracking)
        {
            string json = JsonConvert.SerializeObject(tracking);
            using UnityWebRequest request = CreateRequest(_url + "/track", json);
            yield return request.SendWebRequest();

            LogRequestResult(request);
        }

        private IEnumerator PostBatchRoutine()
        {
            if (_manualBatchedTracks.tracks.Count == 0) yield break;

            string json;
            lock (_lock)
            {
                json = JsonConvert.SerializeObject(_manualBatchedTracks);
                _manualBatchedTracks.tracks.Clear();
            }

            HintwayLog("Posting manual batch — {0} events",
                JsonConvert.DeserializeObject<BatchedTracks>(json)?.tracks?.Count ?? 0);

            using UnityWebRequest request = CreateRequest(_url + "/batch", json);
            yield return request.SendWebRequest();

            LogRequestResult(request);
        }

        private IEnumerator FlushInternalQueue()
        {
            if (_internalQueue.Count == 0) yield break;

            List<Tracking> toSend;
            lock (_lock)
            {
                toSend = new List<Tracking>(_internalQueue);
                _internalQueue.Clear();
            }

            string json = JsonConvert.SerializeObject(new BatchedTracks { tracks = toSend });
            HintwayLog("Flushing internal queue — {0} events", toSend.Count);

            using UnityWebRequest request = CreateRequest(_url + "/batch", json);
            yield return request.SendWebRequest();

            LogRequestResult(request);
        }

        private IEnumerator AutoFlushRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(_autoFlushInterval);

                if (_serverAlive && _internalQueue.Count > 0)
                    yield return FlushInternalQueue();
            }
        }

        private UnityWebRequest CreateRequest(string url, string json)
        {
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private void LogRequestResult(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                HintwayLog("Request failed: {0} — HTTP {1} — {2}", request.url, request.responseCode,
                    request.downloadHandler?.text ?? "(no body)");
            }
            else
            {
                HintwayLog("Request succeeded: {0}", request.url);
            }
        }

        private void StopAutoFlush()
        {
            if (_autoFlushRoutine != null)
            {
                StopCoroutine(_autoFlushRoutine);
                _autoFlushRoutine = null;
            }
        }

        private void HintwayLog(string format, params object[] args)
        {
            if (!_verbose) return;
            try { Debug.LogFormat("[Hintway] " + format, args); }
            catch { Debug.Log("[Hintway] " + string.Format(format, args)); }
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Application lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        private bool OnWantsToQuit()
        {
            if (_flushCompleted) return true;

            if (!_isQuitting)
            {
                _isQuitting = true;
                StartCoroutine(QuitFlushRoutine());
            }

            return false;
        }

        private IEnumerator QuitFlushRoutine()
        {
            FlushAllBeforeClosing();

            float timer = 0f;
            while (timer < 2f)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            _flushCompleted = true;
            Application.Quit();
        }

        private void OnApplicationQuit() => FlushAllBeforeClosing();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) FlushAllBeforeClosing();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) FlushAllBeforeClosing();
        }

        private void FlushAllBeforeClosing()
        {
            if (!_enableAnalytics) return;

            lock (_lock)
            {
                _manualBatchedTracks.tracks.Add(CreateTracking("app_exit", ""));

                if (_internalQueue.Count > 0)
                {
                    _manualBatchedTracks.tracks.AddRange(_internalQueue);
                    _internalQueue.Clear();
                }
            }

            if (_manualBatchedTracks.tracks.Count == 0) return;

            StartCoroutine(PostBatchRoutine());
            Debug.Log("[Hintway] Final flush initiated.");
        }
    }
}
