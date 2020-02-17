Shader "Hidden/Shader/OrderedDitheringColorMonoShader"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/FXAA.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/RTUpscale.hlsl"


    // Toggles
    #pragma multi_compile __ BAYER2X2_ON BAYER3X3_ON BAYER4X4_ON BAYER8X8_ON


    // Textures
    TEXTURE2D_X(_InputTexture);
    TEXTURE2D(_DownScaledTex);


    // Variables
    uniform float iMatrix2x2[4];
    uniform float iMatrix3x3[9];
    uniform float iMatrix4x4[16];
    uniform float iMatrix8x8[64];

    uniform float _Intensity;
    uniform uint _DownscaleFactor;


    // Structs
    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };


    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };


    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }


    // Monochromatic ordered dithering split to own method
    float Dither(float value, float limit)
    {
        if (value < limit)
            return 0.0;
        else
            return 1.0;
    }


    // Downsample
    float4 FragDownsample(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 ss = input.texcoord * _ScreenSize.xy;
        float3 color = LOAD_TEXTURE2D_X(_InputTexture, ss).rgb;

        return float4(color, 1);
    }


    // Final Image
    float4 FragFinalImage(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 positionSS = input.texcoord * _ScreenSize.xy;
        float3 color = SAMPLE_TEXTURE2D(_DownScaledTex, s_point_clamp_sampler, input.texcoord).xyz;

        // Resize UVs, then we can dither image.
        input.texcoord.x *= (_ScreenParams.x / _DownscaleFactor);
        input.texcoord.y *= (_ScreenParams.y / _DownscaleFactor);

        // Apply correct matrix selected in UI.
        uint width  = 4;
        uint height = 4;
        #if (BAYER2X2_ON)
            width  = 2;
            height = 2;
        #endif
        #if (BAYER3X3_ON)
            width  = 3;
            height = 3;
        #endif
        #if (BAYER4X4_ON)
            width  = 4;
            height = 4;
        #endif
        #if (BAYER8X8_ON)
            width  = 8;
            height = 8;
        #endif

        uint size = width * height;

        uint x = input.texcoord.x % width;
        uint y = input.texcoord.y % height;

        // Calculate index.
        int index = width * y + x;

        // Calculate limit value:
        float limit = 0;
        #if (BAYER2X2_ON)
            limit = iMatrix2x2[index] / size;
        #endif
        #if (BAYER3X3_ON)
            limit = iMatrix3x3[index] / size;
        #endif
        #if (BAYER4X4_ON)
            limit = iMatrix4x4[index] / size;
        #endif
        #if (BAYER8X8_ON)
            limit = iMatrix8x8[index] / size;
        #endif

        // Init dithered output.
        float4 c;

        // Dither each color channel separately.
        c.r = Dither(color.r, limit);
        c.g = Dither(color.g, limit);
        c.b = Dither(color.b, limit);

        c.a = 1.0; // Set alpha to 1.

        // Return the dithered image.
        return c;
    }


    ENDHLSL

    SubShader
    {

        Name "OrderedDitheringColorMonoShader"

        ZWrite Off
        ZTest Always
        Blend Off
        Cull Off


        // Renderpasses

        // 0: Prepass
        Pass
        {
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragDownsample
            ENDHLSL
        }

        // 1: Downsample
        Pass
        {
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragFinalImage
            ENDHLSL
        }

    }
    Fallback Off

}