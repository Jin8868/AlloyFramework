Shader "AlloyFramework/UI/BackgroundBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SeparableGaussianBlur"
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment
            #pragma target 3.0
            // 先定义 URP 纹理采样宏，再引入 Blit 工具。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _BlurOffset;

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 nearOffset = _BlurOffset.xy * 1.3846153846;
                float2 farOffset = _BlurOffset.xy * 3.2307692308;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.2270270270;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + nearOffset) * 0.3162162162;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - nearOffset) * 0.3162162162;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + farOffset) * 0.0702702703;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - farOffset) * 0.0702702703;
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }
    }
}
