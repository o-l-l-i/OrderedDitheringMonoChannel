using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using GraphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat;
using System;
using System.Collections.Generic;


[Serializable, VolumeComponentMenu("Post-processing/Custom/OrderedDitheringColorMonoEffect")]
public sealed class OrderedDitheringColorMonoEffect : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    [Tooltip("Controls the intensity of the effect.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    [Header("Resolution downscaling")]
    public ClampedIntParameter downscaleFactor = new ClampedIntParameter(4, 1, 8);

    [Header("Bayer matrix options")]
    public ClampedIntParameter bayerSize = new ClampedIntParameter(1, 1, 4, false);


    Material m_Material;
    MaterialPropertyBlock prop;


    // RT storage init
    Dictionary<int, RTStorage> rtStorage;


    // Get rendertargets
    RTStorage GetRTs(HDCamera camera)
    {
        RTStorage RTs;
        var cameraID = camera.camera.GetInstanceID();

        if (rtStorage.TryGetValue(cameraID, out RTs))
        {
            if (RTs.SizeChanged(camera))
            {
                RTs.Reallocate(camera);
            }
            else if (RTs.downScaleFactor != downscaleFactor.value)
            {
                RTs.downScaleFactor = downscaleFactor.value;
                RTs.Reallocate(camera);
            }
        }
        else
        {
            RTs = new RTStorage(camera);
            rtStorage[cameraID] = RTs;
        }

        return RTs;
    }


    public bool IsActive() => m_Material != null && intensity.value > 0f;

    public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterPostProcess;



    public override void Setup()
    {
        if (Shader.Find("Hidden/Shader/OrderedDitheringColorMonoShader") != null)
        {
            m_Material = CoreUtils.CreateEngineMaterial("Hidden/Shader/OrderedDitheringColorMonoShader");
        }

        prop = new MaterialPropertyBlock();
        rtStorage = new Dictionary<int, RTStorage>();
    }


    // Render pass identifiers
    enum Pass
    {
        Downsample,
        FinalImage
    }


    // Render post processing effect
    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
    {
        if (m_Material == null)
            return;


        // Dithering Bayer matrices, 2x2 to 8x8.
        float[] indexMatrix2x2 = new float[] { 0f, 2f,
                                               3f, 1f };

        float[] indexMatrix3x3 = new float[] { 0f, 7f, 3f,
                                               6f, 5f, 2f,
                                               4f, 1f, 8f };

        float[] indexMatrix4x4 = new float[] { 0,  8,  2, 10,
                                              12,  4, 14,  6,
                                               3, 11,  1,  9,
                                              15,  7, 13,  5 };

        float[] indexMatrix8x8 = new float[] {  0, 48, 12, 60,  3, 51, 15, 63,
                                               32, 16, 44, 28, 35, 19, 47, 31,
                                                8, 56,  4, 52, 11, 59,  7, 55,
                                               40, 24, 36, 20, 43, 27, 39, 23,
                                                2, 50, 14, 62,  1, 49, 13, 61,
                                               34, 18, 46, 30, 33, 17, 45, 29,
                                               10, 58,  6, 54,  9, 57,  5, 53,
                                               42, 26, 38, 22, 41, 25, 37, 21 };


        // Get the RT storage (new will be created if it does not exist.)
        var RTs = GetRTs(camera);

        // Set textures
        m_Material.SetTexture("_InputTexture", source);

        // Set parameters
        m_Material.SetFloat("_Intensity", intensity.value);
        m_Material.SetInt("_DownscaleFactor", downscaleFactor.value);
        m_Material.SetFloatArray("iMatrix2x2", indexMatrix2x2);
        m_Material.SetFloatArray("iMatrix3x3", indexMatrix3x3);
        m_Material.SetFloatArray("iMatrix4x4", indexMatrix4x4);
        m_Material.SetFloatArray("iMatrix8x8", indexMatrix8x8);

        // Set matrix enum.
        // Bayer 2x2
        if (bayerSize.value == 1)
        {
            m_Material.EnableKeyword("BAYER2X2_ON");
            m_Material.DisableKeyword("BAYER3X3_ON");
            m_Material.DisableKeyword("BAYER4X4_ON");
            m_Material.DisableKeyword("BAYER8X8_ON");
        }
        // Bayer 3x3
        else if (bayerSize.value == 2)
        {
            m_Material.DisableKeyword("BAYER2X2_ON");
            m_Material.EnableKeyword("BAYER3X3_ON");
            m_Material.DisableKeyword("BAYER4X4_ON");
            m_Material.DisableKeyword("BAYER8X8_ON");
        }
        // Bayer 4x4
        else if (bayerSize.value == 3)
        {
            m_Material.DisableKeyword("BAYER2X2_ON");
            m_Material.DisableKeyword("BAYER3X3_ON");
            m_Material.EnableKeyword("BAYER4X4_ON");
            m_Material.DisableKeyword("BAYER8X8_ON");
        }
        // Bayer 8x8
        else if (bayerSize.value == 4)
        {
            m_Material.DisableKeyword("BAYER2X2_ON");
            m_Material.DisableKeyword("BAYER3X3_ON");
            m_Material.DisableKeyword("BAYER4X4_ON");
            m_Material.EnableKeyword("BAYER8X8_ON");
        }


        // Downsample
        HDUtils.DrawFullScreen(cmd, m_Material, RTs.downsampled, prop, (int)Pass.Downsample);
        var down = RTs.downsampled;
        prop.SetTexture("_DownScaledTex", down);


        // Render final image
        HDUtils.DrawFullScreen(cmd, m_Material, destination, prop, (int)Pass.FinalImage);
    }


    // Perform clean up
    public override void Cleanup()
    {
        CoreUtils.Destroy(m_Material);
    }


    // RT storage
    sealed class RTStorage
    {
        // Camera base width and height
        int _baseWidth, _baseHeight;

        int _downscaleFactor = 4;
        public int downScaleFactor
        {
            set { _downscaleFactor = value; }
            get { return _downscaleFactor; }
        }

        // Render targets for this effect
        RTHandle _downsampled;
        public RTHandle downsampled { get { return _downsampled; } }


        public RTStorage(HDCamera camera)
        {
            Allocate(camera);
        }


        // Allocate new RTs
        void Allocate(HDCamera camera)
        {
            _baseWidth = camera.actualWidth;
            _baseHeight = camera.actualHeight;

            var width = _baseWidth / downScaleFactor;
            var height = _baseHeight / downScaleFactor;

            const GraphicsFormat rtFormat = GraphicsFormat.R16G16B16A16_SFloat;

            _downsampled = RTHandles.Alloc(width, height, colorFormat: rtFormat);
        }


        // Reallocate RTs
        public void Reallocate(HDCamera camera)
        {
            Release();
            Allocate(camera);
        }


        // Release old RTs
        public void Release()
        {
            if (_downsampled != null)
                RTHandles.Release(_downsampled);
        }


        // Check if camera size matches
        public bool SizeChanged(HDCamera camera)
        {
            if (_baseWidth == camera.actualWidth && _baseHeight == camera.actualHeight)
                return false;
            else
                return true;
        }
    }

}