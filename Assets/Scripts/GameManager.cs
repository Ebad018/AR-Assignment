using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

/// <summary>
/// Bomb Squad: bombs appear on the real floor (ARCore planes). Tap to defuse them before the
/// fuse runs out. Survive the round and you win; let one explode and you lose.
/// Attach to an empty GameObject - everything else is found or created at runtime.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Round")]
    public float roundLength = 30f;
    public int maxAliveBombs = 6;
    [Tooltip("Seconds between spawns at the start / end of the round")]
    public float startSpawnInterval = 1.6f;
    public float endSpawnInterval = 0.6f;
    [Tooltip("Fuse length at the start / end of the round")]
    public float startFuseTime = 5f;
    public float endFuseTime = 2.2f;

    [Header("Bombs")]
    public float bombSize = 0.16f;
    public float maxSpawnDistance = 4f;
    public float minBombSpacing = 0.35f;

    enum State { Scanning, Ready, Playing, Won, Lost }
    State _state = State.Scanning;

    ARPlaneManager _planeManager;
    Camera _camera;

    readonly List<Bomb> _bombs = new();
    float _timeLeft;
    float _spawnTimer;
    int _defused;

    Text _statusText;
    Text _scoreText;
    Text _timerText;
    GameObject _endPanel;
    Text _endTitle;
    Text _endBody;

    void Awake()
    {
        var origin = FindFirstObjectByType<XROrigin>();
        _camera = origin != null ? origin.Camera : Camera.main;
        _planeManager = FindFirstObjectByType<ARPlaneManager>();
        _planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        if (FindFirstObjectByType<AudioListener>() == null)
            _camera.gameObject.AddComponent<AudioListener>();

        Application.targetFrameRate = 60;
        Fx.Init();
        BuildUI();
    }

    void Update()
    {
        bool tapped = TryGetTap(out Vector2 tapPos);

        switch (_state)
        {
            case State.Scanning:
                _statusText.text = "Move your phone slowly\nto scan the floor";
                if (_planeManager.trackables.count > 0) _state = State.Ready;
                break;

            case State.Ready:
                _statusText.text = "Bombs will appear on the floor.\nTap them to defuse before the fuse burns out.\n\n" +
                                   "Survive " + Mathf.RoundToInt(roundLength) + " seconds!\n\nTap to start";
                if (tapped) StartRound();
                break;

            case State.Playing:
                TickRound(tapped, tapPos);
                break;
        }
    }

    // ---- Round flow ----------------------------------------------------------

    void StartRound()
    {
        foreach (var b in _bombs) if (b != null) Destroy(b.gameObject);
        _bombs.Clear();

        _defused = 0;
        _timeLeft = roundLength;
        _spawnTimer = 0.8f;
        _statusText.text = "";
        _endPanel.SetActive(false);
        _scoreText.text = "Defused: 0";
        _state = State.Playing;
    }

    void TickRound(bool tapped, Vector2 tapPos)
    {
        _timeLeft -= Time.deltaTime;
        _bombs.RemoveAll(b => b == null);

        if (_timeLeft <= 0f)
        {
            _timeLeft = 0f;
            Win();
            return;
        }

        // Difficulty ramps: shorter fuses and faster spawns as the round goes on.
        float progress = 1f - (_timeLeft / roundLength);
        float interval = Mathf.Lerp(startSpawnInterval, endSpawnInterval, progress);
        float fuse = Mathf.Lerp(startFuseTime, endFuseTime, progress);

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f && _bombs.Count < maxAliveBombs)
        {
            _spawnTimer = TrySpawnBomb(fuse) ? interval : 0.25f; // no usable plane nearby, retry soon
        }

        if (tapped) TryDefuse(tapPos);

        _timerText.text = Mathf.CeilToInt(_timeLeft).ToString();
    }

    void Win()
    {
        _state = State.Won;
        foreach (var b in _bombs) if (b != null && b.IsLive) b.Defuse();
        _timerText.text = "0";
        StartCoroutine(ShowEnd("YOU SURVIVED!", new Color(0.4f, 1f, 0.5f),
            "Bombs defused: " + _defused, 0.7f));
    }

    public void OnBombExploded(Bomb source)
    {
        if (_state != State.Playing) return;
        _state = State.Lost;
        float survived = roundLength - _timeLeft;
        StartCoroutine(ChainReaction(source));
        StartCoroutine(ShowEnd("BOOM!", new Color(1f, 0.35f, 0.3f),
            "Bombs defused: " + _defused + "\nSurvived " + survived.ToString("0.0") + "s", 1.0f));
    }

    IEnumerator ChainReaction(Bomb source)
    {
        var others = new List<Bomb>(_bombs);
        foreach (var b in others)
        {
            if (b == null || b == source || !b.IsLive) continue;
            yield return new WaitForSeconds(0.15f);
            if (b != null) b.Explode();
        }
    }

    IEnumerator ShowEnd(string title, Color titleColor, string body, float delay)
    {
        yield return new WaitForSeconds(delay);
        _endTitle.text = title;
        _endTitle.color = titleColor;
        _endBody.text = body;
        _endPanel.SetActive(true);
    }

    // ---- Spawning ------------------------------------------------------------

    bool TrySpawnBomb(float fuse)
    {
        var candidates = new List<ARPlane>();
        foreach (var plane in _planeManager.trackables)
        {
            if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
            if (plane.subsumedBy != null) continue; // merged into another plane
            if (Vector3.Distance(plane.center, _camera.transform.position) > maxSpawnDistance) continue;
            candidates.Add(plane);
        }
        if (candidates.Count == 0) return false;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            ARPlane plane = PickWeightedByArea(candidates);
            Vector2 half = plane.extents;
            Vector3 local = new Vector3(Random.Range(-half.x, half.x), 0f, Random.Range(-half.y, half.y));
            Vector3 world = plane.center + plane.transform.rotation * local;

            if (TooCloseToAnotherBomb(world)) continue;

            var go = new GameObject("Bomb");
            go.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.transform.SetParent(plane.transform, true); // ride along with plane updates

            var bomb = go.AddComponent<Bomb>();
            bomb.fuseTime = fuse;
            bomb.Build(this, bombSize);
            _bombs.Add(bomb);
            return true;
        }
        return false;
    }

    static ARPlane PickWeightedByArea(List<ARPlane> planes)
    {
        float total = 0f;
        foreach (var p in planes) total += p.size.x * p.size.y;
        float pick = Random.value * total;
        foreach (var p in planes)
        {
            pick -= p.size.x * p.size.y;
            if (pick <= 0f) return p;
        }
        return planes[planes.Count - 1];
    }

    bool TooCloseToAnotherBomb(Vector3 pos)
    {
        foreach (var b in _bombs)
        {
            if (b == null) continue;
            if (Vector3.Distance(b.transform.position, pos) < minBombSpacing) return true;
        }
        return false;
    }

    // ---- Input ---------------------------------------------------------------

    bool TryGetTap(out Vector2 pos)
    {
        pos = default;
        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
        {
            pos = touch.primaryTouch.position.ReadValue();
            return true;
        }
        var mouse = Mouse.current; // editor testing
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            pos = mouse.position.ReadValue();
            return true;
        }
        return false;
    }

    void TryDefuse(Vector2 screenPos)
    {
        Ray ray = _camera.ScreenPointToRay(screenPos);
        // Generous radius so a thumb tap on a small bomb feels fair.
        if (!Physics.SphereCast(ray, bombSize * 0.5f, out RaycastHit hit, 20f)) return;

        var bomb = hit.collider.GetComponentInParent<Bomb>();
        if (bomb == null || !bomb.IsLive) return;

        bomb.Defuse();
        _defused++;
        _scoreText.text = "Defused: " + _defused;
    }

    // ---- UI (built in code so the scene needs no Canvas setup) --------------

    void BuildUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        _scoreText  = MakeText(canvasGo.transform, "Score", 64, TextAnchor.UpperLeft,  new Vector2(0, 1), new Vector2(40, -40));
        _timerText  = MakeText(canvasGo.transform, "Timer", 84, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-40, -40));
        _statusText = MakeText(canvasGo.transform, "Status", 52, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
        _statusText.rectTransform.sizeDelta = new Vector2(920, 800);

        // End-of-round panel: title, score, Restart / Exit.
        _endPanel = new GameObject("EndPanel", typeof(Image));
        _endPanel.transform.SetParent(canvasGo.transform, false);
        _endPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        var panelRt = _endPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;

        _endTitle = MakeText(_endPanel.transform, "Title", 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 260));
        _endTitle.rectTransform.sizeDelta = new Vector2(1000, 200);
        _endBody = MakeText(_endPanel.transform, "Body", 60, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 60));
        _endBody.rectTransform.sizeDelta = new Vector2(1000, 260);

        MakeButton(_endPanel.transform, "Restart", new Color(0.2f, 0.65f, 0.3f), new Vector2(-190, -200), StartRound);
        MakeButton(_endPanel.transform, "Exit", new Color(0.7f, 0.25f, 0.25f), new Vector2(190, -200), Application.Quit);

        _endPanel.SetActive(false);
    }

    static Text MakeText(Transform parent, string name, int size, TextAnchor anchor, Vector2 anchorPos, Vector2 offset)
    {
        var go = new GameObject(name, typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchorPos;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(600, 120);
        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.8f);
        outline.effectDistance = new Vector2(3, -3);
        return text;
    }

    static void MakeButton(Transform parent, string label, Color color, Vector2 offset, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label + "Button", typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(320, 140);
        go.GetComponent<Button>().onClick.AddListener(onClick);

        var text = MakeText(go.transform, "Label", 56, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
        text.text = label;
        text.rectTransform.sizeDelta = rt.sizeDelta;
    }
}
