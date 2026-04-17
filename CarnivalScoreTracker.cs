using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace CarnivalMod
{
    public class CarnivalScoreTracker : MonoBehaviour
    {
        // --- Tracked state ---
        private int   _hitCount;
        private int   _deathCount;
        private bool  _initialized;
        private bool  _wasDamaged;
        private bool  _wasDead;
        private float _hitCooldown;

        // --- Gold time ---
        private bool  _hasGoldTime;
        private float _goldTimeLimit;

        private const float HitPenalty = 0.2f;

        // --- Display ---
        private GameObject _displayQuad;
        private GameObject _panelCube;
        private GameObject _offCamGO;
        private GameObject _canvasGO;
        private Text       _scoreText;
        private string     _lastScoreText;
        private bool       _hidden;
        private float      _hideTimer;

        // ---------------------------------------------------------------

        private void Start()
        {
            Plugin.Log.LogInfo("[CarnivalScore] Tracker started.");
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            Plugin.Log.LogInfo("[CarnivalScore] OnDestroy called — tracker is being destroyed!");
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (_displayQuad != null) Object.Destroy(_displayQuad);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            if (_panelCube  != null) { Object.Destroy(_panelCube);  _panelCube  = null; }
            if (_displayQuad != null) { Object.Destroy(_displayQuad); _displayQuad = null; }
            if (_offCamGO   != null) { Object.Destroy(_offCamGO);   _offCamGO   = null; }
            if (_canvasGO   != null) { Object.Destroy(_canvasGO);   _canvasGO   = null; }
            _scoreText     = null;
            _lastScoreText = null;
            _initialized   = false;
            _hidden        = false;
            _hideTimer     = 0f;
            _hitCount    = 0;
            _deathCount  = 0;
            _wasDamaged  = false;
            _wasDead     = false;
            _hitCooldown = 0f;
        }

        // ---------------------------------------------------------------

        private void Update()
        {
            if (!_initialized)
            {
                if (PlayerHealthAndStats.PlayerMaxHp > 0)
                {
                    _hitCount   = 0;
                    _deathCount = 0;
                    _wasDamaged = false;
                    InitGoldTime();
                    BuildHUD();
                    _initialized = _displayQuad != null;
                    if (_initialized)
                        Plugin.Log.LogInfo("[CarnivalScore] Initialized.");
                }
                return;
            }

            if (_displayQuad == null) { _initialized = false; return; }

            if (LevelProgressControl.LevelOver)
            {
                if (!_hidden)
                {
                    _hideTimer += Time.deltaTime;
                    if (_hideTimer >= 3f)
                    {
                        if (_panelCube != null) _panelCube.SetActive(false);
                        if (_scoreText  != null) _scoreText.text = "";
                        _hidden = true;
                    }
                }
                return;
            }

            if (_hitCooldown > 0f)
                _hitCooldown -= Time.deltaTime;

            var  action04  = PlayerBhysics.Player?.Actions?.Action04;
            bool isDamaged = action04 != null && action04.Damaged;
            bool isDead    = action04 != null && action04.DeadCounter > 0f;

            if (isDamaged && !_wasDamaged && _hitCooldown <= 0f)
            {
                _hitCount++;
                _hitCooldown = 1.0f;
                Plugin.Log.LogInfo($"[CarnivalScore] Hit! count={_hitCount} t={Time.time:F2}");
            }

            if (isDead && !_wasDead)
            {
                _deathCount++;
                Plugin.Log.LogInfo($"[CarnivalScore] Death! count={_deathCount} t={Time.time:F2}");
            }

            _wasDamaged = isDamaged;
            _wasDead    = isDead;

            RefreshUI();
        }

        // ---------------------------------------------------------------

        private void BuildHUD()
        {
            if (_displayQuad != null) Object.Destroy(_displayQuad);

            Camera uiCam = null;
            foreach (var cam in Object.FindObjectsOfType<Camera>())
                if (cam.name == "Ui_Camera") { uiCam = cam; break; }
            if (uiCam == null) { Plugin.Log.LogError("[CarnivalScore] Ui_Camera not found."); return; }

            // RT taller to fit score + hit counter lines
            var rt = new RenderTexture(256, 96, 0, RenderTextureFormat.ARGB32);
            rt.Create();

            const int offLayer = 29;

            _offCamGO = new GameObject("CarnivalOffCam");
            var offCamGO = _offCamGO;
            Object.DontDestroyOnLoad(offCamGO);
            offCamGO.transform.position = new Vector3(0f, 0f, -10f);
            offCamGO.transform.rotation = Quaternion.identity;
            var offCam = offCamGO.AddComponent<Camera>();
            offCam.targetTexture    = rt;
            offCam.clearFlags       = CameraClearFlags.SolidColor;
            offCam.backgroundColor  = new Color(0.1f, 0.1f, 0.1f, 1f);
            offCam.cullingMask      = 1 << offLayer;
            offCam.orthographic     = true;
            offCam.orthographicSize = 48f;
            offCam.nearClipPlane    = 0.1f;
            offCam.farClipPlane     = 20f;
            offCam.depth            = -100f;

            _canvasGO = new GameObject("CarnivalCanvas");
            var canvasGO = _canvasGO;
            Object.DontDestroyOnLoad(canvasGO);
            canvasGO.layer = offLayer;
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.position   = Vector3.zero;
            canvasRect.sizeDelta  = new Vector2(256f, 96f);
            canvasRect.localScale = Vector3.one;

            Font gameFont = null;
            foreach (var t in Object.FindObjectsOfType<Text>())
                if (t.font != null) { gameFont = t.font; break; }

            var textGO = new GameObject("CarnivalText");
            textGO.layer = offLayer;
            textGO.transform.SetParent(canvasGO.transform, false);
            var tr = textGO.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            _scoreText = textGO.AddComponent<Text>();
            _scoreText.text            = "<size=14>CARNIVAL</size>\n<size=28>0</size>";
            _scoreText.font            = gameFont ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _scoreText.fontSize        = 28;
            _scoreText.color           = Color.white;
            _scoreText.alignment       = TextAnchor.MiddleCenter;
            _scoreText.supportRichText = true;

            Shader sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            var mat = new Material(sh);
            mat.mainTexture = rt;

            _displayQuad = new GameObject("CarnivalMarker");
            Object.DontDestroyOnLoad(_displayQuad);
            _hidden    = false;
            _panelCube = SpawnCubeWithMat(uiCam, 7f, 4.7f, 5f, mat, 2.5f, 0.94f, 0.3f);

            Plugin.Log.LogInfo("[CarnivalScore] Score panel built.");
        }

        // ---------------------------------------------------------------

        private void RefreshUI()
        {
            if (_scoreText == null) return;

            string next;
            if (StageScore.NoScore)
            {
                next = "<size=14>CARNIVAL</size>\n<size=28>N/A</size>";
            }
            else
            {
                float  carnival  = CalcCarnivalScore();
                string scoreStr  = FormatScore(carnival);
                int    totalHits = _hitCount + _deathCount;
                string hitStr    = totalHits > 0 ? $"  <color=red>{totalHits} HIT{(totalHits > 1 ? "S" : "")}</color>" : "";
                string scoreColor = GetScoreColor(carnival);
                next = $"<size=14>CARNIVAL{hitStr}</size>\n<size=28><color={scoreColor}>{scoreStr}</color></size>";
            }

            if (next == _lastScoreText) return;
            _lastScoreText  = next;
            _scoreText.text = next;
            Canvas.ForceUpdateCanvases();
        }

        // ---------------------------------------------------------------

        private void InitGoldTime()
        {
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
                Plugin.Log.LogError("[CarnivalScore] InitGoldTime threw: " + e);
            }
        }

        private float GetTimePenalty()
        {
            if (!_hasGoldTime) return 0f;
            float overtime = StageTimer.StageTime - _goldTimeLimit;
            if (overtime <= 0f) return 0f;
            return 0.1f + Mathf.Floor(overtime / 10f) * 0.1f;
        }

        private string GetScoreColor(float carnival)
        {
            if (carnival <= 0f) return "white";
            if (!_hasGoldTime)  return "white";
            float timeLeft = _goldTimeLimit - StageTimer.StageTime;
            if (timeLeft < 0f)  return "#BB88FF";  // over time — light purple
            if (timeLeft <= 20f) return "orange";   // warning window
            return "white";
        }

        private float CalcCarnivalScore()
        {
            float penalty    = (_hitCount + _deathCount) * HitPenalty + GetTimePenalty();
            float multiplier = Mathf.Max(0f, 1f - penalty);
            return ScoreManager.CurrentScore * multiplier;
        }

        private static GameObject SpawnCubeWithMat(Camera cam, float x, float y, float z,
                                                    Material mat, float sx, float sy, float sz)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name  = "CarnivalScorePanel";
            go.layer = 5;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(x, y, z);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            go.transform.localScale    = new Vector3(sx, sy, sz);
            go.GetComponent<Renderer>().material = mat;
            Plugin.Log.LogInfo($"[CarnivalScore] Panel cube at ({x},{y},{z}) scale=({sx},{sy},{sz})");
            return go;
        }

        private static string FormatScore(float score)
        {
            int s = Mathf.FloorToInt(score);
            if (s >= 1_000_000) return (s / 1_000_000) + "," + ((s % 1_000_000) / 1000).ToString("D3") + "," + (s % 1000).ToString("D3");
            if (s >= 1_000)     return (s / 1_000) + "," + (s % 1_000).ToString("D3");
            return s.ToString();
        }
    }
}
