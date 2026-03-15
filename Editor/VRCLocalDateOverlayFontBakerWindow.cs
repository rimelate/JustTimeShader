using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class VRCLocalDateOverlayFontBakerWindow : EditorWindow
{
    private static readonly string[] AtlasEntries =
    {
        "0", "1", "2", "3",
        "4", "5", "6", "7",
        "8", "9", "/", "-",
        ":", "AM", "PM", ""
    };

    private const int AtlasColumns = 4;
    private const int AtlasRows = 4;

    private Material _targetMaterial;
    private string[] _installedFonts = Array.Empty<string>();
    private int _fontIndex;
    private int _fontSize = 96;
    private int _cellWidth = 96;
    private int _cellHeight = 128;
    private int _padding = 8;
    private Vector2 _scroll;

    public static void Open(Material targetMaterial)
    {
        var window = GetWindow<VRCLocalDateOverlayFontBakerWindow>("Date Font Baker");
        window.minSize = new Vector2(420f, 260f);
        window._targetMaterial = targetMaterial;
        window.RefreshFontList();
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        RefreshFontList();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Windows Font Baker", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(_targetMaterial == null))
        {
            EditorGUILayout.ObjectField("Material", _targetMaterial, typeof(Material), false);
        }

        if (_installedFonts.Length == 0)
        {
            EditorGUILayout.HelpBox("No OS fonts were found.", MessageType.Warning);
            if (GUILayout.Button("Refresh Fonts"))
                RefreshFontList();
            EditorGUILayout.EndScrollView();
            return;
        }

        _fontIndex = EditorGUILayout.Popup("Windows Font", Mathf.Clamp(_fontIndex, 0, _installedFonts.Length - 1), _installedFonts);
        _fontSize = EditorGUILayout.IntSlider("Font Size", _fontSize, 32, 256);
        _cellWidth = EditorGUILayout.IntSlider("Cell Width", _cellWidth, 32, 256);
        _cellHeight = EditorGUILayout.IntSlider("Cell Height", _cellHeight, 32, 256);
        _padding = EditorGUILayout.IntSlider("Padding", _padding, 0, 32);

        using (new EditorGUI.DisabledScope(_targetMaterial == null))
        {
            if (GUILayout.Button("Bake And Assign", GUILayout.Height(28f)))
                BakeAndAssign();
        }

        if (GUILayout.Button("Refresh Fonts"))
            RefreshFontList();

        EditorGUILayout.EndScrollView();
    }

    private void RefreshFontList()
    {
        _installedFonts = Font.GetOSInstalledFontNames()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_fontIndex >= _installedFonts.Length)
            _fontIndex = 0;
    }

    private void BakeAndAssign()
    {
        if (_targetMaterial == null)
        {
            EditorUtility.DisplayDialog("Date Font Baker", "Select a target material first.", "OK");
            return;
        }

        var fontName = _installedFonts[Mathf.Clamp(_fontIndex, 0, _installedFonts.Length - 1)];
        var font = Font.CreateDynamicFontFromOSFont(fontName, _fontSize);
        if (font == null)
        {
            EditorUtility.DisplayDialog("Date Font Baker", "Failed to create the selected OS font.", "OK");
            return;
        }

        font.RequestCharactersInTexture(string.Concat(AtlasEntries), _fontSize, FontStyle.Normal);

        var sourceTexture = font.material != null ? font.material.mainTexture : null;
        if (sourceTexture == null)
        {
            EditorUtility.DisplayDialog("Date Font Baker", "The OS font atlas texture could not be generated.", "OK");
            return;
        }

        var readableSource = CopyTextureReadable(sourceTexture);
        try
        {
            var atlas = BuildAtlas(readableSource, font);
            SaveAndAssignAtlas(atlas, fontName);
        }
        finally
        {
            if (readableSource != null)
                DestroyImmediate(readableSource);
        }
    }

    private Texture2D BuildAtlas(Texture2D source, Font font)
    {
        var atlas = new Texture2D(_cellWidth * AtlasColumns, _cellHeight * AtlasRows, TextureFormat.RGBA32, false, true)
        {
            name = "DateFontAtlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        var clear = new Color32[atlas.width * atlas.height];
        atlas.SetPixels32(clear);

        for (int i = 0; i < AtlasEntries.Length; i++)
        {
            int col = i % AtlasColumns;
            int row = i / AtlasColumns;
            int dstX = col * _cellWidth;
            int dstY = (AtlasRows - 1 - row) * _cellHeight;
            DrawGlyphString(atlas, source, font, AtlasEntries[i], dstX, dstY);
        }

        atlas.Apply(false, false);
        return atlas;
    }

    private void DrawGlyphString(Texture2D destination, Texture2D source, Font font, string glyphText, int dstX, int dstY)
    {
        if (string.IsNullOrEmpty(glyphText))
            return;

        var infos = new CharacterInfo[glyphText.Length];
        int totalWidth = 0;
        int maxHeight = 1;

        for (int i = 0; i < glyphText.Length; i++)
        {
            if (!font.GetCharacterInfo(glyphText[i], out infos[i], _fontSize, FontStyle.Normal))
                return;

            totalWidth += Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(infos[i].advance)));
            maxHeight = Mathf.Max(maxHeight, Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(infos[i].glyphHeight))));
        }

        int drawWidth = Mathf.Max(1, _cellWidth - _padding * 2);
        int drawHeight = Mathf.Max(1, _cellHeight - _padding * 2);
        float scale = Mathf.Min((float)drawWidth / totalWidth, (float)drawHeight / maxHeight);
        if (glyphText == ":")
            scale *= 0.65f;

        int scaledTotalWidth = Mathf.Max(1, Mathf.RoundToInt(totalWidth * scale));
        int startX = dstX + (_cellWidth - scaledTotalWidth) / 2;
        int penX = startX;

        for (int i = 0; i < infos.Length; i++)
        {
            DrawCharacter(destination, source, infos[i], penX, dstY, scale);
            penX += Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(infos[i].advance) * scale));
        }
    }

    private void DrawCharacter(Texture2D destination, Texture2D source, CharacterInfo info, int dstX, int dstY, float scale)
    {
        float uMin = Mathf.Min(Mathf.Min(info.uvBottomLeft.x, info.uvBottomRight.x), Mathf.Min(info.uvTopLeft.x, info.uvTopRight.x));
        float uMax = Mathf.Max(Mathf.Max(info.uvBottomLeft.x, info.uvBottomRight.x), Mathf.Max(info.uvTopLeft.x, info.uvTopRight.x));
        float vMin = Mathf.Min(Mathf.Min(info.uvBottomLeft.y, info.uvBottomRight.y), Mathf.Min(info.uvTopLeft.y, info.uvTopRight.y));
        float vMax = Mathf.Max(Mathf.Max(info.uvBottomLeft.y, info.uvBottomRight.y), Mathf.Max(info.uvTopLeft.y, info.uvTopRight.y));

        int glyphWidth = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(info.glyphWidth)));
        int glyphHeight = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(info.glyphHeight)));
        int scaledWidth = Mathf.Max(1, Mathf.RoundToInt(glyphWidth * scale));
        int scaledHeight = Mathf.Max(1, Mathf.RoundToInt(glyphHeight * scale));
        int startY = dstY + (_cellHeight - scaledHeight) / 2;

        for (int y = 0; y < scaledHeight; y++)
        {
            float ty = scaledHeight > 1 ? (float)y / (scaledHeight - 1) : 0f;
            float v = Mathf.Lerp(vMax, vMin, ty);
            for (int x = 0; x < scaledWidth; x++)
            {
                float tx = scaledWidth > 1 ? (float)x / (scaledWidth - 1) : 0f;
                float u = Mathf.Lerp(uMin, uMax, tx);
                Color sample = source.GetPixelBilinear(u, v);
                float alpha = Mathf.Max(sample.a, sample.grayscale);
                destination.SetPixel(dstX + x, startY + y, new Color(1f, 1f, 1f, alpha));
            }
        }
    }

    private static Texture2D CopyTextureReadable(Texture source)
    {
        var width = Mathf.Max(1, source.width);
        var height = Mathf.Max(1, source.height);
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;

        Graphics.Blit(source, rt);
        RenderTexture.active = rt;

        var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        readable.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return readable;
    }

    private void SaveAndAssignAtlas(Texture2D atlas, string fontName)
    {
        const string folder = "Assets/rimerime/Justtimeshader/Generated";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/rimerime/Justtimeshader", "Generated");

        string safeName = SanitizeFileName(fontName);
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/DateFont_{safeName}.asset");
        AssetDatabase.CreateAsset(atlas, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Undo.RecordObject(_targetMaterial, "Assign Date Font Atlas");
        _targetMaterial.SetFloat("_DateUseFontTex", 1f);
        _targetMaterial.SetTexture("_DateFontTex", atlas);
        _targetMaterial.SetFloat("_DateFontCols", AtlasColumns);
        _targetMaterial.SetFloat("_DateFontRows", AtlasRows);
        EditorUtility.SetDirty(_targetMaterial);

        Selection.activeObject = atlas;
        EditorGUIUtility.PingObject(atlas);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace(' ', '_');
    }
}
