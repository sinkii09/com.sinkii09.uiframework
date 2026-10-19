// Diagonal tile wipe for a full-screen UI curtain (scene/state transitions, heavy loads).
// _Cover 0 -> 1: tiles grow while a band sweeps corner to corner until the screen is covered.
// _Reveal 0 -> 1 (with _Cover = 1): tiles shrink, the band sweeping on, until the screen is clear.
// Exact ends: Cover 1 / Reveal 0 = alpha 1 everywhere; Cover 0 or Reveal 1 = alpha 0.
//
// Tile shapes come from Coffee.UIEffect's pattern textures (copied to Textures/, MIT): the alpha is
// the order a pixel fills in (low first), exactly as UIEffect's Pattern filter reads them.
//
// The curtain itself is the Image: its sprite (e.g. loading art) times its colour, drawn under the wipe.
//
// Constraints: the Image must be Type Simple, stretched over the whole screen, with no sprite or a sprite
// that covers its WHOLE texture (not atlased, not cropped) — the mesh UV must run 0..1 across the screen,
// since it and the screen aspect define the square tile grid. Do not put the surface under a Mask
// (stencil material copies would not see the animated floats).
Shader "UIFramework/UI/Pattern Wipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Curtain Colour", Color) = (0.04, 0.05, 0.09, 1)

        [Header(Pattern)]
        [NoScaleOffset] _PatternTex ("Pattern (alpha = fill order)", 2D) = "white" {}
        [ToggleUI] _PatternReverse ("Grow From Tile Centre", Float) = 0
        [IntRange] _Rows ("Tile Rows", Range(2, 40)) = 12
        _GridRotation ("Grid Rotation (deg, 45 = UIEffect diagonal lattice)", Range(0, 90)) = 45
        _Angle ("Sweep Angle (deg, 315 = top left to bottom right)", Range(0, 360)) = 315
        _Spread ("Sweep Spread", Range(0.01, 1)) = 0.5
        _Softness ("Edge Softness", Range(0.001, 0.2)) = 0.03

        [Header(Progress)]
        _Cover ("Cover", Range(0, 1)) = 0
        _Reveal ("Reveal", Range(0, 1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"   // the mesh UV must span the whole texture
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha   // premultiplied, as UI/Default in Unity 6
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                half4  color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _PatternTex;
            half4 _Color;
            half4 _TextureSampleAdd;
            float4 _ClipRect;
            float _PatternReverse;
            float _Rows;
            float _GridRotation;
            float _Angle;
            float _Spread;
            float _Softness;
            float _Cover;
            float _Reveal;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;   // vertex alpha carries the CanvasGroup alpha
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                // All positional math in float: at mediump, frac(uv * tiles) quantises visibly.
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 screenPos = (IN.texcoord - 0.5) * float2(aspect, 1.0);   // centred, square units

                // Square tiles on a grid rotated as a whole — like UIEffect's pattern presets (45°), which
                // turns the textures' square checker into diamonds while every shape stays inside its tile.
                float gridAngle = radians(_GridRotation);
                float gc = cos(gridAngle);
                float gs = sin(gridAngle);
                float2 gridPos = float2(gc * screenPos.x + gs * screenPos.y, -gs * screenPos.x + gc * screenPos.y) * _Rows;
                float2 id = floor(gridPos);
                float2 local = gridPos - id;

                // One threshold per tile (its centre, rotated back to screen space), so a shape grows and
                // shrinks whole.
                float2 tileCentre = (id + 0.5) / _Rows;
                float2 centre = float2(gc * tileCentre.x - gs * tileCentre.y, gs * tileCentre.x + gc * tileCentre.y);
                float angle = radians(_Angle);
                float2 dir = float2(cos(angle), sin(angle));               // defined in aspect space
                float halfRange = 0.5 * (abs(dir.x) * aspect + abs(dir.y));
                float g = saturate(dot(centre, dir) / (2.0 * halfRange) + 0.5);   // 0 = start corner

                float value = tex2D(_PatternTex, local).a;
                float fillOrder = lerp(value, 1.0 - value, _PatternReverse);       // low fills first

                float spread = max(_Spread, 0.01);
                float soft = max(_Softness, 0.0001);
                float coverT = saturate((_Cover * (1.0 + spread) - g) / spread);
                float revealT = saturate((_Reveal * (1.0 + spread) - g) / spread);

                // Remapped to [-soft, 1 + soft] so fill orders of exactly 0 and 1 are fully in or out
                // at the ends — no anti-aliasing seams left over a covered screen.
                float coverEdge = coverT * (1.0 + 2.0 * soft) - soft;
                float revealEdge = revealT * (1.0 + 2.0 * soft) - soft;
                float covered = smoothstep(-soft, soft, coverEdge - fillOrder);
                // Erased in reverse fill order: each shape shrinks back to where it started growing.
                float revealed = smoothstep(-soft, soft, revealEdge - (1.0 - fillOrder));

                half4 colour = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                colour.a *= (half)(covered * (1.0 - revealed));

                #ifdef UNITY_UI_CLIP_RECT
                colour.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(colour.a - 0.001);
                #endif

                colour.rgb *= colour.a;   // premultiply LAST, after every alpha factor
                return colour;
            }
        ENDCG
        }
    }
}
