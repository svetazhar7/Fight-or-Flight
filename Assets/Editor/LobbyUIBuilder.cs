using FightOrFlight.Lobby;
using FightOrFlight.UI;
using FishNet.Object;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FightOrFlight.EditorTools
{
    /// <summary>
    /// One-shot builder for the lobby screen: generates the player-row prefab and
    /// lays out the full lobby canvas in the active scene, wiring every reference.
    /// Re-runnable from the Tools menu — clears the previous build first.
    /// </summary>
    public static class LobbyUIBuilder
    {
        private const string FontPath = "Assets/Fonts/LilitaOne SDF.asset";
        private const string BgPath = "Assets/UI/Generated/LobbyBackground.png";
        private const string VigPath = "Assets/UI/Generated/Vignette.png";
        private const string AvatarPath = "Assets/UI/Generated/DefaultAvatar.png";
        private const string PrefabPath = "Assets/Prefabs/UI/LobbyPlayerItem.prefab";

        // Palette ------------------------------------------------------------
        private static readonly Color PanelColor = new Color32(24, 28, 50, 250);
        private static readonly Color ListColor = new Color32(12, 14, 28, 90);
        private static readonly Color CardColor = new Color32(40, 46, 78, 235);
        private static readonly Color White = new Color32(245, 247, 255, 255);
        private static readonly Color LightBlue = new Color32(150, 200, 255, 255);
        private static readonly Color Muted = new Color32(176, 184, 208, 255);
        private static readonly Color Gold = new Color32(255, 201, 56, 255);
        private static readonly Color BlueBtn = new Color32(52, 120, 230, 255);
        private static readonly Color GreenBtn = new Color32(54, 190, 110, 255);
        private static readonly Color RedBtn = new Color32(226, 72, 84, 255);
        private static readonly Color GoldBtn = new Color32(245, 172, 40, 255);

        private static TMP_FontAsset _font;
        private static Sprite _round;
        private static Sprite _button;
        private static Sprite _defaultAvatar;
        private static Sprite _bg;
        private static Sprite _vig;

        [MenuItem("Tools/Lobby/Build Lobby UI")]
        public static void BuildLobby()
        {
            LoadAssets();
            GameObject prefab = BuildPlayerItemPrefab();
            BuildScene(prefab);
            Debug.Log("[LobbyUIBuilder] Lobby UI built successfully.");
        }

        private static void LoadAssets()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            _bg = AssetDatabase.LoadAssetAtPath<Sprite>(BgPath);
            _vig = AssetDatabase.LoadAssetAtPath<Sprite>(VigPath);
            _defaultAvatar = AssetDatabase.LoadAssetAtPath<Sprite>(AvatarPath);
            _round = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            _button = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            if (_font == null) Debug.LogWarning("[LobbyUIBuilder] Lilita font asset not found — TMP defaults will be used.");
        }

        // ==================================================================== Prefab

        private static GameObject BuildPlayerItemPrefab()
        {
            var root = new GameObject("LobbyPlayerItem", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(0, 76);
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = 76;
            le.minHeight = 76;

            var item = root.AddComponent<LobbyPlayerItemUI>();

            // Gold host frame (border) behind the card.
            Image frame = AddImage(Child("Frame", rt), _round, new Color(1, 1, 1, 0), Image.Type.Sliced);
            Stretch(frame.rectTransform, 0, 0, 0, 0);
            frame.raycastTarget = false;

            // Card.
            Image card = AddImage(Child("Card", rt), _round, CardColor, Image.Type.Sliced);
            Stretch(card.rectTransform, 3, 3, 3, 3);
            card.raycastTarget = false;
            RectTransform cardRt = card.rectTransform;

            // Avatar.
            Image avatar = AddImage(Child("Avatar", cardRt), _defaultAvatar, Color.white, Image.Type.Simple);
            Anchor(avatar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(56, 56));
            avatar.raycastTarget = false;

            // Name + status (left text block).
            TextMeshProUGUI name = AddText(Child("Name", cardRt), "Player", 27, White, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            Anchor(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(82, 13), new Vector2(330, 34));
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI status = AddText(Child("Status", cardRt), "CONNECTED", 15, Muted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            Anchor(status.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(82, -15), new Vector2(330, 24));

            // Host badge (gold pill, top-right corner of the card).
            var hostBadgeGo = Child("HostBadge", cardRt);
            Image hostBadgeBg = AddImage(hostBadgeGo, _round, Gold, Image.Type.Sliced);
            Anchor(hostBadgeBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -8), new Vector2(62, 24));
            hostBadgeBg.raycastTarget = false;
            TextMeshProUGUI hostBadgeTxt = AddText(Child("Label", hostBadgeBg.rectTransform), "HOST", 13, new Color32(40, 30, 0, 255), TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(hostBadgeTxt.rectTransform, 0, 0, 0, 0);

            // Ready label (right cluster).
            TextMeshProUGUI ready = AddText(Child("Ready", cardRt), "NOT READY", 17, Muted, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            Anchor(ready.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-108, 0), new Vector2(160, 30));

            // Kick button (host only, far right).
            Button kick = MakeButton(Child("KickButton", cardRt), _button, RedBtn, "KICK", 16);
            Anchor(kick.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(82, 42));

            // Wire serialized fields.
            SetRef(item, "canvasGroup", root.GetComponent<CanvasGroup>());
            SetRef(item, "avatarImage", avatar);
            SetRef(item, "highlightFrame", frame);
            SetRef(item, "nameText", name);
            SetRef(item, "statusText", status);
            SetRef(item, "readyText", ready);
            SetRef(item, "hostBadge", hostBadgeGo);
            SetRef(item, "kickButton", kick);
            SetRef(item, "defaultAvatar", _defaultAvatar);

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ===================================================================== Scene

        private static void BuildScene(GameObject prefab)
        {
            Scene scene = SceneManager.GetActiveScene();

            // Clear any previous build so the builder is idempotent.
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == "LobbyCanvas" || go.name == "LobbyNetwork" || go.name == "EventSystem")
                    Object.DestroyImmediate(go);
            }

            // ---- EventSystem ----
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // ---- Network object that owns the roster ----
            var net = new GameObject("LobbyNetwork", typeof(NetworkObject), typeof(LobbyManager));

            // ---- Canvas ----
            var canvasGo = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform canvasRt = (RectTransform)canvasGo.transform;

            var lobbyUI = canvasGo.AddComponent<LobbyUI>();

            // ---- Background, darken, vignette ----
            Image bg = AddImage(Child("Background", canvasRt), _bg, Color.white, Image.Type.Simple);
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.raycastTarget = false;

            Image darken = AddImage(Child("Darken", canvasRt), null, new Color(0, 0, 0, 0.28f), Image.Type.Simple);
            Stretch(darken.rectTransform, 0, 0, 0, 0);
            darken.raycastTarget = false;

            Image vig = AddImage(Child("Vignette", canvasRt), _vig, Color.white, Image.Type.Simple);
            Stretch(vig.rectTransform, 0, 0, 0, 0);
            vig.raycastTarget = false;

            // ---- Panel ----
            Image panel = AddImage(Child("Panel", canvasRt), _round, PanelColor, Image.Type.Sliced);
            RectTransform panelRt = panel.rectTransform;
            panelRt.anchorMin = new Vector2(0.25f, 0.10f);
            panelRt.anchorMax = new Vector2(0.75f, 0.92f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            var panelGroup = panel.gameObject.AddComponent<CanvasGroup>();

            // Top accent bar.
            Image accent = AddImage(Child("Accent", panelRt), _round, BlueBtn, Image.Type.Sliced);
            accent.rectTransform.anchorMin = new Vector2(0, 1);
            accent.rectTransform.anchorMax = new Vector2(1, 1);
            accent.rectTransform.pivot = new Vector2(0.5f, 1);
            accent.rectTransform.sizeDelta = new Vector2(0, 8);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.raycastTarget = false;

            // ---- Header ----
            TextMeshProUGUI title = AddText(Child("Title", panelRt), "LOBBY", 72, White, TextAlignmentOptions.Center, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0, 1);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.sizeDelta = new Vector2(-48, 88);
            title.rectTransform.anchoredPosition = new Vector2(0, -22);

            TextMeshProUGUI lobbyName = AddText(Child("LobbyName", panelRt), "Steam Lobby", 30, LightBlue, TextAlignmentOptions.Center, FontStyles.Normal);
            lobbyName.rectTransform.anchorMin = new Vector2(0, 1);
            lobbyName.rectTransform.anchorMax = new Vector2(1, 1);
            lobbyName.rectTransform.pivot = new Vector2(0.5f, 1);
            lobbyName.rectTransform.sizeDelta = new Vector2(-48, 38);
            lobbyName.rectTransform.anchoredPosition = new Vector2(0, -112);

            TextMeshProUGUI count = AddText(Child("PlayerCount", panelRt), "Players: 0 / 8", 22, Muted, TextAlignmentOptions.Center, FontStyles.Normal);
            count.rectTransform.anchorMin = new Vector2(0, 1);
            count.rectTransform.anchorMax = new Vector2(1, 1);
            count.rectTransform.pivot = new Vector2(0.5f, 1);
            count.rectTransform.sizeDelta = new Vector2(-48, 28);
            count.rectTransform.anchoredPosition = new Vector2(0, -154);

            // ---- Player list (ScrollRect) ----
            Image listPanel = AddImage(Child("ListPanel", panelRt), _round, ListColor, Image.Type.Sliced);
            RectTransform listRt = listPanel.rectTransform;
            listRt.anchorMin = new Vector2(0, 0);
            listRt.anchorMax = new Vector2(1, 1);
            listRt.offsetMin = new Vector2(26, 96);
            listRt.offsetMax = new Vector2(-26, -196);

            var scroll = listPanel.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24;

            var viewportGo = Child("Viewport", listRt);
            var viewport = viewportGo.gameObject.AddComponent<Image>();
            viewport.color = new Color(0, 0, 0, 0);
            Stretch(viewport.rectTransform, 6, 6, 6, 6);
            viewportGo.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Child("Content", viewport.rectTransform);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.rectTransform;
            scroll.content = content;

            // ---- Footer buttons ----
            var footer = Child("Footer", panelRt);
            footer.anchorMin = new Vector2(0, 0);
            footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(0.5f, 0);
            footer.sizeDelta = new Vector2(-52, 68);
            footer.anchoredPosition = new Vector2(0, 20);
            var hlg = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            Button invite = MakeButton(Child("InviteButton", footer), _button, BlueBtn, "INVITE FRIENDS", 22);
            Button ready = MakeButton(Child("ReadyButton", footer), _button, GreenBtn, "READY UP", 22);
            Button leave = MakeButton(Child("LeaveButton", footer), _button, RedBtn, "LEAVE LOBBY", 22);
            Button start = MakeButton(Child("StartButton", footer), _button, GoldBtn, "START GAME", 22);
            TextMeshProUGUI readyLabel = ready.GetComponentInChildren<TextMeshProUGUI>();

            foreach (Button b in new[] { invite, ready, leave, start })
                b.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // ---- Wire LobbyUI ----
            SetRef(lobbyUI, "panelGroup", panelGroup);
            SetRef(lobbyUI, "panelRect", panelRt);
            SetRef(lobbyUI, "titleText", title);
            SetRef(lobbyUI, "lobbyNameText", lobbyName);
            SetRef(lobbyUI, "playerCountText", count);
            SetRef(lobbyUI, "listContent", content);
            SetRef(lobbyUI, "playerItemPrefab", prefab.GetComponent<LobbyPlayerItemUI>());
            SetRef(lobbyUI, "inviteButton", invite);
            SetRef(lobbyUI, "leaveButton", leave);
            SetRef(lobbyUI, "startButton", start);
            SetRef(lobbyUI, "readyButton", ready);
            SetRef(lobbyUI, "readyButtonLabel", readyLabel);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // =================================================================== Helpers

        private static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt, float l, float t, float r, float b)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static Image AddImage(RectTransform rt, Sprite sprite, Color color, Image.Type type)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = type;
            if (sprite != null && type == Image.Type.Sliced)
                img.pixelsPerUnitMultiplier = 1f;
            return img;
        }

        private static TextMeshProUGUI AddText(RectTransform rt, string text, float size, Color color, TextAlignmentOptions align, FontStyles style)
        {
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button MakeButton(RectTransform rt, Sprite sprite, Color color, string label, float labelSize)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;

            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            rt.gameObject.AddComponent<ButtonHoverEffect>();

            TextMeshProUGUI txt = AddText(Child("Label", rt), label, labelSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(txt.rectTransform, 6, 2, 6, 2);
            return btn;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[LobbyUIBuilder] Field '{field}' not found on {target.GetType().Name}.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
