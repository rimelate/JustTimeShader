using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class VRCLocalDateOverlayLiltoonGUI : ShaderGUI
{
    private ShaderGUI _lilInspector;
    private bool _lilChecked;
    private bool _showDate = true;
    private bool _showFallback;

    private static GUIStyle _sFoldout;
    private static GUIStyle _sBoxOuterFallback;
    private static GUIStyle _sSubHeader;
    private static GUIStyle _sInnerBoxFallback;

    private static bool _reflectionReady;
    private static PropertyInfo _settingsInstanceProperty;
    private static FieldInfo _settingsLanguageField;
    private static Type _lilGuiType;
    private static Type _lilLanguageManagerType;
    private static PropertyInfo _langSetProperty;
    private static PropertyInfo _languageNameProperty;

    private static readonly GUIContent[] FormatLabels =
    {
        new GUIContent("YYYY/MM/DD", "Year / Month / Day"),
        new GUIContent("MM/DD/YYYY", "Month / Day / Year"),
        new GUIContent("DD/MM/YYYY", "Day / Month / Year"),
        new GUIContent("YYYY-MM-DD", "ISO 8601"),
    };

    private static readonly Color SeparatorLine = new Color(0.40f, 0.40f, 0.40f, 0.50f);

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
    {
        var mat = editor.target as Material;
        if (mat == null)
        {
            base.OnGUI(editor, props);
            return;
        }

        EnsureReflectionCache();

        EditorGUIUtility.labelWidth = 160f;
        EditorGUI.BeginChangeCheck();

        DrawDateSection(editor, props, mat);
        DrawSeparator();

        var desiredShader = mat.shader;
        EditorGUILayout.HelpBox(
            L(
                "Overlay rendering mode is fixed for this shader. Do not change it in the lilToon section below; it will be auto-restored.",
                "オーバーレイ用の描画モードはこのシェーダーで固定です。下の lilToon セクションで変更しても自動的に元に戻ります。"
            ),
            MessageType.Info
        );

        var lil = GetLilToonInspector();
        if (lil != null)
        {
            try
            {
                lil.OnGUI(editor, props);
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox(
                    "The lilToon inspector threw an error:\n" + e.Message + "\n\nFalling back to a simple property list.",
                    MessageType.Error
                );
                DrawFallbackInspector(editor, props);
            }
        }
        else
        {
            DrawFallbackInspector(editor, props);
        }

        if (desiredShader != null)
        {
            foreach (var target in editor.targets)
            {
                var material = target as Material;
                if (material == null || material.shader == desiredShader) continue;

                Undo.RecordObject(material, "Restore Date Overlay Shader");
                material.shader = desiredShader;
                EditorUtility.SetDirty(material);
            }
        }

        SyncEditorDate(mat);
        if (EditorGUI.EndChangeCheck())
        {
            MarkTargetsDirty(editor.targets);
        }
    }

    private void DrawDateSection(MaterialEditor editor, MaterialProperty[] props, Material mat)
    {
        _showDate = DrawLilFoldout(
            L("Date Overlay  (VRC Local Date)", "日付オーバーレイ（VRC ローカル日付）"),
            _showDate
        );
        if (!_showDate) return;

        using (new EditorGUILayout.VerticalScope(GetBoxOuter()))
        {
            DrawLilSubHeader(L("Display", "表示"));
            using (new EditorGUILayout.VerticalScope(GetInnerBox()))
            {
                var pEnable = FindProperty("_DateEnable", props, false);
                if (pEnable != null)
                {
                    bool enabled = pEnable.floatValue > 0.5f;
                    bool next = EditorGUILayout.Toggle(
                        new GUIContent(
                            L("Enable Overlay", "オーバーレイを表示"),
                            L("Show or hide the local date/time overlay.", "ローカル日時オーバーレイの表示と非表示を切り替えます。")
                        ),
                        enabled
                    );
                    if (next != enabled) pEnable.floatValue = next ? 1f : 0f;
                }

                EditorGUILayout.Space(2f);

                var pFlags = FindProperty("_DateShowFlags", props, false);
                int flags = pFlags != null ? Mathf.RoundToInt(pFlags.floatValue) : 7;
                bool showYear = (flags & 1) != 0;
                bool showMonth = (flags & 2) != 0;
                bool showDay = (flags & 4) != 0;
                bool showHour = (flags & 8) != 0;
                bool showMinute = (flags & 16) != 0;
                bool showSecond = (flags & 32) != 0;

                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    showYear = EditorGUILayout.ToggleLeft(L("Year", "年"), showYear, GUILayout.ExpandWidth(true));
                    showMonth = EditorGUILayout.ToggleLeft(L("Month", "月"), showMonth, GUILayout.ExpandWidth(true));
                    showDay = EditorGUILayout.ToggleLeft(L("Day", "日"), showDay, GUILayout.ExpandWidth(true));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    showHour = EditorGUILayout.ToggleLeft(L("Hour", "時"), showHour, GUILayout.ExpandWidth(true));
                    showMinute = EditorGUILayout.ToggleLeft(L("Minute", "分"), showMinute, GUILayout.ExpandWidth(true));
                    showSecond = EditorGUILayout.ToggleLeft(L("Second", "秒"), showSecond, GUILayout.ExpandWidth(true));
                }
                if (EditorGUI.EndChangeCheck() && pFlags != null)
                {
                    flags = (showYear ? 1 : 0) |
                            (showMonth ? 2 : 0) |
                            (showDay ? 4 : 0) |
                            (showHour ? 8 : 0) |
                            (showMinute ? 16 : 0) |
                            (showSecond ? 32 : 0);
                    pFlags.floatValue = flags;
                }

                bool hasDate = showYear || showMonth || showDay;
                bool hasTime = showHour || showMinute || showSecond;
                int formatIndex = 0;
                if (hasDate)
                {
                    var pFormat = FindProperty("_DateFormat", props, false);
                    if (pFormat != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        formatIndex = Mathf.Clamp(Mathf.RoundToInt(pFormat.floatValue), 0, 3);
                        int nextFormat = EditorGUILayout.Popup(
                            new GUIContent(
                                L("Date Format", "日付フォーマット"),
                                L("Choose how the date is displayed.", "日付の表示形式を選択します。")
                            ),
                            formatIndex,
                            FormatLabels
                        );
                        if (EditorGUI.EndChangeCheck())
                        {
                            pFormat.floatValue = nextFormat;
                            formatIndex = nextFormat;
                        }
                    }
                }

                int hourMode = 0;
                bool showAmPm = false;
                if (hasTime)
                {
                    var pHourMode = FindProperty("_DateHourMode", props, false);
                    if (pHourMode != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        hourMode = Mathf.Clamp(Mathf.RoundToInt(pHourMode.floatValue), 0, 1);
                        int nextHourMode = EditorGUILayout.Popup(
                            new GUIContent(
                                L("Hour Mode", "時刻表記"),
                                L("Choose between 24-hour and 12-hour time.", "24時間表記と12時間表記を切り替えます。")
                            ),
                            hourMode,
                            new[] { L("24 Hour", "24時間"), L("12 Hour", "12時間") }
                        );
                        if (EditorGUI.EndChangeCheck())
                        {
                            pHourMode.floatValue = nextHourMode;
                            hourMode = nextHourMode;
                        }
                    }

                    var pShowAmPm = FindProperty("_DateShowAmPm", props, false);
                    if (pShowAmPm != null)
                    {
                        bool current = pShowAmPm.floatValue > 0.5f;
                        bool next = EditorGUILayout.Toggle(
                            new GUIContent(
                                L("Show AM/PM", "AM/PM を表示"),
                                L("Append AM or PM after the time when using a font atlas.", "フォントアトラス使用時に時刻の後ろへ AM または PM を追加します。")
                            ),
                            current
                        );
                        if (next != current) pShowAmPm.floatValue = next ? 1f : 0f;
                        showAmPm = next;
                    }
                }

            }

            EditorGUILayout.Space(4f);
            DrawLilSubHeader(L("Font", "フォント"));
            using (new EditorGUILayout.VerticalScope(GetInnerBox()))
            {
                var pUseFontTex = FindProperty("_DateUseFontTex", props, false);
                if (pUseFontTex != null)
                {
                    bool useFontAtlas = pUseFontTex.floatValue > 0.5f;
                    bool nextUseFontAtlas = EditorGUILayout.Toggle(
                        new GUIContent(
                            L("Use Font Atlas", "フォントアトラスを使用"),
                            L("Use a custom font atlas instead of procedural digit shapes.", "手続き型グリフの代わりにカスタムのフォントアトラスを使用します。")
                        ),
                        useFontAtlas
                    );
                    if (nextUseFontAtlas != useFontAtlas) pUseFontTex.floatValue = nextUseFontAtlas ? 1f : 0f;

                    if (nextUseFontAtlas)
                    {
                        DrawOptionalProp(
                            editor,
                            props,
                            "_DateFontTex",
                            L("Font Atlas", "フォントアトラス")
                        );
                    }

                    if (GUILayout.Button(L("Bake Windows Font Atlas...", "Windows フォントをアトラスにベイク..."), GUILayout.Height(22f)))
                    {
                        VRCLocalDateOverlayFontBakerWindow.Open(mat);
                    }
                }
            }

            EditorGUILayout.Space(4f);
            DrawLilSubHeader(L("Position", "位置"));
            using (new EditorGUILayout.VerticalScope(GetInnerBox()))
            {
                var pSt = FindProperty("_DateTex_ST", props, false);
                if (pSt != null)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Vector4 st = pSt.vectorValue;
                        Vector2 tiling = new Vector2(st.x, st.y);
                        Vector2 offset = new Vector2(st.z, st.w);

                        EditorGUI.BeginChangeCheck();
                        tiling = DrawTilingOffsetRow(L("Tiling", "タイリング"), tiling);
                        offset = DrawTilingOffsetRow(L("Offset", "オフセット"), offset);
                        if (EditorGUI.EndChangeCheck())
                        {
                            pSt.vectorValue = new Vector4(tiling.x, tiling.y, offset.x, offset.y);
                        }
                    }
                }

                EditorGUILayout.Space(2f);
                if (GUILayout.Button(L("Open Position Adjuster...", "位置調整ウィンドウを開く..."), GUILayout.Height(24f)))
                {
                    VRCLocalDateOverlayWindow.OpenWithMaterial(mat);
                }
            }
        }
    }

    private void DrawFallbackInspector(MaterialEditor editor, MaterialProperty[] props)
    {
        EditorGUILayout.HelpBox(
            L(
                "The lilToon inspector could not be loaded. A simple fallback inspector is shown instead.",
                "lilToon インスペクターを読み込めなかったため、代わりにシンプルなフォールバックインスペクターを表示しています。"
            ),
            MessageType.Warning
        );

        _showFallback = DrawLilFoldout(L("All Properties", "全プロパティ"), _showFallback);
        if (!_showFallback) return;

        using (new EditorGUILayout.VerticalScope(GetBoxOuter()))
        using (new EditorGUI.IndentLevelScope())
        {
            base.OnGUI(editor, props);
        }
    }

    private static bool DrawLilFoldout(string title, bool display)
    {
        var style = GetFoldoutStyle();
        var rect = GUILayoutUtility.GetRect(16f, 20f, style);
        rect.width += 8f;
        rect.x -= 8f;

        GUI.Box(rect, title, style);
        if (Event.current.type == EventType.Repaint)
        {
            EditorStyles.foldout.Draw(new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f), false, false, display, false);
        }

        rect.width -= 24f;
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            display = !display;
            Event.current.Use();
            EditorWindow.focusedWindow?.Repaint();
        }

        return display;
    }

    private static void DrawLilSubHeader(string title)
    {
        var rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
        GUI.Label(rect, title, GetSubHeaderStyle());
    }

    private static void DrawSeparator()
    {
        var rect = GUILayoutUtility.GetRect(0f, 8f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 4f, rect.width, 1f), SeparatorLine);
    }

    private static GUIStyle GetFoldoutStyle()
    {
        if (_sFoldout == null)
        {
            _sFoldout = new GUIStyle("ShurikenModuleTitle")
            {
                font = EditorStyles.label.font,
                fontSize = EditorStyles.label.fontSize,
                fontStyle = EditorStyles.label.fontStyle,
                border = new RectOffset(15, 7, 4, 4),
                contentOffset = new Vector2(20f, -2f),
                fixedHeight = 22,
            };
        }

        return _sFoldout;
    }

    private static GUIStyle GetBoxOuter()
    {
        var lilStyle = TryGetLilGuiStyle("boxOuter");
        if (lilStyle != null) return lilStyle;

        if (_sBoxOuterFallback == null)
        {
            _sBoxOuterFallback = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(6, 6, 4, 4),
            };
        }

        return _sBoxOuterFallback;
    }

    private static GUIStyle GetInnerBox()
    {
        var lilStyle = TryGetLilGuiStyle("boxInnerHalf");
        if (lilStyle != null) return lilStyle;

        lilStyle = TryGetLilGuiStyle("boxInner");
        if (lilStyle != null) return lilStyle;

        if (_sInnerBoxFallback == null)
        {
            _sInnerBoxFallback = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(4, 4, 2, 2),
            };
        }

        return _sInnerBoxFallback;
    }

    private static GUIStyle GetSubHeaderStyle()
    {
        var lilStyle = TryGetLilGuiStyle("boldLabel");
        if (lilStyle != null) return lilStyle;

        if (_sSubHeader == null)
        {
            _sSubHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = EditorStyles.miniLabel.fontSize,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.78f, 0.74f, 0.66f)
                        : Color.black
                },
                alignment = TextAnchor.MiddleLeft,
            };
        }

        return _sSubHeader;
    }

    private static GUIStyle TryGetLilGuiStyle(string fieldName)
    {
        try
        {
            if (_lilGuiType == null) return null;
            var field = _lilGuiType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null) as GUIStyle;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureReflectionCache()
    {
        if (_reflectionReady) return;
        _reflectionReady = true;

        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_settingsInstanceProperty == null || _settingsLanguageField == null)
                {
                    var settingsType = assembly.GetType("lilToon.Settings");
                    if (settingsType != null)
                    {
                        _settingsInstanceProperty = settingsType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        _settingsLanguageField = settingsType.GetField("language", BindingFlags.Public | BindingFlags.Instance);
                    }
                }

                if (_lilGuiType == null)
                {
                    _lilGuiType = assembly.GetType("lilToon.lilEditorGUI");
                }

                if (_lilLanguageManagerType == null || _langSetProperty == null || _languageNameProperty == null)
                {
                    _lilLanguageManagerType = assembly.GetType("lilToon.lilLanguageManager");
                    if (_lilLanguageManagerType != null)
                    {
                        _langSetProperty = _lilLanguageManagerType.GetProperty("langSet", BindingFlags.Public | BindingFlags.Static);
                        var languageSettingsType = assembly.GetType("lilToon.lilLanguageManager+LanguageSettings");
                        if (languageSettingsType != null)
                        {
                            _languageNameProperty = languageSettingsType.GetProperty("languageName", BindingFlags.Public | BindingFlags.Instance);
                        }
                    }
                }

                if ((_settingsInstanceProperty != null && _settingsLanguageField != null || _langSetProperty != null && _languageNameProperty != null) && _lilGuiType != null)
                {
                    break;
                }
            }
        }
        catch
        {
        }
    }

    private static string L(string en, string ja)
    {
        try
        {
            string language = GetLilLanguageCode();
            if (!string.IsNullOrEmpty(language))
            {
                return language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? ja : en;
            }
        }
        catch
        {
        }

        return System.Globalization.CultureInfo.CurrentCulture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? ja : en;
    }

    private static string GetLilLanguageCode()
    {
        if (_langSetProperty != null && _languageNameProperty != null)
        {
            var langSet = _langSetProperty.GetValue(null);
            if (langSet != null)
            {
                var languageName = _languageNameProperty.GetValue(langSet) as string;
                if (!string.IsNullOrEmpty(languageName))
                    return languageName;
            }
        }

        if (_settingsInstanceProperty != null && _settingsLanguageField != null)
        {
            var instance = _settingsInstanceProperty.GetValue(null);
            if (instance != null)
            {
                var language = _settingsLanguageField.GetValue(instance) as string;
                if (!string.IsNullOrEmpty(language))
                    return language;
            }
        }

        return null;
    }

    private ShaderGUI GetLilToonInspector()
    {
        if (_lilChecked) return _lilInspector;
        _lilChecked = true;

        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("lilToon.lilToonInspector");
                if (type == null) continue;

                _lilInspector = (ShaderGUI)Activator.CreateInstance(type);
                break;
            }
        }
        catch
        {
        }

        return _lilInspector;
    }

    private static void SyncEditorDate(Material mat)
    {
        if (!mat.HasProperty("_EditorDate")) return;

        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        float nowSeconds = (float)(DateTime.UtcNow - epoch).TotalSeconds;
        if (Mathf.Abs(mat.GetFloat("_EditorDate") - nowSeconds) > 1f)
        {
            mat.SetFloat("_EditorDate", nowSeconds);
        }
    }

    private static void MarkTargetsDirty(UnityEngine.Object[] targets)
    {
        if (targets == null) return;
        foreach (var target in targets)
        {
            if (target != null)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }

    private static void DrawOptionalProp(MaterialEditor editor, MaterialProperty[] props, string name, string label, string tooltip = "")
    {
        var property = FindProperty(name, props, false);
        if (property != null)
        {
            editor.ShaderProperty(property, new GUIContent(label, tooltip));
        }
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }

    private static string BuildSampleString(int flags, int formatIndex, int hourMode, bool showAmPm)
    {
        bool showYear = (flags & 1) != 0;
        bool showMonth = (flags & 2) != 0;
        bool showDay = (flags & 4) != 0;
        bool showHour = (flags & 8) != 0;
        bool showMinute = (flags & 16) != 0;
        bool showSecond = (flags & 32) != 0;

        string dateSeparator = formatIndex == 3 ? "-" : "/";
        var dateParts = new List<string>();
        if (formatIndex == 1)
        {
            if (showMonth) dateParts.Add("MM");
            if (showDay) dateParts.Add("DD");
            if (showYear) dateParts.Add("YYYY");
        }
        else if (formatIndex == 2)
        {
            if (showDay) dateParts.Add("DD");
            if (showMonth) dateParts.Add("MM");
            if (showYear) dateParts.Add("YYYY");
        }
        else
        {
            if (showYear) dateParts.Add("YYYY");
            if (showMonth) dateParts.Add("MM");
            if (showDay) dateParts.Add("DD");
        }

        var timeParts = new List<string>();
        if (showHour) timeParts.Add(hourMode == 1 ? "hh" : "HH");
        if (showMinute) timeParts.Add("MM");
        if (showSecond) timeParts.Add("SS");

        string dateString = string.Join(dateSeparator, dateParts);
        string timeString = string.Join(":", timeParts);

        if (!string.IsNullOrEmpty(timeString) && showAmPm && showHour) timeString += " AM";
        if (!string.IsNullOrEmpty(dateString) && !string.IsNullOrEmpty(timeString)) return dateString + " " + timeString;
        return !string.IsNullOrEmpty(dateString) ? dateString : timeString;
    }

    private static Vector2 DrawTilingOffsetRow(string label, Vector2 value)
    {
        Rect row = EditorGUILayout.GetControlRect(true, 18f);
        Rect labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height);
        Rect fieldRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y, row.width - EditorGUIUtility.labelWidth, row.height);

        GUI.Label(labelRect, label);

        float halfWidth = fieldRect.width * 0.5f - 1f;

        GUI.Label(new Rect(fieldRect.x, fieldRect.y, 12f, fieldRect.height), "X", EditorStyles.miniLabel);
        value.x = EditorGUI.FloatField(new Rect(fieldRect.x + 13f, fieldRect.y, halfWidth - 13f, fieldRect.height), value.x);

        GUI.Label(new Rect(fieldRect.x + halfWidth + 2f, fieldRect.y, 12f, fieldRect.height), "Y", EditorStyles.miniLabel);
        value.y = EditorGUI.FloatField(new Rect(fieldRect.x + halfWidth + 15f, fieldRect.y, halfWidth - 13f, fieldRect.height), value.y);

        return value;
    }
}
