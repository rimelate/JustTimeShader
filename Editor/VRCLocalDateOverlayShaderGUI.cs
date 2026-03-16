using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom material inspector for JustTimeShader.
/// Registered via  CustomEditor "VRCLocalDateOverlayShaderGUI"  in the shader.
/// </summary>
public sealed class VRCLocalDateOverlayShaderGUI : ShaderGUI
{
    // ── Section foldout states ──────────────────────────────────────────────
    private bool _showOverlay   = true;
    private bool _showColor     = true;
    private bool _showFont      = true;
    private bool _showPlacement = true;

    // ── Format labels (displayed in the popup) ─────────────────────────────
    private static readonly GUIContent[] FormatLabels =
    {
        new GUIContent("YYYY/MM/DD", "年/月/日 (スラッシュ区切り)"),
        new GUIContent("MM/DD/YYYY", "月/日/年 (米国式)"),
        new GUIContent("DD/MM/YYYY", "日/月/年 (欧州式)"),
        new GUIContent("YYYY-MM-DD", "年-月-日 (ISO 8601)"),
    };

    // ── Colors ────────────────────────────────────────────────────────────
    private static readonly Color AccentBlue    = new Color(0.30f, 0.68f, 1.00f);
    private static readonly Color AccentGreen   = new Color(0.35f, 0.85f, 0.45f);
    private static readonly Color HeaderBg      = new Color(0.14f, 0.14f, 0.14f, 0.85f);
    private static readonly Color SeparatorLine = new Color(0.35f, 0.35f, 0.35f, 0.5f);

    // ── Main GUI ─────────────────────────────────────────────────────────

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
    {
        var mat = editor.target as Material;
        if (mat == null) { base.OnGUI(editor, props); return; }

        EditorGUIUtility.labelWidth = 160f;
        EditorGUI.BeginChangeCheck();

        // ── 1. Date Overlay ──────────────────────────────────────────────
        if (DrawSectionHeader(ref _showOverlay, "Date Overlay", AccentBlue))
        {
            var pEnable = FindProperty("_DateEnable", props);
            var pFormat = FindProperty("_DateFormat", props);

            using (new EditorGUI.IndentLevelScope())
            {
                // Enable toggle with colored indicator
                bool enabled    = pEnable.floatValue > 0.5f;
                bool nextEnabled = EditorGUILayout.Toggle(
                    new GUIContent("Enable Overlay", "日付オーバーレイを有効にします"), enabled);
                if (nextEnabled != enabled)
                    pEnable.floatValue = nextEnabled ? 1f : 0f;

                EditorGUILayout.Space(2f);

                // Format popup (cleaner than the default 0-3 slider)
                EditorGUI.BeginChangeCheck();
                int fmtIdx    = Mathf.Clamp(Mathf.RoundToInt(pFormat.floatValue), 0, FormatLabels.Length - 1);
                int nextFmt   = EditorGUILayout.Popup(
                    new GUIContent("Date Format", "表示する日付の書式"),
                    fmtIdx, FormatLabels);
                if (EditorGUI.EndChangeCheck())
                    pFormat.floatValue = nextFmt;

                // Format preview badge
                string[] samples =
                {
                    "YYYY/MM/DD",
                    "MM/DD/YYYY",
                    "DD/MM/YYYY",
                    "YYYY-MM-DD",
                };
                var previewRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                previewRect.x    += EditorGUI.indentLevel * 15f;
                previewRect.width -= EditorGUI.indentLevel * 15f;
                EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.08f, 0.9f));
                DrawBorder(previewRect, new Color(0.3f, 0.3f, 0.3f), 1f);
                var previewStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = AccentGreen },
                    fontSize  = 12,
                    fontStyle = FontStyle.Bold,
                };
                GUI.Label(previewRect, samples[Mathf.Clamp(nextFmt, 0, samples.Length - 1)], previewStyle);
            }
        }

        DrawSeparator();

        // ── 2. Color ─────────────────────────────────────────────────────
        if (DrawSectionHeader(ref _showColor, "Color", AccentBlue))
        {
            var pColor   = FindProperty("_Color",   props);
            var pBgColor = FindProperty("_BgColor", props);
            var pCutoff  = FindProperty("_Cutoff",  props);

            using (new EditorGUI.IndentLevelScope())
            {
                editor.ShaderProperty(pColor,   new GUIContent("Date Color",   "数字・区切り文字の色"));
                editor.ShaderProperty(pBgColor, new GUIContent("Background",   "背景色 (α=0 で透明)"));
                editor.ShaderProperty(pCutoff,  new GUIContent("Alpha Cutoff", "α値がこの閾値以下のピクセルを切り捨て"));
            }
        }

        DrawSeparator();

        // ── 3. Font ───────────────────────────────────────────────────────
        if (DrawSectionHeader(ref _showFont, "Font", AccentBlue))
        {
            var pUseTex = FindProperty("_DateUseFontTex", props, false);
            var pTex    = FindProperty("_DateFontTex",    props, false);
            var pCols   = FindProperty("_DateFontCols",   props, false);

            using (new EditorGUI.IndentLevelScope())
            {
                bool useTex = pUseTex != null && pUseTex.floatValue > 0.5f;
                if (pUseTex != null)
                {
                    bool next = EditorGUILayout.Toggle(
                        new GUIContent("Use Font Texture",
                            "チェックするとテクスチャのフォントを使用します\n" +
                            "オフの場合は手続き SDF 描画（テクスチャ不要）"), useTex);
                    if (next != useTex) pUseTex.floatValue = next ? 1f : 0f;
                    useTex = next;
                }

                if (useTex)
                {
                    if (pTex != null)
                        editor.ShaderProperty(pTex, new GUIContent("Font Texture",
                            "横 N 列の等幅スプライトシート\n" +
                            "列の並び: [0][1][2][3][4][5][6][7][8][9][/][-]"));
                    if (pCols != null)
                        editor.ShaderProperty(pCols, new GUIContent("Font Columns",
                            "テクスチャの横の列数 (デフォルト 12: 0-9 + / + -)"));

                    EditorGUILayout.HelpBox(
                        "フォントテクスチャのレイアウト:\n" +
                        "横一列に 12 キャラクターを等幅で並べてください\n" +
                        "左から: 0  1  2  3  4  5  6  7  8  9  /  -",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "手続き SDF 描画モード (テクスチャ不要)\n" +
                        "\"Use Font Texture\" をオンにすると任意のフォントが使えます",
                        MessageType.None);
                }
            }
        }

        DrawSeparator();

        // ── 4. Placement ─────────────────────────────────────────────────
        if (DrawSectionHeader(ref _showPlacement, "Placement", AccentBlue))
        {
            var pUVRect = FindProperty("_DateUVRect", props, false);

            using (new EditorGUI.IndentLevelScope())
            {
                // 現在の Rect 値を読み取り専用で表示
                if (pUVRect != null)
                {
                    var v = pUVRect.vectorValue;
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.LabelField("UV Rect",
                            string.Format("x:{0:F3}  y:{1:F3}  w:{2:F3}  h:{3:F3}", v.x, v.y, v.z, v.w));
                }

                EditorGUILayout.Space(4f);

                // ウィンドウを開くボタン
                if (GUILayout.Button("位置調整ウィンドウを開く...", GUILayout.Height(24f)))
                    VRCLocalDateOverlayWindow.OpenWithMaterial(mat);
            }
        }

        DrawSeparator();

        EditorGUILayout.Space(8f);

        // ── Footer ───────────────────────────────────────────────────────
        DrawSeparator();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            var footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
            GUILayout.Label("JustTimeShader", footerStyle);
            GUILayout.FlexibleSpace();
        }

        editor.RenderQueueField();

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(mat);
            if (EditorUtility.IsPersistent(mat))
                AssetDatabase.SaveAssetIfDirty(mat);
        }

        // ── Scene-view live preview ───────────────────────────────────────
        // Keep _EditorDate in sync with the current UTC time (outside BeginChangeCheck
        // so it never triggers a user-visible dirty/undo entry).
        // The shader uses this when VRC_GetUTCUnixTimeInSeconds() == 0 (Unity editor).
        if (mat != null && mat.HasProperty("_EditorDate"))
        {
            var    epoch     = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            float  nowSecs   = (float)(System.DateTime.UtcNow - epoch).TotalSeconds;
            // Update at most once per minute to avoid constant GPU shader re-upload
            if (Mathf.Abs(mat.GetFloat("_EditorDate") - nowSecs) > 60f)
                mat.SetFloat("_EditorDate", nowSecs);   // direct set — no SetDirty, no undo
        }

    }

    // ── Material Preview ─────────────────────────────────────────────────

    public override void OnMaterialPreviewGUI(MaterialEditor editor, Rect r, GUIStyle background)
    {
        if (Event.current.type != EventType.Repaint) return;

        // Dark backdrop
        EditorGUI.DrawRect(r, new Color(0.10f, 0.10f, 0.12f));

        var mat = editor.target as Material;
        if (mat == null) return;

        // Resolve current date string using actual date + material format
        var now = System.DateTime.Now;
        string dateText = now.ToString("yyyy/MM/dd");
        if (mat.HasProperty("_DateFormat"))
        {
            int fmt = Mathf.Clamp(Mathf.RoundToInt(mat.GetFloat("_DateFormat")), 0, 3);
            switch (fmt)
            {
                case 1: dateText = now.ToString("MM/dd/yyyy"); break;
                case 2: dateText = now.ToString("dd/MM/yyyy"); break;
                case 3: dateText = now.ToString("yyyy-MM-dd"); break;
                default: dateText = now.ToString("yyyy/MM/dd"); break;
            }
        }

        // Date color from material property
        Color dateColor = Color.white;
        if (mat.HasProperty("_Color")) dateColor = mat.GetColor("_Color");

        int fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.22f), 10, 56);

        var shadowStyle = new GUIStyle
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = fontSize,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = new Color(0f, 0f, 0f, 0.55f) },
        };
        var textStyle = new GUIStyle
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = fontSize,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = dateColor },
        };

        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), dateText, shadowStyle);
        GUI.Label(r, dateText, textStyle);
    }

    // ── Section Header ────────────────────────────────────────────────────

    /// <summary>Draws a styled section header with foldout. Returns true when expanded.</summary>
    private static bool DrawSectionHeader(ref bool foldout, string title, Color accent)
    {
        var r = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, HeaderBg);

        // Left accent bar
        EditorGUI.DrawRect(new Rect(r.x, r.y + 2f, 3f, r.height - 4f), accent);

        // Chevron
        string chevron = foldout ? "▾" : "▸";
        var chevStyle  = new GUIStyle(EditorStyles.boldLabel)
        {
            normal   = { textColor = accent },
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
        };
        GUI.Label(new Rect(r.x + 6f, r.y, 18f, r.height), chevron, chevStyle);

        // Title
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal   = { textColor = new Color(0.88f, 0.88f, 0.88f) },
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
        };
        GUI.Label(new Rect(r.x + 22f, r.y, r.width - 22f, r.height), title, titleStyle);

        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            foldout = !foldout;
            Event.current.Use();
            // Use a throwaway repaint object
            var w = EditorWindow.focusedWindow;
            if (w != null) w.Repaint();
        }

        return foldout;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void DrawSeparator()
    {
        var r = GUILayoutUtility.GetRect(0f, 4f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(r.x, r.y + 2f, r.width, 1f), SeparatorLine);
    }

    private static void DrawBorder(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.xMin,     r.yMin,     r.width, t),        c);
        EditorGUI.DrawRect(new Rect(r.xMin,     r.yMax - t, r.width, t),        c);
        EditorGUI.DrawRect(new Rect(r.xMin,     r.yMin,     t,       r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.yMin,     t,       r.height), c);
    }

    /// <summary>Draws only the XY components of a Vector4 property as a Vector2 field.</summary>
    private static void DrawVector2Field(MaterialProperty prop, GUIContent label)
    {
        EditorGUI.BeginChangeCheck();
        var v  = prop.vectorValue;
        var v2 = EditorGUILayout.Vector2Field(label, new Vector2(v.x, v.y));
        if (EditorGUI.EndChangeCheck())
            prop.vectorValue = new Vector4(v2.x, v2.y, v.z, v.w);
    }
}
