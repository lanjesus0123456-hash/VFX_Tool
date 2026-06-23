// ============================================================
//  VFX/Dissolve — 溶解 Shader
//
//  实现原理：
//  噪声贴图亮度值与 _DissolveProgress 对比，
//  低于阈值的像素被 clip 裁切，阈值边缘产生发光效果。
//
//  使用方式：
//  1. 设置 _MainTex 和 _NoiseTex
//  2. 动画驱动 _DissolveProgress 0→1 溶解消失，1→0 反向出现
//
//  参数说明：
//  _DissolveProgress : 溶解进度（0=完整, 1=消失）
//  _EdgeWidth        : 发光边缘宽度
//  _EdgeColor        : 发光颜色（HDR）
//  _EdgeEmission     : 发光强度
//  _DissolveDir      : 方向性偏移（正值=从下往上消失）
// ============================================================
Shader "VFX/Dissolve"
{
    Properties
    {
        _MainTex          ("Main Texture",       2D)          = "white" {}
        _NoiseTex         ("Dissolve Noise",     2D)          = "white" {}
        _Color            ("Color",              Color)        = (1,1,1,1)
        [Header(Dissolve)]
        _DissolveProgress ("Dissolve Progress",  Range(0,1))  = 0
        _NoiseScale       ("Noise Scale",        Range(0.1,5))= 1
        _DissolveDir      ("Direction Bias",     Range(-1,1)) = 0
        [Header(Edge Glow)]
        _EdgeWidth        ("Edge Width",         Range(0,0.2)) = 0.05
        [HDR]
        _EdgeColor        ("Edge Color",         Color)        = (1,0.5,0,1)
        _EdgeEmission     ("Edge Emission",      Range(1,10))  = 3
        [Toggle]
        _UseVertexColor   ("Multiply Vertex Color", Float)    = 1
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
            sampler2D _NoiseTex;
            float4    _Color;
            float     _DissolveProgress;
            float     _NoiseScale;
            float     _DissolveDir;
            float     _EdgeWidth;
            float4    _EdgeColor;
            float     _EdgeEmission;
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

                float noise = tex2D(_NoiseTex, i.uv * _NoiseScale).r;

                // 方向性偏移
                noise += i.uv.y * _DissolveDir;
                noise  = saturate(noise);

                float threshold = _DissolveProgress;
                clip(noise - threshold);

                // 发光边缘
                float edgeFactor = 1.0 - smoothstep(threshold, threshold + _EdgeWidth, noise);
                edgeFactor = saturate(edgeFactor);

                fixed4 result = main * i.color;
                result.rgb = lerp(result.rgb, _EdgeColor.rgb * _EdgeEmission, edgeFactor);
                result.a   = main.a * i.color.a;

                return result;
            }
            ENDCG
        }
    }
}
