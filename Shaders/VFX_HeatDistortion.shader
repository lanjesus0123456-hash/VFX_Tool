// ============================================================
//  VFX/HeatDistortion — 热扭曲 Shader（GrabPass 版）
//
//  实现原理：
//  GrabPass 捕获场景 + 双层噪声叠加偏移 UV + 重采样
//
//  使用方式：
//  1. 创建一个 Quad，赋予此材质
//  2. 放置在热扭曲区域（火焰/岩浆/爆炸边缘）
//  3. 调整 _DistortStrength 和 _Speed
//
//  URP 适配：GrabPass 需替换为 _CameraOpaqueTexture
//
//  参数说明：
//  _NoiseTex       : 噪声贴图（推荐 Perlin Noise）
//  _DistortStrength: 扭曲强度（建议 0.01~0.05）
//  _Speed          : 噪声滚动速度
//  _NoiseScale     : 噪声缩放
// ============================================================
Shader "VFX/HeatDistortion"
{
    Properties
    {
        _NoiseTex        ("Noise Texture",      2D)          = "bump" {}
        _DistortStrength ("Distort Strength",   Range(0,0.1)) = 0.02
        _Speed           ("Scroll Speed",       Range(0,5))   = 0.5
        _NoiseScale      ("Noise Scale",        Range(0.1,5)) = 1
        _AlphaMask       ("Alpha Mask (opt)",   2D)           = "white" {}
        [Toggle]
        _UseMask         ("Use Alpha Mask",     Float)        = 0
    }

    SubShader
    {
        GrabPass { "_HeatGrabTex" }

        Tags
        {
            "Queue"          = "Transparent+1"
            "RenderType"     = "Transparent"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature _USEMASK_ON
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float4 grabUV  : TEXCOORD0;
                float2 uv      : TEXCOORD1;
                float4 color   : COLOR;
            };

            sampler2D _HeatGrabTex;
            sampler2D _NoiseTex;
            sampler2D _AlphaMask;
            float4    _NoiseTex_ST;
            float     _DistortStrength;
            float     _Speed;
            float     _NoiseScale;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.grabUV = ComputeGrabScreenPos(o.pos);
                o.uv     = TRANSFORM_TEX(v.uv, _NoiseTex) * _NoiseScale;
                o.color  = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 双层噪声叠加：Y方向速度更快，形成上升感
                float2 uv1 = i.uv + float2(0, _Time.y * _Speed);
                float2 uv2 = i.uv * 0.7 + float2(0.1, _Time.y * _Speed * 0.6);

                float2 noise1 = (tex2D(_NoiseTex, uv1).rg * 2.0 - 1.0);
                float2 noise2 = (tex2D(_NoiseTex, uv2).rg * 2.0 - 1.0);
                float2 noise  = (noise1 + noise2) * 0.5 * _DistortStrength;

                float4 grabUV = i.grabUV;
                grabUV.xy += noise * grabUV.w;

                fixed4 distorted = tex2Dproj(_HeatGrabTex, grabUV);

                float mask = 1.0;
                #if _USEMASK_ON
                    mask = tex2D(_AlphaMask, i.uv / _NoiseScale).r;
                #endif

                distorted.a = i.color.a * mask;
                return distorted;
            }
            ENDCG
        }
    }
}
