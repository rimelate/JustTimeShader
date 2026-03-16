using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class VRCLocalDateOverlayWindow : EditorWindow
{
    private static readonly string[] SupportedShaders =
    {
        "JustTimeShader",
        "JustTimeShader_liltoon",
    };

    private static readonly int DateEnableId    = Shader.PropertyToID("_DateEnable");
    private static readonly int DateTexSTId      = Shader.PropertyToID("_DateTex_ST");
    private static readonly int DateFormatId    = Shader.PropertyToID("_DateFormat");
    private static readonly int DateShowFlagsId = Shader.PropertyToID("_DateShowFlags");

    private enum DateFormat { YMD_Slash = 0, MDY_Slash = 1, DMY_Slash = 2, YMD_Hyphen = 3 }

    private enum DragMode
    {
        None, Move,
        Left, Right, Top, Bottom,
        TopLeft, TopRight, BottomLeft, BottomRight,
        Create,
    }

    // ─── マテリアル ───
    private Material   _material;
    private Material[] _availableMaterials;
    private string[]   _materialNames;
    private int        _materialIndex;

    // ─── UI 状態 ───
    private Texture  _backgroundOverride;
    private Vector2  _leftScroll;
    private bool     _selectionDirty = true;
    private bool     _lockAspect;
    private float    _aspectRatio = 5f;
    private bool     _pendingSave;

    // ─── プレビューのドラッグ状態 ───
    // プレビュー領域の Rect（DrawPreviewPanel の Repaint 時に記録）
    private Rect    _previewPanelRect;
    private DragMode _dragMode       = DragMode.None;
    private Vector2  _dragStartMouse;
    private Vector4  _dragStartRect;
    private Rect     _dragStartImageRect;
    private Vector2  _createStartUv;
    private bool     _didDrag;

    // ─── ズーム＆パン ───
    private float   _zoom              = 1f;
    private Vector2 _panOffset         = Vector2.zero;
    private bool    _isPanning;
    private Vector2 _panDragStart;
    private Vector2 _panOffsetAtPanStart;

    private const float ZoomMin        = 0.05f;
    private const float ZoomMax        = 32f;

    private const float LeftPanelWidth = 320f;
    private const float HandleSize     = 12f;
    private const float MinRectSize    = 0.01f;

    // ─── ウィンドウ開閉 ───
    [MenuItem("Tools/JustTimeShaderEditor/Local Date Overlay Adjuster")]
    private static void OpenWindow()
    {
        var w = GetWindow<VRCLocalDateOverlayWindow>("Date Overlay");
        w.minSize = new Vector2(900f, 520f);
        w.TryAdoptSelection(false);
    }

    public static void OpenWithMaterial(Material mat)
    {
        var w = GetWindow<VRCLocalDateOverlayWindow>("Date Overlay");
        w.minSize = new Vector2(900f, 520f);
        w.SetMaterial(mat, true);
        w.Focus();
    }

    // ─── Unity コールバック ───
    private void OnEnable()
    {
        _selectionDirty = true;
        wantsMouseMove  = true;   // MouseMove も受け取る（hotControl なしでも確実にイベントが来るように）
        RefreshSelectionMaterials(true);
    }

    private void OnSelectionChange()
    {
        _selectionDirty = true;
        RefreshSelectionMaterials(_material == null);
        Repaint();
    }

    private void OnLostFocus() => SaveMaterialIfNeeded();
    private void OnDisable()   => SaveMaterialIfNeeded();

    // ─────────────────────────────────────────────────
    //  OnGUI
    // ─────────────────────────────────────────────────
    private void OnGUI()
    {
        RefreshSelectionMaterials(false);

        // ★ ScrollView より前にプレビューのマウスイベントを処理する
        //    （ScrollView が evt.Use() を呼ぶ前に確実に受け取るため）
        if (Event.current.type != EventType.Layout)
            ProcessPreviewEvents();

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawLeftPanel();
            DrawPreviewPanel();
        }
    }

    // ─────────────────────────────────────────────────
    //  プレビューのイベント処理（OnGUI 先頭で呼ぶ）
    // ─────────────────────────────────────────────────
    private void ProcessPreviewEvents()
    {
        if (_previewPanelRect.width < 2f) return;  // まだ Repaint されていない

        Texture bg        = GetPreviewTexture();
        Rect    imageRect = GetImageRect(_previewPanelRect, bg);
        Event   e         = Event.current;
        Vector2 mouse     = e.mousePosition;
        Vector2 panelCenter = _previewPanelRect.center;

        switch (e.type)
        {
            // ── マウスホイール：ズーム ──
            case EventType.ScrollWheel when _previewPanelRect.Contains(mouse):
            {
                float oldZoom = _zoom;
                // delta.y > 0 = ホイール下 = ズームアウト
                float newZoom = Mathf.Clamp(_zoom * Mathf.Pow(1.12f, -e.delta.y), ZoomMin, ZoomMax);
                float ratio   = newZoom / oldZoom;
                // マウス位置を中心にズーム
                _panOffset = _panOffset * ratio + (mouse - panelCenter) * (1f - ratio);
                _zoom      = newZoom;
                e.Use();
                Repaint();
                break;
            }

            // ── 中ボタン：パン開始 ──
            case EventType.MouseDown when e.button == 2:
            {
                if (!_previewPanelRect.Contains(mouse)) break;
                _isPanning           = true;
                _panDragStart        = mouse;
                _panOffsetAtPanStart = _panOffset;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                e.Use();
                break;
            }

            // ── 中ボタンドラッグ：パン ──
            case EventType.MouseDrag when e.button == 2 && _isPanning:
            {
                _panOffset = _panOffsetAtPanStart + (mouse - _panDragStart);
                e.Use();
                Repaint();
                break;
            }

            // ── 中ボタンアップ：パン終了 ──
            case EventType.MouseUp when e.button == 2 && _isPanning:
            {
                _isPanning            = false;
                GUIUtility.hotControl = 0;
                e.Use();
                break;
            }

            // ── 左ボタンダウン：オーバーレイ操作 ──
            case EventType.MouseDown when e.button == 0:
            {
                if (_material == null || !IsCompatibleMaterial(_material)) break;
                if (!imageRect.Contains(mouse)) break;

                Rect overlayRect = UVToRect(imageRect, GetDateRect());
                DragMode mode = GetDragMode(mouse, overlayRect);
                _dragMode           = (mode != DragMode.None) ? mode : DragMode.Create;
                _dragStartMouse     = mouse;
                _dragStartRect      = GetDateRect();
                _dragStartImageRect = imageRect;
                _createStartUv      = ScreenToUV(imageRect, mouse);
                _didDrag            = false;

                // hotControl を占有して ScrollView にドラッグを奪わせない
                GUIUtility.hotControl     = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.keyboardControl = 0;

                if (_dragMode == DragMode.Create)
                {
                    var seed = new Vector4(_createStartUv.x, _createStartUv.y, MinRectSize, MinRectSize);
                    ApplyRect(SanitizeRect(seed, false), false);
                }

                e.Use();
                break;
            }

            // ── ドラッグ中 ──
            case EventType.MouseDrag when e.button == 0 && _dragMode != DragMode.None:
            {
                _didDrag = true;

                float iw = Mathf.Max(1f, _dragStartImageRect.width);
                float ih = Mathf.Max(1f, _dragStartImageRect.height);
                var uvDelta = new Vector2(
                    (mouse.x - _dragStartMouse.x) / iw,
                    (_dragStartMouse.y - mouse.y) / ih);

                Vector4 r = _dragStartRect;
                switch (_dragMode)
                {
                    case DragMode.Move:
                        // サイズを維持したまま位置だけ動かす
                        r.x = Mathf.Clamp(_dragStartRect.x + uvDelta.x, 0f, Mathf.Max(0f, 1f - _dragStartRect.z));
                        r.y = Mathf.Clamp(_dragStartRect.y + uvDelta.y, 0f, Mathf.Max(0f, 1f - _dragStartRect.w));
                        break;
                    case DragMode.Left:        r.x += uvDelta.x; r.z -= uvDelta.x; break;
                    case DragMode.Right:       r.z += uvDelta.x; break;
                    case DragMode.Top:         r.w += uvDelta.y; break;
                    case DragMode.Bottom:      r.y += uvDelta.y; r.w -= uvDelta.y; break;
                    case DragMode.TopLeft:     r.x += uvDelta.x; r.z -= uvDelta.x; r.w += uvDelta.y; break;
                    case DragMode.TopRight:    r.z += uvDelta.x; r.w += uvDelta.y; break;
                    case DragMode.BottomLeft:  r.x += uvDelta.x; r.z -= uvDelta.x; r.y += uvDelta.y; r.w -= uvDelta.y; break;
                    case DragMode.BottomRight: r.z += uvDelta.x; r.y += uvDelta.y; r.w -= uvDelta.y; break;
                    case DragMode.Create:
                    {
                        Vector2 end = ScreenToUV(_dragStartImageRect, mouse);
                        r.x = Mathf.Min(_createStartUv.x, end.x);
                        r.y = Mathf.Min(_createStartUv.y, end.y);
                        r.z = Mathf.Max(MinRectSize, Mathf.Abs(end.x - _createStartUv.x));
                        r.w = Mathf.Max(MinRectSize, Mathf.Abs(end.y - _createStartUv.y));
                        break;
                    }
                }

                ApplyRect(SanitizeRect(r, _lockAspect), false);
                e.Use();
                break;
            }

            // ── マウスアップ ──
            case EventType.MouseUp when e.button == 0 && _dragMode != DragMode.None:
            {
                bool save = _didDrag;
                GUIUtility.hotControl = 0;
                _dragMode  = DragMode.None;
                _didDrag   = false;
                e.Use();
                if (save) SaveMaterialIfNeeded();
                Repaint();
                break;
            }

            // ── カーソル更新（Repaint 時） ──
            case EventType.Repaint:
            {
                // 中ボタンパン中は移動カーソル
                if (_isPanning)
                {
                    EditorGUIUtility.AddCursorRect(_previewPanelRect, MouseCursor.Pan);
                    break;
                }
                if (_material != null && IsCompatibleMaterial(_material))
                {
                    Rect overlayRect = UVToRect(imageRect, GetDateRect());
                    DragMode hot     = GetDragMode(mouse, overlayRect);
                    MouseCursor cur  = _dragMode != DragMode.None
                        ? CursorForMode(_dragMode)
                        : (hot == DragMode.None ? MouseCursor.ArrowPlus : CursorForMode(hot));
                    EditorGUIUtility.AddCursorRect(imageRect, cur);
                }
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  左パネル
    // ─────────────────────────────────────────────────
    private void DrawLeftPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftPanelWidth)))
        {
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            EditorGUILayout.LabelField("VRC Local Date Overlay", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawDropZone();
            DrawMaterialSelector();
            EditorGUILayout.Space(6f);

            if (_material == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a material that uses JustTimeShader or JustTimeShader_liltoon.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!IsCompatibleMaterial(_material))
            {
                EditorGUILayout.HelpBox("The selected material uses an unsupported shader.", MessageType.Error);
                if (GUILayout.Button("Adopt Compatible Material From Selection"))
                    TryAdoptSelection(true);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawHeaderButtons();
            EditorGUILayout.Space(8f);
            DrawBasicSection();
            EditorGUILayout.Space(8f);
            DrawBackgroundSection();
            EditorGUILayout.Space(8f);
            DrawRectSection();

            EditorGUILayout.EndScrollView();
        }
    }

    // ─────────────────────────────────────────────────
    //  プレビューパネル（描画のみ、イベント処理は ProcessPreviewEvents）
    // ─────────────────────────────────────────────────
    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            // ─ ヘッダー行：ラベル・ズーム表示・リセットボタン ─
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(
                    string.Format("{0:F0}%", _zoom * 100f),
                    new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight },
                    GUILayout.Width(46f));
                if (GUILayout.Button("1:1",   GUILayout.Width(28f))) { _zoom = 1f; _panOffset = Vector2.zero; Repaint(); }
                if (GUILayout.Button("Fit",   GUILayout.Width(28f))) { _zoom = 1f; _panOffset = Vector2.zero; Repaint(); }
            }

            Rect previewRect = GUILayoutUtility.GetRect(10f, 100000f, 10f, 100000f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // Layout イベント以外なら _previewPanelRect を更新
            if (Event.current.type != EventType.Layout)
                _previewPanelRect = previewRect;

            // 背景色とチェッカーは previewRect ちょうどなのでクリップ不要
            EditorGUI.DrawRect(previewRect, new Color(0.13f, 0.13f, 0.13f));
            DrawChecker(previewRect, new Color(0.19f, 0.19f, 0.19f), new Color(0.14f, 0.14f, 0.14f), 16f);

            Texture background = GetPreviewTexture();
            // imageRect はズーム時に previewRect を超えることがあるので
            // GUI.BeginClip で描画をプレビュー領域内に限定する
            Rect imageRect = GetImageRect(previewRect, background);

            GUI.BeginClip(previewRect);
            {
                // BeginClip 内では座標が previewRect.position 分だけシフトする
                Vector2 off       = previewRect.position;
                Rect    localImg  = new Rect(imageRect.x - off.x, imageRect.y - off.y,
                                            imageRect.width, imageRect.height);
                Rect    localArea = new Rect(0f, 0f, previewRect.width, previewRect.height);

                if (background != null)
                {
                    GUI.DrawTexture(localImg, background, ScaleMode.StretchToFill, false);
                }
                else
                {
                    DrawCenteredLabel(localArea, "No preview texture\nScroll to zoom  /  Middle-drag to pan");
                }

                if (_material != null && IsCompatibleMaterial(_material))
                {
                    Vector4 uvRect      = GetDateRect();
                    Rect    localOverlay = UVToRect(localImg, uvRect);

                    // オーバーレイ矩形の描画
                    EditorGUI.DrawRect(localOverlay, new Color(0.1f, 0.7f, 1f, 0.15f));
                    Handles.BeginGUI();
                    Handles.color = new Color(0.1f, 0.8f, 1f, 1f);
                    Handles.DrawAAPolyLine(2f,
                        new Vector3(localOverlay.xMin, localOverlay.yMin),
                        new Vector3(localOverlay.xMax, localOverlay.yMin),
                        new Vector3(localOverlay.xMax, localOverlay.yMax),
                        new Vector3(localOverlay.xMin, localOverlay.yMax),
                        new Vector3(localOverlay.xMin, localOverlay.yMin));
                    Handles.EndGUI();

                    DrawHandles(localOverlay);
                }
            }
            GUI.EndClip();
        }
    }

    // ─────────────────────────────────────────────────
    //  左パネルのUI部品
    // ─────────────────────────────────────────────────
    private void DrawDropZone()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
        GUI.Box(rect, GUIContent.none);
        DrawCenteredLabel(rect, "Drop a material or GameObject here");

        Event evt = Event.current;
        if (!rect.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (Object obj in DragAndDrop.objectReferences)
            {
                if (TryAdoptObject(obj)) { evt.Use(); return; }
            }
        }
    }

    private void DrawMaterialSelector()
    {
        EditorGUI.BeginChangeCheck();
        Material next = (Material)EditorGUILayout.ObjectField("Material", _material, typeof(Material), false);
        if (EditorGUI.EndChangeCheck()) SetMaterial(next, true);

        if (_availableMaterials != null && _availableMaterials.Length > 0)
        {
            int nextIndex = EditorGUILayout.Popup("From Selection", _materialIndex, _materialNames);
            if (nextIndex != _materialIndex && nextIndex >= 0 && nextIndex < _availableMaterials.Length)
            {
                _materialIndex = nextIndex;
                SetMaterial(_availableMaterials[_materialIndex], false);
            }
        }
    }

    private void DrawHeaderButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Adopt Selection")) TryAdoptSelection(true);
            if (GUILayout.Button("Ping Material"))   EditorGUIUtility.PingObject(_material);
        }
    }

    private void DrawBasicSection()
    {
        EditorGUILayout.LabelField("Basic", EditorStyles.boldLabel);

        bool enabled     = _material.HasProperty(DateEnableId) && _material.GetFloat(DateEnableId) > 0.5f;
        bool nextEnabled = EditorGUILayout.Toggle("Enable Overlay", enabled);
        if (nextEnabled != enabled)
            SetMaterialFloat(DateEnableId, nextEnabled ? 1f : 0f);

        EditorGUILayout.Space(4f);

        // ── 表示コンポーネント ──
        int flags = _material.HasProperty(DateShowFlagsId) ? _material.GetInt(DateShowFlagsId) : 7;
        EditorGUILayout.LabelField("Display Components", EditorStyles.miniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            flags = DrawFlagToggle(flags, 1,  "Year");
            flags = DrawFlagToggle(flags, 2,  "Month");
            flags = DrawFlagToggle(flags, 4,  "Day");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            flags = DrawFlagToggle(flags, 8,  "Hour");
            flags = DrawFlagToggle(flags, 16, "Minute");
            flags = DrawFlagToggle(flags, 32, "Second");
        }
        if (_material.HasProperty(DateShowFlagsId))
            SetMaterialInt(DateShowFlagsId, flags);

        // ── 日付フォーマット（日付コンポーネントがある場合のみ）──
        if ((flags & 7) != 0 && _material.HasProperty(DateFormatId))
        {
            int rawFmt = _material.GetInt(DateFormatId);
            DateFormat format     = (DateFormat)Mathf.Clamp(rawFmt, 0, 3);
            DateFormat nextFormat = (DateFormat)EditorGUILayout.EnumPopup("Date Format", format);
            if (nextFormat != format)
                SetMaterialInt(DateFormatId, (int)nextFormat);
        }
    }

    private static int DrawFlagToggle(int flags, int bit, string label)
    {
        bool cur  = (flags & bit) != 0;
        bool next = GUILayout.Toggle(cur, label, GUILayout.ExpandWidth(true));
        return next ? (flags | bit) : (flags & ~bit);
    }

    private void DrawBackgroundSection()
    {
        EditorGUILayout.LabelField("Preview Source", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        Texture next = (Texture)EditorGUILayout.ObjectField("Background Override", _backgroundOverride, typeof(Texture), false);
        if (EditorGUI.EndChangeCheck()) { _backgroundOverride = next; Repaint(); }

        Texture active = GetPreviewTexture();
        EditorGUILayout.LabelField("Active Preview", active != null ? active.name : "None");
    }

    private void DrawRectSection()
    {
        EditorGUILayout.LabelField("Date Rect", EditorStyles.boldLabel);

        Vector4 rect = GetDateRect();
        EditorGUI.BeginChangeCheck();
        float x = EditorGUILayout.FloatField("X",      rect.x);
        float y = EditorGUILayout.FloatField("Y",      rect.y);
        float w = EditorGUILayout.FloatField("Width",  rect.z);
        float h = EditorGUILayout.FloatField("Height", rect.w);
        _lockAspect = EditorGUILayout.Toggle("Lock Aspect", _lockAspect);
        if (_lockAspect)
            _aspectRatio = Mathf.Max(0.01f, EditorGUILayout.FloatField("Aspect", _aspectRatio));

        if (EditorGUI.EndChangeCheck())
            ApplyRect(SanitizeRect(new Vector4(x, y, w, h), _lockAspect), true);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset"))
                ApplyRect(new Vector4(0.1f, 0.45f, 0.4f, 0.1f), true);

            if (GUILayout.Button("Fit Width"))
            {
                Vector4 cur = GetDateRect();
                cur.x = 0.05f;
                cur.z = 0.9f;
                ApplyRect(SanitizeRect(cur, false), true);
            }
        }
    }

    // ─────────────────────────────────────────────────
    //  ドラッグ補助
    // ─────────────────────────────────────────────────
    private DragMode GetDragMode(Vector2 mouse, Rect overlayRect)
    {
        // 角ハンドル
        if (HandleRect(overlayRect.xMin, overlayRect.yMin).Contains(mouse)) return DragMode.TopLeft;
        if (HandleRect(overlayRect.xMax, overlayRect.yMin).Contains(mouse)) return DragMode.TopRight;
        if (HandleRect(overlayRect.xMin, overlayRect.yMax).Contains(mouse)) return DragMode.BottomLeft;
        if (HandleRect(overlayRect.xMax, overlayRect.yMax).Contains(mouse)) return DragMode.BottomRight;

        // 辺ハンドル
        float mid = HandleSize;
        Rect left   = new Rect(overlayRect.xMin - mid * 0.5f, overlayRect.yMin + mid, mid, Mathf.Max(0f, overlayRect.height - mid * 2f));
        Rect right  = new Rect(overlayRect.xMax - mid * 0.5f, overlayRect.yMin + mid, mid, Mathf.Max(0f, overlayRect.height - mid * 2f));
        Rect top    = new Rect(overlayRect.xMin + mid, overlayRect.yMin - mid * 0.5f, Mathf.Max(0f, overlayRect.width - mid * 2f), mid);
        Rect bottom = new Rect(overlayRect.xMin + mid, overlayRect.yMax - mid * 0.5f, Mathf.Max(0f, overlayRect.width - mid * 2f), mid);

        if (left.Contains(mouse))   return DragMode.Left;
        if (right.Contains(mouse))  return DragMode.Right;
        if (top.Contains(mouse))    return DragMode.Top;
        if (bottom.Contains(mouse)) return DragMode.Bottom;

        // 内部 = 移動
        if (overlayRect.Contains(mouse)) return DragMode.Move;
        return DragMode.None;
    }

    private void DrawHandles(Rect r)
    {
        // 角4つ
        EditorGUI.DrawRect(HandleRect(r.xMin, r.yMin), Color.white);
        EditorGUI.DrawRect(HandleRect(r.xMax, r.yMin), Color.white);
        EditorGUI.DrawRect(HandleRect(r.xMin, r.yMax), Color.white);
        EditorGUI.DrawRect(HandleRect(r.xMax, r.yMax), Color.white);
        // 辺中央4つ
        EditorGUI.DrawRect(HandleRect((r.xMin + r.xMax) * 0.5f, r.yMin), Color.white);
        EditorGUI.DrawRect(HandleRect((r.xMin + r.xMax) * 0.5f, r.yMax), Color.white);
        EditorGUI.DrawRect(HandleRect(r.xMin, (r.yMin + r.yMax) * 0.5f), Color.white);
        EditorGUI.DrawRect(HandleRect(r.xMax, (r.yMin + r.yMax) * 0.5f), Color.white);
    }

    private static MouseCursor CursorForMode(DragMode mode)
    {
        switch (mode)
        {
            case DragMode.Left:
            case DragMode.Right:       return MouseCursor.ResizeHorizontal;
            case DragMode.Top:
            case DragMode.Bottom:      return MouseCursor.ResizeVertical;
            case DragMode.TopLeft:
            case DragMode.BottomRight: return MouseCursor.ResizeUpLeft;
            case DragMode.TopRight:
            case DragMode.BottomLeft:  return MouseCursor.ResizeUpRight;
            case DragMode.Move:        return MouseCursor.MoveArrow;
            default:                   return MouseCursor.Arrow;
        }
    }

    private static Rect HandleRect(float x, float y) =>
        new Rect(x - HandleSize * 0.5f, y - HandleSize * 0.5f, HandleSize, HandleSize);

    private static Vector2 ScreenToUV(Rect imageRect, Vector2 point)
    {
        float u = Mathf.Clamp01((point.x - imageRect.xMin) / Mathf.Max(1f, imageRect.width));
        float v = Mathf.Clamp01((imageRect.yMax - point.y) / Mathf.Max(1f, imageRect.height));
        return new Vector2(u, v);
    }

    // ─────────────────────────────────────────────────
    //  マテリアル管理
    // ─────────────────────────────────────────────────
    private void RefreshSelectionMaterials(bool adoptFirst)
    {
        if (!_selectionDirty) return;

        var list = new List<Material>();
        foreach (Object obj in Selection.objects)
            CollectMaterials(obj, list);

        if (list.Count == 0)
        {
            _availableMaterials = null;
            _materialNames      = null;
            _materialIndex      = 0;
            _selectionDirty     = false;
            return;
        }

        _availableMaterials = list.ToArray();
        _materialNames = new string[_availableMaterials.Length];
        for (int i = 0; i < _availableMaterials.Length; i++)
            _materialNames[i] = _availableMaterials[i].name;

        if (_material != null)
        {
            for (int i = 0; i < _availableMaterials.Length; i++)
            {
                if (_availableMaterials[i] == _material)
                {
                    _materialIndex  = i;
                    _selectionDirty = false;
                    return;
                }
            }
        }

        if (adoptFirst)
        {
            _materialIndex = 0;
            SetMaterial(_availableMaterials[0], false);
        }

        _selectionDirty = false;
    }

    private void CollectMaterials(Object obj, List<Material> list)
    {
        if (obj is Material mat && IsCompatibleMaterial(mat))
        {
            if (!list.Contains(mat)) list.Add(mat);
            return;
        }
        if (obj is GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (IsCompatibleMaterial(m) && !list.Contains(m)) list.Add(m);
        }
    }

    private bool TryAdoptSelection(bool repaint)
    {
        _selectionDirty = true;
        RefreshSelectionMaterials(false);
        if (_availableMaterials == null || _availableMaterials.Length == 0) return false;
        SetMaterial(_availableMaterials[0], repaint);
        _materialIndex = 0;
        return true;
    }

    private bool TryAdoptObject(Object obj)
    {
        if (obj is Material mat && IsCompatibleMaterial(mat)) { SetMaterial(mat, true); return true; }
        if (obj is GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (IsCompatibleMaterial(m)) { SetMaterial(m, true); return true; }
        }
        return false;
    }

    private void SetMaterial(Material mat, bool repaint)
    {
        _material = mat;
        if (repaint) Repaint();
    }

    private static bool IsCompatibleMaterial(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        foreach (string n in SupportedShaders)
            if (mat.shader.name == n) return true;
        return false;
    }

    private Texture GetPreviewTexture()
    {
        if (_backgroundOverride != null) return _backgroundOverride;
        if (_material == null) return null;
        if (_material.HasProperty("_MainTex")) return _material.GetTexture("_MainTex");
        if (_material.HasProperty("_BaseMap"))  return _material.GetTexture("_BaseMap");
        return null;
    }

    // ─────────────────────────────────────────────────
    //  UV Rect ↔ _DateTex_ST 変換
    //  ST: (sx=1/w, sy=1/h, ox=-x/w, oy=-y/h)
    //  Rect: (x=-ox/sx, y=-oy/sy, w=1/sx, h=1/sy)
    // ─────────────────────────────────────────────────
    private Vector4 GetDateRect()
    {
        if (_material == null || !_material.HasProperty(DateTexSTId))
            return new Vector4(0.1f, 0.45f, 0.4f, 0.1f);

        Vector4 st = _material.GetVector(DateTexSTId);
        float sx = Mathf.Abs(st.x) > 1e-5f ? st.x : 1f;
        float sy = Mathf.Abs(st.y) > 1e-5f ? st.y : 1f;
        float w  = 1f / sx;
        float h  = 1f / sy;
        float x  = -st.z / sx;
        float y  = -st.w / sy;
        return new Vector4(x, y, w, h);
    }

    /// <summary>UV Rect を _DateTex_ST に変換してマテリアルへ書き込む（Undo 付き）</summary>
    private void ApplyRect(Vector4 rect, bool saveNow)
    {
        if (_material == null || !_material.HasProperty(DateTexSTId)) return;

        float w  = Mathf.Max(rect.z, MinRectSize);
        float h  = Mathf.Max(rect.w, MinRectSize);
        float sx = 1f / w;
        float sy = 1f / h;
        float ox = -rect.x / w;
        float oy = -rect.y / h;
        var st = new Vector4(sx, sy, ox, oy);

        Undo.RecordObject(_material, "Move Date Rect");
        _material.SetVector(DateTexSTId, st);
        EditorUtility.SetDirty(_material);
        _pendingSave = true;
        if (saveNow) SaveMaterialIfNeeded();
        Repaint();
    }

    private void SetMaterialFloat(int id, float value)
    {
        if (_material == null || !_material.HasProperty(id)) return;
        Undo.RecordObject(_material, "Edit Date Overlay");
        _material.SetFloat(id, value);
        EditorUtility.SetDirty(_material);
        _pendingSave = true;
        SaveMaterialIfNeeded();
        Repaint();
    }

    private void SetMaterialInt(int id, int value)
    {
        if (_material == null || !_material.HasProperty(id)) return;
        if (_material.GetInt(id) == value) return;
        Undo.RecordObject(_material, "Edit Date Overlay");
        _material.SetInt(id, value);
        EditorUtility.SetDirty(_material);
        _pendingSave = true;
        SaveMaterialIfNeeded();
        Repaint();
    }

    private void SaveMaterialIfNeeded()
    {
        if (!_pendingSave || _material == null) return;
        if (EditorUtility.IsPersistent(_material))
            AssetDatabase.SaveAssetIfDirty(_material);
        _pendingSave = false;
    }

    // ─────────────────────────────────────────────────
    //  数学ユーティリティ
    // ─────────────────────────────────────────────────
    private Vector4 SanitizeRect(Vector4 r, bool lockAspect)
    {
        r.z = Mathf.Max(MinRectSize, r.z);
        r.w = Mathf.Max(MinRectSize, r.w);
        if (lockAspect)
            r.w = Mathf.Max(MinRectSize, r.z / Mathf.Max(0.01f, _aspectRatio));
        r.x = Mathf.Clamp(r.x, 0f, 1f - MinRectSize);
        r.y = Mathf.Clamp(r.y, 0f, 1f - MinRectSize);
        r.z = Mathf.Clamp(r.z, MinRectSize, 1f - r.x);
        r.w = Mathf.Clamp(r.w, MinRectSize, 1f - r.y);
        return r;
    }

    private static Rect FitRect(Rect outer, float w, float h)
    {
        float scale = Mathf.Min(outer.width / Mathf.Max(1f, w), outer.height / Mathf.Max(1f, h));
        float dw = w * scale, dh = h * scale;
        return new Rect(outer.x + (outer.width - dw) * 0.5f, outer.y + (outer.height - dh) * 0.5f, dw, dh);
    }

    /// <summary>ズーム・パンを適用した imageRect を返す（ProcessPreviewEvents と DrawPreviewPanel で共有）</summary>
    private Rect GetImageRect(Rect previewRect, Texture bg)
    {
        // ズーム等倍時のベースサイズ
        float baseW, baseH;
        if (bg != null)
        {
            float scale = Mathf.Min(previewRect.width  / Mathf.Max(1f, bg.width),
                                    previewRect.height / Mathf.Max(1f, bg.height));
            baseW = bg.width  * scale;
            baseH = bg.height * scale;
        }
        else
        {
            baseW = previewRect.width;
            baseH = previewRect.height;
        }

        float w  = baseW * _zoom;
        float h  = baseH * _zoom;
        float cx = previewRect.center.x + _panOffset.x;
        float cy = previewRect.center.y + _panOffset.y;
        return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
    }

    /// <summary>UV座標（Y=0が画像下端）→ GUI座標（Y=0が画面上端）</summary>
    private static Rect UVToRect(Rect img, Vector4 uv)
    {
        return new Rect(
            img.x + uv.x * img.width,
            img.y + (1f - uv.y - uv.w) * img.height,
            uv.z * img.width,
            uv.w * img.height);
    }

    private static void DrawCenteredLabel(Rect rect, string text)
    {
        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.85f, 0.85f, 0.85f) },
        };
        GUI.Label(rect, text, style);
    }

    private static void DrawChecker(Rect rect, Color a, Color b, float size)
    {
        int cols = Mathf.CeilToInt(rect.width  / size);
        int rows = Mathf.CeilToInt(rect.height / size);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                Color c    = ((x + y) & 1) == 0 ? a : b;
                Rect  cell = new Rect(rect.x + x * size, rect.y + y * size, size, size);
                EditorGUI.DrawRect(cell, c);
            }
    }
}
