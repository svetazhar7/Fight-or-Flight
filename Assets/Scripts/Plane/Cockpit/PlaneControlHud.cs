using UnityEngine;
using UnityEngine.UI;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Owner-only HUD. Shows a top-left dashboard with live telemetry (speed, altitude,
    /// throttle, RPM) and the control key reference for the current mode. Also shows the
    /// active camera view (1st / 3rd person) so the player knows which mode they're in.
    ///
    /// Built entirely in code — no prefab or Canvas in the scene needed.
    /// Only visible to the local owner of the plane (the seated pilot).
    /// </summary>
    public class PlaneControlHud : MonoBehaviour
    {
        [Tooltip("Auto-found on this GameObject if left empty.")]
        [SerializeField] private PlaneController _plane;

        private GameObject _root;
        private Text _text;
        private bool _built;

        // cached for the view-mode line, updated by PlayerController via SetThirdPerson
        private bool _thirdPerson;

        public void SetThirdPerson(bool value) => _thirdPerson = value;

        // ── Control reference strings ───────────────────────────────────────────
        private const string GroundKeys =
            "<b>TAXI</b>  E=Gas  Q=Brake  A/D=Steer  W=Rotate(above Vr)";

        private const string FlightKeys =
            "<b>FLIGHT</b>  W/S=Pitch  A/D=Roll  E=Thr+  Q=Thr-";

        private const string CommonKeys =
            "F=Exit seat   `=Toggle 1st/3rd view";

        // ── Unity lifecycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            if (_plane == null)
                _plane = GetComponent<PlaneController>();
        }

        private void Update()
        {
            if (_plane == null) return;

            bool owned = _plane.IsOwner;

            if (!_built)
            {
                if (!owned) return;
                Build();
            }

            if (_root.activeSelf != owned)
                _root.SetActive(owned);
            if (!owned) return;

            _text.text = ComposeText();
        }

        private string ComposeText()
        {
            bool ground = _plane.Mode == PlaneControlMode.Ground;

            // ── Telemetry ──
            float speedKmh  = Mathf.Max(0f, _plane.ForwardSpeed) * 3.6f;
            float altM      = _plane.transform.position.y;
            float throttle  = _plane.Throttle * 100f;
            float rpm       = _plane.NormalizedRpm * 100f;
            string viewMode = _thirdPerson ? "3rd person" : "1st person";

            // ── Status ──
            string status;
            if (_plane.IsStalling)
                status = "<color=#FF4444><b>⚠ STALL — lower the nose!</b></color>";
            else if (ground && _plane.GroundSpeed >= _plane.RotationSpeed)
                status = "<color=#44FF88><b>Vr reached — hold W to lift off!</b></color>";
            else if (ground)
                status = $"<color=#AAAAAA>Hold E to accelerate  (Vr = {_plane.RotationSpeed * 3.6f:0} km/h)</color>";
            else
                status = "<color=#88DDFF>Airborne</color>";

            // ── Build text ──
            return
                $"<b><color=#FFDD44>● PLANE HUD</color></b>   <color=#888888>View: {viewMode}</color>\n" +
                "─────────────────────────────────────\n" +
                $"Speed    <b>{speedKmh,5:0} km/h</b>    Alt  <b>{altM,5:0} m</b>\n" +
                $"Throttle <b>{throttle,4:0} %</b>        RPM  <b>{rpm,4:0} %</b>\n" +
                "─────────────────────────────────────\n" +
                status + "\n" +
                "─────────────────────────────────────\n" +
                (ground ? GroundKeys : FlightKeys) + "\n" +
                CommonKeys;
        }

        // ── Canvas builder ───────────────────────────────────────────────────────
        private void Build()
        {
            _root = new GameObject("PlaneControlHud_Canvas");
            _root.transform.SetParent(null, false);

            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            // ── Panel ──────────────────────────────────────────────────────────
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            RectTransform pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = new Vector2(0f, 1f);
            pr.anchorMax = new Vector2(0f, 1f);
            pr.pivot     = new Vector2(0f, 1f);
            pr.anchoredPosition = new Vector2(20f, -20f);
            pr.sizeDelta        = new Vector2(560f, 220f);
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.62f);

            // ── Text ───────────────────────────────────────────────────────────
            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(panel.transform, false);
            RectTransform tr = textGo.AddComponent<RectTransform>();
            tr.anchorMin  = Vector2.zero;
            tr.anchorMax  = Vector2.one;
            tr.offsetMin  = new Vector2(14f, 12f);
            tr.offsetMax  = new Vector2(-14f, -12f);

            _text = textGo.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                         ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize             = 20;
            _text.lineSpacing          = 1.15f;
            _text.color                = Color.white;
            _text.supportRichText      = true;
            _text.horizontalOverflow   = HorizontalWrapMode.Overflow;
            _text.verticalOverflow     = VerticalWrapMode.Overflow;
            _text.alignment            = TextAnchor.UpperLeft;

            DontDestroyOnLoad(_root);
            _built = true;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }
    }
}
