using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

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

    public static AnalyticsManager Instance { get; private set; }

    [SerializeField] private bool _initializeOnAwake = true;
    [SerializeField] private bool _enableAnalytics = true;
    [SerializeField] private bool _verbose = false;

    [SerializeField] private string _tenantId;
    [SerializeField] private string _url = "https://in.vortexanalytics.io";
    [SerializeField] private string _platform;

    [Header("Auto-Flush Settings (Doesn't consern the manual batching API)")]
    [Tooltip("If true, all events are queued until a manual Flush or timer Flush occurs.")]
    [SerializeField] private bool _autoBatching = false;
    [SerializeField] private float _autoFlushInterval = 10f;

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
        {
            StartCoroutine(CheckServerAvailability());
        }

        TrackEvent("app_started");
    }

    private void InitSession()
    {
        _identity = PlayerPrefs.GetString("device_identity", Guid.NewGuid().ToString());
        PlayerPrefs.SetString("device_identity", _identity);
        _sessionId = Guid.NewGuid().ToString();
        _appVersion = Application.version;
        VortexLog("Session initialized - Identity: {0}, SessionId: {1}, AppVersion: {2}", _identity, _sessionId, _appVersion);
    }

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
            {
                StartCoroutine(CheckServerAvailability());
            }
            else if (_serverAlive && _autoBatching && _autoFlushRoutine == null)
            {
                _autoFlushRoutine = StartCoroutine(AutoFlushRoutine());
            }
        }
    }

    private Tracking CreateTracking(string name, string value)
    {
        var trackingData = new TrackingData
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
            trackingData.custom = _customData;

        return new Tracking
        {
            tenant_id = _tenantId,
            tracking = trackingData
        };
    }

    // Networking
    private IEnumerator CheckServerAvailability()
    {
        if (string.IsNullOrEmpty(_url)) yield break;
        if (string.IsNullOrEmpty(_tenantId))
        {
            VortexLog("Tenant ID is not set. Analytics disabled.");
            _serverAlive = false;
            _isServerChecked = true;
            yield break;
        }

        string validateUrl = _url + "/validate?tenant_id=" + UnityWebRequest.EscapeURL(_tenantId);
        VortexLog("Validating tenant at {0}", validateUrl);

        using UnityWebRequest request = UnityWebRequest.Get(validateUrl);
        request.timeout = 5;

        yield return request.SendWebRequest();

        _serverAlive = request.result == UnityWebRequest.Result.Success;
        bool tenantValid = _serverAlive && request.responseCode == 200;
        _isServerChecked = true;

        if (!_serverAlive)
        {
            VortexLog("Server is unreachable. Analytics disabled.");
            if (_autoFlushRoutine != null)
            {
                StopCoroutine(_autoFlushRoutine);
                _autoFlushRoutine = null;
            }
            yield break;
        }

        if (!tenantValid)
        {
            VortexLog("Tenant ID '{0}' is invalid or unauthorized (HTTP {1}). Analytics disabled.", _tenantId, request.responseCode);
            _serverAlive = false;
            if (_autoFlushRoutine != null)
            {
                StopCoroutine(_autoFlushRoutine);
                _autoFlushRoutine = null;
            }
            yield break;
        }

        VortexLog("Tenant validated successfully - Server alive, tenant '{0}' authorized.", _tenantId);

        if (_autoBatching && _autoFlushRoutine == null)
            _autoFlushRoutine = StartCoroutine(AutoFlushRoutine());

        if (!_autoBatching && _internalQueue.Count > 0)
            StartCoroutine(FlushInternalQueue());
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

    private void VortexLog(string format, params object[] args)
    {
        if (!_verbose) return;
        try { Debug.LogFormat("[Vortex] " + format, args); }
        catch { Debug.Log("[Vortex] " + string.Format(format, args)); }
    }

    // Sending
    private IEnumerator PostSingle(Tracking tracking)
    {
        string json = JsonConvert.SerializeObject(tracking);
        using UnityWebRequest request = CreateRequest(_url + "/track", json);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            VortexLog("Request failed: {0}", request.url);
            VortexLog("Response code: {0}", request.responseCode);
            VortexLog("Response body: {0}", request.downloadHandler != null ? request.downloadHandler.text : "(no body)");
        }
        else
        {
            VortexLog("Request succeeded: {0}", request.url);
        }
    }

    public void FlushManualBatch() 
    {
        if (!_enableAnalytics) return;
        StartCoroutine(PostBatchRoutine());
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

        VortexLog("Posting manual batch with {0} events", JsonConvert.DeserializeObject<BatchedTracks>(json)?.tracks?.Count ?? 0);

        using UnityWebRequest request = CreateRequest(_url + "/batch", json);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            VortexLog("Batch request failed: {0}", request.url);
            VortexLog("Response code: {0}", request.responseCode);
            VortexLog("Response body: {0}", request.downloadHandler != null ? request.downloadHandler.text : "(no body)");
        }
        else
        {
            VortexLog("Batch request succeeded: {0}", request.url);
        }
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

        BatchedTracks batch = new BatchedTracks { tracks = toSend };
        string json = JsonConvert.SerializeObject(batch);

        VortexLog("Flushing internal queue with {0} events", batch.tracks.Count);

        using UnityWebRequest request = CreateRequest(_url + "/batch", json);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            VortexLog("Flush request failed: {0}", request.url);
            VortexLog("Response code: {0}", request.responseCode);
            VortexLog("Response body: {0}", request.downloadHandler != null ? request.downloadHandler.text : "(no body)");
        }
        else
        {
            VortexLog("Flush request succeeded: {0}", request.url);
        }
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

    // Custom Data
    public void SetCustomData(Dictionary<string, object> customData)
    {
        if (customData == null || customData.Count == 0)
        {
            _customData = "";
            return;
        }

        _customData = JsonConvert.SerializeObject(customData);
    }

    public void ClearCustomData()
    {
        _customData = "";
    }

    // Public methods
    public void TrackEvent(string eventName, Dictionary<string, object> props)
    {
        if (!_enableAnalytics) return;

        if (!_serverAlive && _isServerChecked && !_autoBatching)
            return;

        string serializedProps = JsonConvert.SerializeObject(props); 
        ProcessTrackEvent(eventName, serializedProps);
    }

    public void TrackEvent(string eventName, string props = "")
    {
        if (!_enableAnalytics) return;

        if (!_serverAlive && _isServerChecked && !_autoBatching)
            return;

        string wrapped = string.IsNullOrEmpty(props) ? "" : JsonConvert.SerializeObject(new ValueWrapper { data = props });
        ProcessTrackEvent(eventName, wrapped);
    }

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

    public void BatchedTrackEvent(string eventName, Dictionary<string, object> props)
    {
        if (!_enableAnalytics) return;
        if (!_serverAlive) return;

        string serializedProps = JsonConvert.SerializeObject(props);
        lock (_lock) { _manualBatchedTracks.tracks.Add(CreateTracking(eventName, serializedProps)); }
    }

    public void BatchedTrackEvent(string eventName, string props = "")
    {
        if (!_enableAnalytics) return;
        if (!_serverAlive) return;

        lock (_lock) { _manualBatchedTracks.tracks.Add(CreateTracking(eventName, props)); }
    }

    private bool OnWantsToQuit()
    {
        if (_flushCompleted)
            return true;

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

        float timeout = 2f;
        float timer = 0f;

        while (timer < timeout)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        _flushCompleted = true;
        Application.Quit();
    }

    private void OnApplicationQuit()
    {
        FlushAllBeforeClosing();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            FlushAllBeforeClosing();
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            FlushAllBeforeClosing();
        }
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
        
        Debug.Log("[Analytics] Attempting final flush before exit...");
    }
}
