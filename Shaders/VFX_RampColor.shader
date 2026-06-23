// ============================================================
//  VFX/RampColor — 颜色重映射 Shader
//
//  核心逻辑：
//  采样主贴图 → 取指定通道值 → 作为 U 坐标采样 Ramp 贴图
//  → 输出重映射后的颜色
//
//  使用方式：
//  1. MainTex：原始特效贴图（灰度或彩色均可）
//  2. RampTex：颜色渐变贴图，建议 256×1，Wrap Mode = Clamp
//  3. 调整 _RampSource 选择采样通道（默认亮度）
//
//  修复日志 2026-03-31：
//  1. 移除 CustomEditor "ShaderGUI" — 该类不存在会报错
//
//  参数说明：
//  _RampSource  : 0=亮度, 1=R, 2=G, 3=B, 4=A
//  _RampOffset  : Ramp UV 偏移（整体色相偏移）
//  _RampPower   : 对比度调整
//  _AlphaSource : 0=主贴图Alpha, 1=Ramp贴图Alpha
// ============================================================
Shader "VFX/RampColor"
{
    Properties
    {
        _MainTex        ("Main Texture",           2D)          = "white" {}
        _RampTex        ("Ramp Texture (256x1)",   2D)          = "white" {}
        _Color          ("Color Tint",             Color)        = (1,1,1,1)

        [Header(Ramp Sampling)]
        [KeywordEnum(Luminance, R, G, B, A)]
        _RampSource     ("Ramp Sample Source",     Float)        = 0
        _RampOffset     ("Ramp UV Offset",         Range(-1,1))  = 0
        _RampPower      ("Ramp Contrast",          Range(0.1,5)) = 1

        [Header(Alpha)]
        [KeywordEnum(Main, Ramp)]
        _AlphaSource    ("Alpha Source",           Float)        = 0

        [Header(Blend)]
        [Toggle]
        _UseVertexColor ("Multiply Vertex Color",  Float)        = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "IgnoreProjector"= "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature _RAMPSOURCE_LUMINANCE _RAMPSOURCE_R _RAMPSOURCE_G _RAMPSOURCE_B _RAMPSOURCE_A
            #pragma shader_feature _ALPHASOURCE_MAIN _ALPHASOURCE_RAMP
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            sampler2D _RampTex;
            float4    _Color;
            float     _RampOffset;
            float     _RampPower;
            float     _UseVertexColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = lerp(float4(1,1,1,1), v.color, _UseVertexColor) * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 main = tex2D(_MainTex, i.uv);

                // 选择采样通道
                float rampU;
                #if defined(_RAMPSOURCE_R)
                    rampU = main.r;
                #elif defined(_RAMPSOURCE_G)
                    rampU = main.g;
                #elif defined(_RAMPSOURCE_B)
                    rampU = main.b;
                #elif defined(_RAMPSOURCE_A)
                    rampU = main.a;
                #else   // Luminance（默认）
                    rampU = dot(main.rgb, float3(0.299, 0.587, 0.114));
                #endif

                // 对比度 & 偏移
                rampU = saturate(pow(max(rampU, 0.0001), _RampPower) + _RampOffset);

                // 采样 Ramp（固定 V=0.5 避免边缘采样问题）
                fixed4 ramp = tex2D(_RampTex, float2(rampU, 0.5));

                // 合并输出
                fixed4 result;
                result.rgb = ramp.rgb * i.color.rgb;

                #if defined(_ALPHASOURCE_RAMP)
                    result.a = ramp.a * i.color.a;
                #else
                    result.a = main.a * i.color.a;
                #endif

                return result;
            }
            ENDCG
        }
    }

    // [FIX] 移除了错误的 CustomEditor "ShaderGUI" 声明
}
