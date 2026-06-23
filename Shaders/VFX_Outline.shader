// ============================================================
//  VFX/Outline — 描边 Shader（顶点挤出法，双 Pass）
//
//  实现原理：
//  Pass 1：沿法线挤出，只渲染背面（描边色）
//  Pass 2：正常渲染模型正面
//
//  修复日志 2026-03-31：
//  1. Pass 1 添加 Blend SrcAlpha OneMinusSrcAlpha — 使 _OutlineAlpha 半透明生效
//
//  参数说明：
//  _OutlineWidth  : 描边宽度（建议 0.01~0.05）
//  _OutlineColor  : 描边颜色（HDR）
//  _OutlineAlpha  : 描边透明度
//
//  注意：低模硬边建议在建模时平滑法线（Smooth Normals）
// ============================================================
Shader "VFX/Outline"
{
    Properties
    {
        _MainTex      ("Main Texture",      2D)          = "white" {}
        _Color        ("Main Color",        Color)        = (1,1,1,1)
        [Header(Outline)]
        _OutlineWidth ("Outline Width",     Range(0,0.1)) = 0.02
        [HDR]
        _OutlineColor ("Outline Color",     Color)        = (1,0.8,0,1)
        _OutlineAlpha ("Outline Alpha",     Range(0,1))   = 1
        [Header(Main Pass)]
        [Enum(UnityEngine.Rendering.CullMode)]
        _CullMode     ("Cull Mode",         Float)        = 2   // Back
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // ── Pass 1：描边（背面挤出）───────────────────────────
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            // [FIX] 添加 Alpha Blend，使 _OutlineAlpha 半透明生效
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert_outline
            #pragma fragment frag_outline
            #include "UnityCG.cginc"

            float  _OutlineWidth;
            float4 _OutlineColor;
            float  _OutlineAlpha;

            struct appdata_outline
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f_outline
            {
                float4 pos : SV_POSITION;
            };

            v2f_outline vert_outline(appdata_outline v)
            {
                v2f_outline o;
                float3 extruded = v.vertex.xyz + v.normal * _OutlineWidth;
                o.pos = UnityObjectToClipPos(float4(extruded, 1));
                return o;
            }

            fixed4 frag_outline(v2f_outline i) : SV_Target
            {
                return fixed4(_OutlineColor.rgb, _OutlineAlpha);
            }
            ENDCG
        }

        // ── Pass 2：主体正常渲染 ─────────────────────────────
        Pass
        {
            Name "MAIN"
            Cull [_CullMode]
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}
