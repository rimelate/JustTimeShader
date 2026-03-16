Shader "JustTimeShader"
{
    Properties
    {
        _Color   ("Date Color",       Color)      = (1,1,1,1)
        _BgColor ("Background Color", Color)      = (0,0,0,0)
        _Cutoff  ("Alpha Cutoff",     Range(0,1)) = 0.01

        [Toggle]   _DateEnable    ("Enable Date Overlay", Float) = 1

        // 位置は位置調整ウィンドウ (Tools > JustTimeShaderEditor > Local Date Overlay Adjuster) から編集
        [HideInInspector] _DateUVRect ("UV Rect (X,Y,W,H)", Vector) = (0.0, 0.0, 0.4, 0.1)

        [IntRange] _DateFormat ("Format  0:YYYY/MM/DD  1:MM/DD/YYYY  2:DD/MM/YYYY  3:YYYY-MM-DD", Range(0,3)) = 0

        // ── Font Texture (optional) ───────────────────────────────────────────────
        // テクスチャを使う場合: 横 12 列の等幅スプライト [0][1][2][3][4][5][6][7][8][9][/][-]
        // テクスチャ未設定 (white) の場合は手続き SDF 描画にフォールバック
        [Toggle] _DateUseFontTex ("Use Font Texture", Float) = 0
        _DateFontTex ("Font Texture", 2D) = "white" {}
        // フォントテクスチャの列数 (デフォルト 12 = 0-9 + / + -)
        [IntRange] _DateFontCols ("Font Columns", Range(1, 32)) = 12

        // Automatically kept in sync by VRCLocalDateOverlayShaderGUI for Scene-view preview.
        // The shader uses this when VRC_GetUTCUnixTimeInSeconds() == 0 (Unity editor).
        [HideInInspector] _EditorDate ("", Float) = 1773446400
    }

    CustomEditor "VRCLocalDateOverlayShaderGUI"

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Packages/com.vrchat.base/ShaderLibrary/VRCTime.cginc"

            float4 _Color;
            float4 _BgColor;
            float  _Cutoff;
            float  _DateEnable;
            float4 _DateUVRect;
            int    _DateFormat;
            float  _DateUseFontTex;
            sampler2D _DateFontTex;
            float  _DateFontCols;
            float  _EditorDate;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // ── Procedural SDF digit rendering ──────────────────────────────────────
            // Digits are drawn using a 7-segment SDF.  No external texture needed.

            // Rounded-rectangle SDF  (negative = inside)
            float RRectSDF(float2 p, float2 center, float2 halfSize, float r)
            {
                float2 d = abs(p - center) - max(halfSize - r, 0.0);
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
            }

            float HSegSDF(float2 uv, float cy, float x0, float x1, float thick, float r)
            {
                return RRectSDF(uv, float2((x0+x1)*0.5, cy), float2((x1-x0)*0.5, thick), r);
            }

            float VSegSDF(float2 uv, float cx, float y0, float y1, float thick, float r)
            {
                return RRectSDF(uv, float2(cx, (y0+y1)*0.5), float2(thick, (y1-y0)*0.5), r);
            }

            // 7-segment digit — returns alpha [0..1]
            // Cell UV: x ∈ [0,1] left→right,  y ∈ [0,1] bottom→top
            // Segments: A=bit0(top), B=bit1(upper-R), C=bit2(lower-R),
            //           D=bit3(bot), E=bit4(lower-L), F=bit5(upper-L), G=bit6(mid)
            float DrawDigit(float2 uv, int digit)
            {
                static const uint kSegs[10] = {
                     63u,  // 0  ABCDEF
                      6u,  // 1  BC
                     91u,  // 2  ABDEG
                     79u,  // 3  ABCDG
                    102u,  // 4  BCFG
                    109u,  // 5  ACDFG
                    125u,  // 6  ACDEFG
                      7u,  // 7  ABC
                    127u,  // 8  ABCDEFG
                    111u   // 9  ABCDFG
                };
                uint mask = kSegs[clamp(digit, 0, 9)];

                const float T   = 0.09;    // segment half-thickness (in cell-UV space)
                const float R   = 0.05;    // corner radius
                const float X0  = 0.15;    // left vertical center x
                const float X1  = 0.85;    // right vertical center x
                const float YT  = 0.91;    // top bar y
                const float YM  = 0.50;    // middle bar y
                const float YB  = 0.09;    // bottom bar y
                const float GAP = 0.03;    // gap between H-seg ends and V-seg starts
                const float AA  = 0.025;   // antialiasing half-width

                float d = 1e6;
                if (mask &  1u) d = min(d, HSegSDF(uv, YT, X0, X1, T, R));                  // A top
                if (mask &  2u) d = min(d, VSegSDF(uv, X1, YM+T+GAP, YT-T-GAP, T, R));      // B upper-R
                if (mask &  4u) d = min(d, VSegSDF(uv, X1, YB+T+GAP, YM-T-GAP, T, R));      // C lower-R
                if (mask &  8u) d = min(d, HSegSDF(uv, YB, X0, X1, T, R));                  // D bottom
                if (mask & 16u) d = min(d, VSegSDF(uv, X0, YB+T+GAP, YM-T-GAP, T, R));      // E lower-L
                if (mask & 32u) d = min(d, VSegSDF(uv, X0, YM+T+GAP, YT-T-GAP, T, R));      // F upper-L
                if (mask & 64u) d = min(d, HSegSDF(uv, YM, X0, X1, T, R));                  // G middle
                return 1.0 - smoothstep(-AA, AA, d);
            }

            // Forward slash "/"  from bottom-left (0.25,0) to top-right (0.75,1)
            float DrawSlash(float2 uv)
            {
                // Line: 2x - y = 0.5  →  distance = |2x-y-0.5| / sqrt(5)
                float dist = abs(2.0*uv.x - uv.y - 0.5) * 0.4472135955; // 1/sqrt(5)
                const float W  = 0.09;
                const float AA = 0.025;
                float clip = step(0.05, uv.x) * step(uv.x, 0.95)
                           * step(0.02, uv.y) * step(uv.y, 0.98);
                return (1.0 - smoothstep(W-AA, W+AA, dist)) * clip;
            }

            // Hyphen "-"  centered horizontal bar
            float DrawHyphen(float2 uv)
            {
                float dx = abs(uv.x - 0.5);
                float dy = abs(uv.y - 0.5);
                const float HW = 0.33;
                const float HH = 0.09;
                const float AA = 0.025;
                return (1.0 - smoothstep(HW-AA, HW+AA, dx))
                     * (1.0 - smoothstep(HH-AA, HH+AA, dy));
            }

            // ── Font texture sampling ─────────────────────────────────────────────
            // テクスチャは横 cols 列の等幅スプライト: 列 0-9 = 数字, 10 = "/", 11 = "-"
            float4 SampleFontTex(float2 uv, int content, float cols)
            {
                float col;
                if      (content >= 0)  col = (float)clamp(content, 0, 9);
                else if (content == -1) col = 10.0;
                else                    col = 11.0;
                float u = (col + uv.x) / cols;
                return tex2D(_DateFontTex, float2(u, uv.y));
            }

            // ── Date math ────────────────────────────────────────────────────────────
            // All arithmetic uses uint to avoid "integer divides may be much slower" warnings.
            // Assumes z >= 0 (post-epoch dates; always true for VRC timestamps).
            void CivilFromDays(uint z, out int y, out int m, out int d)
            {
                z    += 719468u;
                uint era = z / 146097u;
                uint doe = z - era * 146097u;
                uint yoe = (doe - doe/1460u + doe/36524u - doe/146096u) / 365u;
                uint doy = doe - (365u*yoe + yoe/4u - yoe/100u);
                uint mp  = (5u*doy + 2u) / 153u;
                d = (int)(doy - (153u*mp + 2u)/5u + 1u);
                m = (int)mp + ((int)mp < 10 ? 3 : -9);
                y = (int)(yoe + era*400u) + (m <= 2 ? 1 : 0);
            }

            // ── Cell content lookup ──────────────────────────────────────────────────
            // Returns 0–9 (digit), −1 (slash "/"), −2 (hyphen "−")
            // Uses if/else-if + single return to silence "potentially uninitialized" warning.
            int GetCellContent(int fmt, int cell,
                               int y0, int y1, int y2, int y3,
                               int m0, int m1, int d0, int d1)
            {
                int c = d1; // initialise — satisfies HLSL D3D11 compiler
                if (fmt == 1) // MM/DD/YYYY
                {
                    if      (cell == 0) c = m0;
                    else if (cell == 1) c = m1;
                    else if (cell == 2) c = -1;
                    else if (cell == 3) c = d0;
                    else if (cell == 4) c = d1;
                    else if (cell == 5) c = -1;
                    else if (cell == 6) c = y0;
                    else if (cell == 7) c = y1;
                    else if (cell == 8) c = y2;
                    else                c = y3;
                }
                else if (fmt == 2) // DD/MM/YYYY
                {
                    if      (cell == 0) c = d0;
                    else if (cell == 1) c = d1;
                    else if (cell == 2) c = -1;
                    else if (cell == 3) c = m0;
                    else if (cell == 4) c = m1;
                    else if (cell == 5) c = -1;
                    else if (cell == 6) c = y0;
                    else if (cell == 7) c = y1;
                    else if (cell == 8) c = y2;
                    else                c = y3;
                }
                else if (fmt == 3) // YYYY-MM-DD
                {
                    if      (cell == 0) c = y0;
                    else if (cell == 1) c = y1;
                    else if (cell == 2) c = y2;
                    else if (cell == 3) c = y3;
                    else if (cell == 4) c = -2;
                    else if (cell == 5) c = m0;
                    else if (cell == 6) c = m1;
                    else if (cell == 7) c = -2;
                    else if (cell == 8) c = d0;
                    else                c = d1;
                }
                else // YYYY/MM/DD (default, fmt == 0)
                {
                    if      (cell == 0) c = y0;
                    else if (cell == 1) c = y1;
                    else if (cell == 2) c = y2;
                    else if (cell == 3) c = y3;
                    else if (cell == 4) c = -1;
                    else if (cell == 5) c = m0;
                    else if (cell == 6) c = m1;
                    else if (cell == 7) c = -1;
                    else if (cell == 8) c = d0;
                    else                c = d1;
                }
                return c;
            }

            // ── UV rect ──────────────────────────────────────────────────────────────

            float4 ResolveDateRect()
            {
                return float4(_DateUVRect.xy, max(_DateUVRect.zw, float2(0.0001, 0.0001)));
            }

            // ── Main overlay renderer ────────────────────────────────────────────────

            float4 RenderDateOverlay(float2 meshUV)
            {
                float4 rect    = ResolveDateRect();
                float2 localUV = (meshUV - rect.xy) / rect.zw;
                if (any(localUV < 0.0) || any(localUV > 1.0))
                    return float4(0, 0, 0, 0);

                // ── Time source ──────────────────────────────────────────────────────
                uint utcSeconds = VRC_GetUTCUnixTimeInSeconds();
                // In the Unity editor VRCTime returns 0.  Fall back to _EditorDate
                // (updated every minute by VRCLocalDateOverlayShaderGUI).
                if (utcSeconds == 0u)
                    utcSeconds = (uint)max(1.0, _EditorDate);

                int  tzOff     = VRC_GetTimezoneOffsetSeconds();
                int  localSecs = (int)utcSeconds + tzOff;
                // Clamp to 0 so the uint cast never wraps; dates before epoch → 1970-01-01.
                uint udays     = (uint)max(0, localSecs) / 86400u;

                int year, month, day;
                CivilFromDays(udays, year, month, day);

                // ── Digit extraction (uint arithmetic — no signed-divide warnings) ───
                uint uyear  = (uint)year;
                uint umonth = (uint)month;
                uint uday   = (uint)day;
                int y0 = (int)((uyear  / 1000u) % 10u);
                int y1 = (int)((uyear  /  100u) % 10u);
                int y2 = (int)((uyear  /   10u) % 10u);
                int y3 = (int)( uyear            % 10u);
                int m0 = (int)((umonth /   10u) % 10u);
                int m1 = (int)( umonth           % 10u);
                int d0 = (int)((uday   /   10u) % 10u);
                int d1 = (int)( uday             % 10u);

                // ── Cell selection ────────────────────────────────────────────────────
                int    cell   = clamp((int)floor(localUV.x * 10.0), 0, 9);
                float2 cellUV = float2(frac(localUV.x * 10.0), localUV.y);

                int content = GetCellContent(_DateFormat, cell,
                                             y0, y1, y2, y3, m0, m1, d0, d1);

                // ── Rasterize ─────────────────────────────────────────────────────────
                float4 color;
                float cols = max(_DateFontCols, 1.0);
                if (_DateUseFontTex > 0.5)
                {
                    // フォントテクスチャモード
                    float4 texCol = SampleFontTex(cellUV, content, cols);
                    color = float4(texCol.rgb * _Color.rgb, texCol.a * _Color.a);
                    color = float4(lerp(_BgColor.rgb, color.rgb, texCol.a),
                                   max(texCol.a * _Color.a, _BgColor.a));
                }
                else
                {
                    // SDF 手続き描画モード (デフォルト)
                    float alpha;
                    if      (content >= 0)  alpha = DrawDigit (cellUV, content);
                    else if (content == -1) alpha = DrawSlash (cellUV);
                    else                    alpha = DrawHyphen(cellUV);

                    float4 fg = float4(_Color.rgb, _Color.a * alpha);
                    color = float4(lerp(_BgColor.rgb, fg.rgb, alpha),
                                   max(fg.a, _BgColor.a));
                }
                return color;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_DateEnable < 0.5) return float4(0, 0, 0, 0);
                float4 col = RenderDateOverlay(i.uv);
                clip(col.a - _Cutoff);
                return col;
            }
            ENDHLSL
        }
    }
}
