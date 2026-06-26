#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEditor;
using System.Linq;
using System;
using Object = UnityEngine.Object;

#if UNITY_6000_2_OR_NEWER
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif

namespace Pinwheel.Poseidon
{
    public class ShaderBrowserTreeView : TreeView
    {
        public static class Styles
        {
            public static readonly Color32 colorRowOdd = new Color32(55, 55, 55, 255);
            public static readonly Color32 colorRowEven = new Color32(65, 65, 65, 255);
            public static readonly Color32 colorRowSelected = new Color32(62, 95, 150, 255);
            public static readonly Texture2D checkmarkIcon = Resources.Load<Texture2D>("Poseidon/Textures/CheckmarkIcon");

            public static Rect ShrinkRect(Rect r, int pixel)
            {
                return new RectOffset(pixel, pixel, pixel, pixel).Remove(r);
            }
        }

        public class Item : TreeViewItem
        {
            public ShaderDesc desc { get; set; }

            public Item(int id, int depth, string displayName = null) : base(id, depth, displayName)
            {

            }
        }

        private WaterShaderLibrary library;

        protected ShaderBrowserTreeView(WaterShaderLibrary lib, TreeViewState state) : base(state)
        {
            library = lib;
            Init();
        }

        protected ShaderBrowserTreeView(WaterShaderLibrary lib, TreeViewState state, MultiColumnHeader multiColumnHeader) : base(state, multiColumnHeader)
        {
            library = lib;
            Init();
        }

        public static ShaderBrowserTreeView Create(WaterShaderLibrary lib)
        {
            MultiColumnHeaderState.Column[] columns = new MultiColumnHeaderState.Column[]
            {
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent(" "), allowToggleVisibility = false, width = 20},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Shader"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Style"),allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Mesh"),  allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Priority"), allowToggleVisibility = false},

                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Mesh Noise"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Ripples"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Sine Wave"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Wave Mask"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Normal Maps"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Light Absorption"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Fresnel"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Foam"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Foam HQ"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Foam Crest"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Foam Slope"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Reflection"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Refraction"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Caustic"), allowToggleVisibility = false},
                new MultiColumnHeaderState.Column(){headerContent = new GUIContent("Special Features"),  allowToggleVisibility = false},
            };
            MultiColumnHeaderState headerState = new MultiColumnHeaderState(columns);
            MultiColumnHeader header = new MultiColumnHeader(headerState);
            header.ResizeToFit();

            TreeViewState state = new TreeViewState();
            ShaderBrowserTreeView treeView = new ShaderBrowserTreeView(lib, state, header);
            treeView.useScrollView = true;
            treeView.showAlternatingRowBackgrounds = true;
            treeView.showBorder = true;
            treeView.rowHeight = 24;

            header.sortingChanged += (header) => { treeView.Reload(); };

            return treeView;
        }

        protected override TreeViewItem BuildRoot()
        {
            int itemId = 0;
            Item root = new Item(itemId++, -1);
            foreach (ShaderDesc d in library.m_shaderDescriptions)
            {
                Item item = new Item(itemId++, 0);
                item.desc = d;
                root.AddChild(item);
            }

            return root;
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            List<TreeViewItem> items = new List<TreeViewItem>();
            if (root.children != null)
                items.AddRange(root.children);

            if (!string.IsNullOrEmpty(searchString))
                items = items.Where(item => DoesItemMatchSearch(item, searchString)).ToList();

            int sortedColumnIndex = multiColumnHeader.sortedColumnIndex;
            if (sortedColumnIndex >= 0 &&
                m_sortDelegates[sortedColumnIndex] != null)
            {
                MultiColumnHeaderState.Column column = multiColumnHeader.state.columns[sortedColumnIndex];
                bool sortAscending = column.sortedAscending;
                items.Sort(m_sortDelegates[sortedColumnIndex]);

                if (!sortAscending)
                {
                    items.Reverse();
                }
            }

            return items;
        }

        protected delegate void CellGUIHandler(Rect cellRect, ShaderDesc desc);
        protected CellGUIHandler[] m_cellGuiDelegates;
        protected System.Comparison<TreeViewItem>[] m_sortDelegates;

        protected void Init()
        {
            m_cellGuiDelegates = new CellGUIHandler[]
            {
                CellGUI_Fav,
                CellGUI_Shader,
                CellGUI_Style,
                CellGUI_Mesh,
                CellGUI_Priority,
                CellGUI_MeshNoise,
                CellGUI_Ripples,
                CellGUI_SineWave,
                CellGUI_WaveMask,
                CellGUI_NormalMaps,
                CellGUI_LightAbsorption,
                CellGUI_Fresnel,
                CellGUI_Foam,
                CellGUI_FoamHQ,
                CellGUI_FoamCrest,
                CellGUI_FoamSlope,
                CellGUI_Reflection,
                CellGUI_Refraction,
                CellGUI_Caustic,
                CellGUI_CustomFeatures,
            };

            m_sortDelegates = new System.Comparison<TreeViewItem>[]
            {
                null,
                null,
                null,
                null,
                null,
                null,
                null
            };
        }

        //protected int SortName(TreeViewItem x, TreeViewItem y)
        //{
        //    return string.Compare((x as Item).note.name, (y as Item).note.name);
        //}

        //protected int SortTodo(TreeViewItem x, TreeViewItem y)
        //{
        //    int completed, total;
        //    Note noteX = (x as Item).note;
        //    noteX.GetChecklistsProgress(out completed, out total);
        //    int remainingX = total - completed;

        //    Note noteY = (y as Item).note;
        //    noteY.GetChecklistsProgress(out completed, out total);
        //    int remainingY = total - completed;

        //    return remainingX.CompareTo(remainingY);
        //}

        //protected int SortDescription(TreeViewItem x, TreeViewItem y)
        //{
        //    return string.Compare((x as Item).note.description, (y as Item).note.description);
        //}

        //protected int SortAttachTarget(TreeViewItem x, TreeViewItem y)
        //{
        //    return string.Compare((x as Item).note.attachTarget.targetType.ToString(), (y as Item).note.attachTarget.targetType.ToString());
        //}

        //protected int SortLinkage(TreeViewItem x, TreeViewItem y)
        //{
        //    int xVal = NoteUtils.IsNoteConnectedToTrelloCard((x as Item).note) ? 1 : 0;
        //    int yVal = NoteUtils.IsNoteConnectedToTrelloCard((y as Item).note) ? 1 : 0;
        //    return xVal.CompareTo(yVal);
        //}

        protected override void RowGUI(RowGUIArgs args)
        {
            Item item = (Item)args.item;
            ShaderDesc template = item.desc;

            int columnsCount = args.GetNumVisibleColumns();
            for (int iC = 0; iC < columnsCount; ++iC)
            {
                int realColumnIndex = args.GetColumn(iC);
                Rect cellRect = args.GetCellRect(iC);
                if (realColumnIndex >= 0 && realColumnIndex < m_cellGuiDelegates.Length)
                {
                    CellGUIHandler guiDelegate = m_cellGuiDelegates[realColumnIndex];
                    guiDelegate.Invoke(cellRect, template);
                }
                else
                {
                    EditorGUI.LabelField(cellRect, "No GUI delegate");
                }
            }
        }

        protected bool GetShaderFavState(ShaderDesc sd)
        {
            return EditorPrefs.GetBool($"fav-{sd.name}", false);
        }

        protected void SetShaderFavState(ShaderDesc sd, bool fav)
        {
            EditorPrefs.SetBool($"fav-{sd.name}", fav);
        }

        private static Texture2D ICON_STAR = Resources.Load<Texture2D>("Poseidon/Textures/StarIcon");
        private static Texture2D ICON_STAR_OUTLINE = Resources.Load<Texture2D>("Poseidon/Textures/StarIconOutline");
        private static GUIContent FAVED_GUI_CONTENT = new GUIContent(ICON_STAR);
        private static GUIContent NOT_FAVED_GUI_CONTENT = new GUIContent(ICON_STAR_OUTLINE);

        protected void CellGUI_Fav(Rect r, ShaderDesc d)
        {
            bool isFav = GetShaderFavState(d);
            GUIContent guiContent = isFav ? FAVED_GUI_CONTENT : NOT_FAVED_GUI_CONTENT;
            GUI.contentColor = isFav ?
                new Color32(255, 220, 100, 255) :
                new Color32(75, 75, 75, 255);
            if (GUI.Button(r, guiContent, PEditorCommon.CenteredLabel))
            {
                SetShaderFavState(d, !isFav);
            }
            GUI.contentColor = Color.white;
        }

        protected void CellGUI_Shader(Rect r, ShaderDesc d)
        {
            GUIContent content = EditorGUIUtility.TrTextContent(d.name, d.name);
            EditorGUI.LabelField(r, content);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (GUI.Button(r, "", GUIStyle.none))
            {
                EditorGUIUtility.PingObject(d.shader);
            }
        }

        protected void CellGUI_Mesh(Rect r, ShaderDesc d)
        {
            EditorGUI.LabelField(r, ObjectNames.NicifyVariableName(d.meshType.ToString()));
        }

        protected void CellGUI_Style(Rect r, ShaderDesc d)
        {
            EditorGUI.LabelField(r, ObjectNames.NicifyVariableName(d.visualStyle.ToString()));
        }

        protected void CellGUI_Priority(Rect r, ShaderDesc d)
        {

            EditorGUI.LabelField(r, ObjectNames.NicifyVariableName(d.priority.ToString()));
        }

        private void DrawFeatureCheckmark(Rect r, ShaderDesc d, ShaderDesc.WaterFeature f)
        {
            if (d.features.HasFlag(f))
            {
                GUIContent checkLabel = EditorGUIUtility.TrTextContentWithIcon("", Styles.checkmarkIcon);
                EditorGUI.LabelField(r, checkLabel);
            }
        }

        protected void CellGUI_MeshNoise(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.MeshNoise);
        }

        protected void CellGUI_Ripples(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.Ripples);
        }

        protected void CellGUI_SineWave(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.SineWave);
        }

        protected void CellGUI_WaveMask(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.WaveMask);
        }

        protected void CellGUI_NormalMaps(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.NormalMaps);
        }

        protected void CellGUI_LightAbsorption(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.LightAbsorption);
        }

        protected void CellGUI_Fresnel(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.Fresnel);
        }

        protected void CellGUI_Foam(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.Foam);
        }

        protected void CellGUI_FoamHQ(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.FoamHQ);
        }

        protected void CellGUI_FoamCrest(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.FoamCrest);
        }

        protected void CellGUI_FoamSlope(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.FoamSlope);
        }

        protected void CellGUI_Reflection(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.Reflection);
        }

        protected void CellGUI_Refraction(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.Refraction);
        }

        protected void CellGUI_Caustic(Rect r, ShaderDesc d)
        {
            DrawFeatureCheckmark(r, d, ShaderDesc.WaterFeature.Caustic);
        }

        protected void CellGUI_CustomFeatures(Rect r, ShaderDesc d)
        {
            GUIContent guiContent = new GUIContent(d.specialFeatures, d.specialFeatures);
            EditorGUI.LabelField(r, guiContent);
        }

        protected void CellGUI_Actions(Rect r, ShaderDesc t)
        {
            //Rect buttonRect = new Rect()
            //{
            //    position = r.position,
            //    size = Vector2.one * r.height
            //};
            //if (GUIUtils.ButtonIcon(buttonRect, Icons.TRASH_BIN, "Pernamently Delete"))
            //{
            //    if (EditorUtility.DisplayDialog(
            //        "Confirm Delete",
            //        $"Delete '{Utilities.Ellipsis(n.name, 64)}' from database? This only delete the note from this local machine.",
            //        "Delete", "Cancel"))
            //    {
            //        NoteManager.instance.SetDirty();
            //        NoteManager.instance.RecordUndo("Delete note from database");
            //        NoteManager.instance.RemoveNote(n.id);
            //    }
            //}

            //buttonRect.x += r.height;
            //if (GUIUtils.ButtonIcon(buttonRect, Icons.EDIT_NOTE, "Edit"))
            //{
            //    NoteUI.ShowAsPopup(buttonRect, n);
            //}

            //buttonRect.x += r.height;
            //bool isNoteAttached = NoteUtils.IsNoteAttached(n);
            //if (Utilities.HasSingleObjectSelection())
            //{
            //    if (!isNoteAttached &&
            //        GUIUtils.ButtonIcon(buttonRect, Icons.PIN, "Attach to selected object"))
            //    {
            //        NoteUtils.AttachNote(n, Selection.activeObject);
            //        EditorApplication.RepaintHierarchyWindow();
            //        EditorApplication.RepaintProjectWindow();
            //    }
            //}

            //if (isNoteAttached &&
            //    GUIUtils.ButtonIcon(buttonRect, Icons.UNPIN, "Detach"))
            //{
            //    NoteUtils.DetachNote(n);
            //    EditorApplication.RepaintHierarchyWindow();
            //    EditorApplication.RepaintProjectWindow();
            //}
        }

        protected override bool DoesItemMatchSearch(TreeViewItem treeViewItem, string search)
        {
            Item item = (Item)treeViewItem;
            ShaderDesc template = item.desc;

            if (template.name.Contains(search, System.StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }

            return false;
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public ShaderDesc GetSelectedShader()
        {
            if (!HasSelection())
            {
                return null;
            }
            else
            {
                int id = GetSelection()[0];
                Item item = FindItem(id, rootItem) as Item;
                return item.desc;
            }

        }
    }
}

#endif