// ============================================================
//  VFX/PivotFracture — 轴点破碎 Shader
//
//  使用方式：
//  1. 在 DCC 工具中将网格预先破碎，每个碎片记录其本地中心点
//  2. 将碎片中心点（相对于整体模型空间）写入 UV2（xy = xz 平面偏移）
//  3. 用 _Progress 参数（0→1）驱动破碎动画
//
//  参数说明：
//  _Progress       : 破碎进度（0=完整, 1=完全爆散）
//  _Spread         : 爆散距离倍数
//  _RotSpeed       : 碎片旋转速度（度/Progress单位）
//  _ScaleFade      : 碎片最终缩放（0=消失, 1=保持原大小）
// ============================================================
Shader "VFX/PivotFracture"
{
    Properties
    {
        _MainTex    ("Main Texture",      2D)           = "white" {}
        _Color      ("Color",             Color)         = (1,1,1,1)
        _Progress   ("Fracture Progress", Range(0,1))   = 0
        _Spread     ("Spread Distance",   Range(0,10))  = 2
        _RotSpeed   ("Rotation Speed",    Range(0,720)) = 180
        _ScaleFade  ("Scale at End",      Range(0,1))   = 0
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
                float2 uv2    : TEXCOORD1;   // UV2：碎片轴点偏移
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
            float     _Progress;
            float     _Spread;
            float     _RotSpeed;
            float     _ScaleFade;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Rodrigues 旋转公式
            float3 RotateAroundAxis(float3 v, float3 axis, float angleDeg)
            {
                float rad = angleDeg * UNITY_PI / 180.0;
                float s = sin(rad), c = cos(rad);
                return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
            }

            v2f vert(appdata v)
            {
                v2f o;

                float2 pivot = v.uv2;
                float  seed  = hash(pivot);
                float  prog  = _Progress;

                // 爆散方向
                float3 outDir = normalize(float3(
                    pivot.x * 0.8 + (seed - 0.5) * 0.4,
                    (seed * 1.6 - 0.8),
                    pivot.y * 0.8 + (seed * 1.3 - 0.65) * 0.4
                ));

                // 位移
                float3 pos = v.vertex.xyz;
                pos += outDir * prog * _Spread * (0.4 + seed * 0.6);

                // 绕随机轴旋转
                float3 rotAxis = normalize(float3(seed - 0.5, seed * 1.3 - 0.65, 0.5 - seed * 0.8));
                float  rotAngle = prog * _RotSpeed * (seed * 2.0 - 1.0);
                float3 pivot3   = float3(pivot.x, 0, pivot.y);
                pos = pivot3 + RotateAroundAxis(pos - pivot3, rotAxis, rotAngle);

                // 缩放淡出
                float scale = lerp(1.0, _ScaleFade, prog * prog);
                pos = pivot3 + (pos - pivot3) * scale;

                o.pos     = UnityObjectToClipPos(float4(pos, 1));
                o.uv      = TRANSFORM_TEX(v.uv, _MainTex);
                o.color   = v.color * _Color;
                o.color.a *= (1.0 - prog * prog);
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
