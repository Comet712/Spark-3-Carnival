using UnityEngine;
using UnityEngine.UI;

namespace CarnivalMod
{
    public class CarnivalScoreTracker : MonoBehaviour
    {
        // --- Tracked state ---
        private int   _hitCount;    // damage taken without dying
        private int   _deathCount;  // deaths (tracked separately for per-level weight tuning)
        private int   _lastHP;
        private bool  _initialized;

        // --- Gold time ---
        private bool  _hasGoldTime;
        private float _goldTimeLimit;   // gold time + 60 seconds

        // Hit and death weights — tweak per level in the future as needed
        private const float HitPenalty   = 0.2f;
        private const float DeathPenalty = 0.2f;

        // --- Canvas UI references ---
        private GameObject _canvasGO;
        private Text       _scoreText;
        private Text       _detailText;

        // ---------------------------------------------------------------

        private void Start()
        {
            Plugin.Log.LogInfo("[CarnivalScore] Tracker started.");
            try
            {
                _hasGoldTime = !GameProgressVariables.NoTimeMedalStatic;
                if (_hasGoldTime
                    && Save.SpeedGoldTargets != null
                    && Save.CurrentStageIndex >= 0
                    && Save.CurrentStageIndex < Save.SpeedGoldTargets.Length)
                {
                    float goldTime = Save.SpeedGoldTargets[Save.CurrentStageIndex];
                    if (goldTime > 0f)
                        _goldTimeLimit = goldTime + 60f;
                    else
                        _hasGoldTime = false;
                }
                else
                {
                    _hasGoldTime = false;
                }
                Plugin.Log.LogInfo($"[CarnivalScore] hasGoldTime={_hasGoldTime} goldTimeLimit={_goldTimeLimit}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("[CarnivalScore] Start() threw: " + e);
            }

            BuildCanvas();
        }

        private void OnDestroy()
        {
            if (_canvasGO != null) Destroy(_canvasGO);
        }

        // ---------------------------------------------------------------

        private void Update()
        {
            if (!_initialized)
            {
                if (PlayerHealthAndStats.PlayerMaxHp > 0)
                {
                    _lastHP = PlayerHealthAndStats.PlayerHP;
                    _initialized = true;
                    Plugin.Log.LogInfo($"[CarnivalScore] Initialized. HP={_lastHP} MaxHP={PlayerHealthAndStats.PlayerMaxHp}");
                }
                return;
            }

            if (LevelProgressControl.LevelOver) return;

            int currentHP = PlayerHealthAndStats.PlayerHP;
            if (currentHP < _lastHP && _lastHP > 0)
            {
                if (currentHP <= 0) _deathCount++;
                else                _hitCount++;
            }
            _lastHP = currentHP;

            RefreshUI();
        }

        // ---------------------------------------------------------------

        private float GetTimePenalty()
        {
            if (!_hasGoldTime) return 0f;
            float overtime = StageTimer.StageTime - _goldTimeLimit;
            if (overtime <= 0f) return 0f;
            return 0.1f + Mathf.Floor(overtime / 10f) * 0.1f;
        }

        private float CalcCarnivalScore()
        {
            float penalty    = _hitCount * HitPenalty + _deathCount * DeathPenalty + GetTimePenalty();
            float multiplier = Mathf.Max(0f, 1f - penalty);
            return ScoreManager.CurrentScore * multiplier;
        }

        // ---------------------------------------------------------------

        private void BuildCanvas()
        {
            // The game uses two cameras:
            //   Main Camera  depth=1   culling=-33 (everything except layer 5)
            //   Ui_Camera    depth=10  culling=32  (layer 5 / UI only)
            // All HUD elements live on layer 5 and are rendered by Ui_Camera.
            // We create a ScreenSpaceCamera canvas on Ui_Camera so our overlay
            // goes through the same pipeline and appears above the game's HUD.

            Camera uiCam = null;
            foreach (var cam in Object.FindObjectsOfType<Camera>())
            {
                if (cam.name == "Ui_Camera") { uiCam = cam; break; }
            }

            if (uiCam == null)
            {
                Plugin.Log.LogError("[CarnivalScore] Ui_Camera not found — cannot build panel.");
                return;
            }
            Plugin.Log.LogInfo($"[CarnivalScore] Found Ui_Camera depth={uiCam.depth}  culling={uiCam.cullingMask}");

            // Canvas
            _canvasGO = new GameObject("CarnivalScoreCanvas");
            _canvasGO.layer = 5;

            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera  = uiCam;
            canvas.planeDistance = 0.5f;
            canvas.sortingOrder = 100;   // above game HUD (sortingOrder 0)

            var scaler = _canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode    = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasGO.AddComponent<GraphicRaycaster>();

            // Panel — anchored top-right, below the game's TIME display
            var panel = new GameObject("CarnivalPanel");
            panel.transform.SetParent(_canvasGO.transform, false);

            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(1f, 1f);
            panelRect.anchorMax        = new Vector2(1f, 1f);
            panelRect.pivot            = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-10f, -90f);
            panelRect.sizeDelta        = new Vector2(210f, 70f);

            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            // "CARNIVAL SCORE" title
            var titleText = MakeText(panel.transform, "CARNIVAL SCORE",
                anchorMin: new Vector2(0f, 0.65f), anchorMax: new Vector2(1f, 1f),
                fontSize: 13, bold: true, color: new Color(1f, 0.82f, 0.18f));
            titleText.alignment = TextAnchor.MiddleCenter;

            // Live score number
            _scoreText = MakeText(panel.transform, "0",
                anchorMin: new Vector2(0f, 0.25f), anchorMax: new Vector2(1f, 0.72f),
                fontSize: 22, bold: true, color: Color.white);
            _scoreText.alignment = TextAnchor.MiddleCenter;

            // Breakdown detail line
            _detailText = MakeText(panel.transform, "0 hits",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0.3f),
                fontSize: 11, bold: false, color: new Color(0.8f, 0.8f, 0.8f));
            _detailText.alignment = TextAnchor.MiddleCenter;

            // Everything must be on layer 5 so Ui_Camera renders it
            SetLayerRecursively(_canvasGO, 5);

            Plugin.Log.LogInfo($"[CarnivalScore] Canvas built on Ui_Camera. Screen={Screen.width}x{Screen.height}");
        }

        private void RefreshUI()
        {
            if (_scoreText == null) return;

            float carnivalScore = CalcCarnivalScore();
            float timePenalty   = GetTimePenalty();
            float totalPenalty  = _hitCount * HitPenalty + _deathCount * DeathPenalty + timePenalty;

            _scoreText.text = string.Format("{0:0,0}", Mathf.FloorToInt(carnivalScore));

            int    displayHits = _hitCount + _deathCount;
            string detail      = displayHits == 1 ? "1 hit" : displayHits + " hits";

            if (_hasGoldTime)
            {
                float overtime = StageTimer.StageTime - _goldTimeLimit;
                if (overtime > 0f)
                    detail += "  \u2022  +" + Mathf.FloorToInt(overtime) + "s over";
            }

            if (totalPenalty > 0f)
                detail += "  (\u2212" + Mathf.RoundToInt(totalPenalty * 100f) + "%)";

            _detailText.text = detail;
        }

        private static Text MakeText(Transform parent, string content,
            Vector2 anchorMin, Vector2 anchorMax,
            int fontSize, bool bold, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(4f,  2f);
            rect.offsetMax = new Vector2(-4f, -2f);

            var text = go.AddComponent<Text>();
            text.text      = content;
            text.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize  = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.color     = color;
            text.alignment = TextAnchor.MiddleCenter;

            return text;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
