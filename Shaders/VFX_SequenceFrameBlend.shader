// ============================================================
//  VFX/SequenceFrameBlend — 序列帧升格平滑 Shader
//
//  解决问题：
//  序列帧动画在升格（慢镜头）下帧率不足导致的卡顿感。
//  通过在相邻两帧之间做线性插值，产生平滑过渡效果。
//
//  使用方式：
//  1. 将此 Shader 赋予材质
//  2. 挂载 SequenceFrameBlend.cs 脚本（Runtime 文件夹）控制播放
//  3. 脚本会自动更新 _FrameA/_FrameB/_BlendFactor
//
//  参数说明：
//  _MainTex     : 序列帧贴图（Sprite Sheet）
//  _Cols        : 横向帧数（列数）
//  _Rows        : 纵向帧数（行数）
//  _FrameA      : 当前帧编号（从 0 开始，C# 脚本写入）
//  _FrameB      : 下一帧编号（C# 脚本写入）
//  _BlendFactor : 两帧之间的插值权重 0→1（C# 脚本写入）
//  _Color       : 颜色叠加（支持粒子顶点色）
// ============================================================
Shader "VFX/SequenceFrameBlend"
{
    Properties
    {
        _MainTex      ("Sprite Sheet",      2D)          = "white" {}
        _Color        ("Color",             Color)        = (1,1,1,1)
        [Header(Frame Control)]
        _Cols         ("Columns",           Float)        = 8
        _Rows         ("Rows",              Float)        = 8
        _FrameA       ("Frame A (Current)", Float)        = 0
        _FrameB       ("Frame B (Next)",    Float)        = 1
        _BlendFactor  ("Blend Factor",      Range(0,1))   = 0
        [Header(Alpha)]
        [Toggle]
        _UseVertexColor("Multiply Vertex Color", Float)  = 1
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
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _Color;
            float     _Cols;
            float     _Rows;
            float     _FrameA;
            float     _FrameB;
            float     _BlendFactor;
            float     _UseVertexColor;

            // 根据帧编号计算 UV（左上角为第0帧，从左到右从上到下）
            float2 FrameUV(float frameIndex, float2 uv, float cols, float rows)
            {
                float col = fmod(frameIndex, cols);
                float row = floor(frameIndex / cols);
                float2 cellSize = float2(1.0 / cols, 1.0 / rows);
                return (uv + float2(col, rows - 1.0 - row)) * cellSize;
            }

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
                float2 uvA = FrameUV(_FrameA, i.uv, _Cols, _Rows);
                float2 uvB = FrameUV(_FrameB, i.uv, _Cols, _Rows);

                fixed4 colA = tex2D(_MainTex, uvA);
                fixed4 colB = tex2D(_MainTex, uvB);

                fixed4 result = lerp(colA, colB, _BlendFactor);
                return result * i.color;
            }
            ENDCG
        }
    }
}
