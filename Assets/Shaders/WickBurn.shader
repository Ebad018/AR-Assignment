// Wick that burns down: fragments with u > _Burn are discarded, the part just below the
// burn line glows orange, and the rest of the burnt end is charred. Drive _Burn from 1 -> 0.
Shader "AR/WickBurn"
{
    Properties
    {
        _BaseColor("Color", Color) = (0.55, 0.38, 0.2, 1)
        _CharColor("Char Color", Color) = (0.05, 0.03, 0.02, 1)
        _Burn("Burn", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CharColor;
                float _Burn;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float below = _Burn - IN.uv.x;   // how far this fragment is below the burn line
                clip(below);

                half3 col = lerp(_CharColor.rgb, _BaseColor.rgb, saturate(below * 6.0));
                col += half3(1.0, 0.45, 0.1) * saturate(1.0 - below * 25.0) * 1.5; // ember glow

                Light mainLight = GetMainLight();
                half ndl = saturate(dot(normalize(IN.normalWS), mainLight.direction)) * 0.6 + 0.4;
                return half4(col * ndl * mainLight.color, 1);
            }
            ENDHLSL
        }
    }
}
