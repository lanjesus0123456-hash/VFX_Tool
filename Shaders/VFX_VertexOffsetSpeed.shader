// ============================================================
//  VFX/VertexOffsetSpeed — 顶点偏移速度感 Shader
//
//  使用方式：
//  UV.y = 0  →  头部（当前位置，不偏移，alpha最高）
//  UV.y = 1  →  尾部（拖尾方向，最大偏移，alpha最低）
//
//  制作建议：
//  - 面片纵向分段数建议 ≥ 8，偏移效果更顺滑
//  - 配合粒子系统：将 _StretchDir 绑定粒子速度方向
//
//  修复日志 2026-03-31：
//  1. HeadSoftness 逻辑修复 — 原始条件判断导致柔化始终不生效
//
//  参数说明：
//  _StretchDir     : 拉伸方向
//  _StretchAmount  : 最大拉伸距离
//  _TailAlpha      : 尾部最终透明度（0=完全透明）
//  _TailCurve      : 尾部透明度衰减曲线指数
//  _HeadSoftness   : 头部边缘柔化范围（0=硬边）
// ============================================================
Shader "VFX/VertexOffsetSpeed"
{
    Properties
    {
        _MainTex        ("Main Texture",       2D)           = "white" {}
        _Color          ("Color",              Color)         = (1,1,1,1)
        [Header(Stretch)]
        _StretchDir     ("Stretch Direction",  Vector)        = (-1,0,0,0)
        _StretchAmount  ("Stretch Amount",     Range(0,20))  = 3
        [Header(Alpha)]
        _TailAlpha      ("Tail Alpha",         Range(0,1))   = 0
        _TailCurve      ("Tail Fade Curve",    Range(0.1,5)) = 1.5
        _HeadSoftness   ("Head Softness",      Range(0,0.5)) = 0.05
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
            #include "UnityCG.cginc"

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

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _Color;
            float4    _StretchDir;
            float     _StretchAmount;
            float     _TailAlpha;
            float     _TailCurve;
            float     _HeadSoftness;

            v2f vert(appdata v)
            {
                v2f o;

                float  t   = v.uv.y;
                float3 dir = normalize(_StretchDir.xyz);

                v.vertex.xyz += dir * t * _StretchAmount;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);

                // 尾部衰减：t=0 → 1.0, t=1 → _TailAlpha
                float tailFade = pow(1.0 - t, _TailCurve);
                float alpha    = lerp(_TailAlpha, 1.0, tailFade);

                // [FIX] 头部柔化：让 UV.y 接近 0 的顶点有一段柔和过渡
                // 原始代码: smoothstep(0, _HeadSoftness, t > 0.5 ? 1-t : t+0.5) — 永远 ≥0.5, 无效
                // 修复后: 直接用 t 做 smoothstep，UV.y=0 处 alpha=0 平滑过渡到 1
                if (_HeadSoftness > 0.001)
                    alpha *= smoothstep(0.0, _HeadSoftness, t);

                o.color   = v.color * _Color;
                o.color.a *= alpha;
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
