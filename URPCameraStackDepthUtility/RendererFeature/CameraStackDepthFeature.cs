using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class CameraStackDepthFeature : ScriptableRendererFeature
{
    class CameraStackDepthPass : ScriptableRenderPass
    {
        List<Camera> m_CameraStack;
        private List<RTHandle> m_DepthTextureStack;
        private List<RTHandle> m_MergeTextureStack;
        private List<bool> m_ActiveCameras;

        private Material m_DisplayMaterial;
        private Material m_MergeMaterial;

        class DepthPassData
        {
            public TextureHandle source;
        }

        class MergePassData
        {
            public TextureHandle backDepth;
            public TextureHandle frontDepth;
            public Material material;
        }

        static void ExecuteDepthPreservationPass(DepthPassData data, RasterGraphContext context)
        {
            // Blit current camera depth to render target.
            Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);

            // RTHandles.Release(data.source);
        }

        static void ExecuteDepthMergePass(MergePassData data, RasterGraphContext context)
        {
            // Set material textures.
            data.material.SetTexture(BackDepthTex, data.backDepth);
            data.material.SetTexture(FrontDepthTex, data.frontDepth);
            Blitter.BlitTexture(context.cmd, data.backDepth, new Vector4(1, 1, 0, 0), data.material,
                0); // The source doesn't matter
        }

        public void Setup(List<Camera> cameraStack, List<bool> activeCameras, ref List<RTHandle> depthTextureStack,
            ref List<RTHandle> mergeTextureStack, Material displayMaterial, Material mergeMaterial)
        {
            m_CameraStack = cameraStack;
            m_ActiveCameras = activeCameras;
            m_DepthTextureStack = depthTextureStack;
            m_MergeTextureStack = mergeTextureStack;

            m_DisplayMaterial = displayMaterial;
            m_MergeMaterial = mergeMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // --- Setup ---
            var resourceData = frameData.Get<UniversalResourceData>();

            // Get camera data.
            var cameraData = frameData.Get<UniversalCameraData>();

            if (cameraData.camera.cameraType != CameraType.Game) return;

            // Get camera id.
            var activeCameraCount = m_ActiveCameras.Count(active => active);

            var activeCameraId = -1;
            var cameraId = -1;
            var active = false;
            for (int i = 0; i < m_ActiveCameras.Count; i++)
            {
                cameraId++;
                if (m_ActiveCameras[i])
                {
                    activeCameraId++;
                    if (cameraData.camera == m_CameraStack[i])
                    {
                        active = true;
                        break;
                    }
                }
            }

            if (active)
            {
                // Debug.Log($"RenderGraph called. Camera Name: {cameraData.camera.name}. Camera ID: {cameraId}");

                // --- Depth Preservation Pass ---
                using (var builder =
                       renderGraph.AddRasterRenderPass<DepthPassData>("Depth Preservation Pass", out var passData))
                {
                    // Set render target.
                    var destination = renderGraph.ImportTexture(m_DepthTextureStack[activeCameraId]);
                    builder.SetRenderAttachment(destination, 0);

                    // Set depth texture.
                    passData.source = resourceData.cameraDepthTexture;

                    // Set the depth texture as readable.
                    builder.UseTexture(passData.source);

                    builder.SetRenderFunc((DepthPassData data, RasterGraphContext context) =>
                        ExecuteDepthPreservationPass(data, context));
                }

                // --- Depth Merge Pass ---
                if (activeCameraId != 0) // Skip first camera.
                {
                    using (var builder =
                           renderGraph.AddRasterRenderPass<MergePassData>("Depth Merge Pass", out var passData))
                    {
                        // Set render target.
                        var destination = renderGraph.ImportTexture(m_MergeTextureStack[activeCameraId - 1]);
                        builder.SetRenderAttachment(destination, 0);

                        // Set depth to merge.
                        if (activeCameraId == 1)
                        {
                            passData.backDepth = renderGraph.ImportTexture(m_DepthTextureStack[0]);
                        }
                        else
                        {
                            passData.backDepth = renderGraph.ImportTexture(m_MergeTextureStack[activeCameraId - 2]);
                        }

                        passData.frontDepth = renderGraph.ImportTexture(m_DepthTextureStack[activeCameraId]);

                        // Set material.
                        passData.material = m_MergeMaterial;
                        builder.UseTexture(passData.backDepth);
                        builder.UseTexture(passData.frontDepth);

                        builder.SetRenderFunc((MergePassData data, RasterGraphContext context) =>
                            ExecuteDepthMergePass(data, context));
                    }
                }
            }

            // --- Example Blit Texture Pass ---
            if (cameraId == m_CameraStack.Count - 1) // Only called in the last camera.
            {
                var source =
                    renderGraph.ImportTexture(activeCameraCount > 1 ? m_MergeTextureStack[^1] : m_DepthTextureStack[0]);

                RenderGraphUtils.BlitMaterialParameters param = new(source, resourceData.activeColorTexture,
                    m_DisplayMaterial, 0);
                param.sourceTexturePropertyID = Shader.PropertyToID("_DepthTexture");
                renderGraph.AddBlitPass(param, passName: "Blit Depth Texture Pass");
            }
        }
    }
    
    public enum MergeMode
    {
        Overlay,
        Maximum
    }

    [SerializeField]
    RenderPassEvent m_PassEvent = RenderPassEvent.AfterRenderingTransparents;
    
    [SerializeField] 
    private Material m_BlitMaterial;
    
    [Header("Merge Settings")]
    [SerializeField] [Tooltip("Select how stacked camera depth textures are combined.\n\n" +
         "Overlay: Front camera depth overrides back depth.\n" +
         "Maximum: Uses the larger depth value between layers.")]
    private MergeMode m_MergeMode = MergeMode.Maximum;

    [Header("Camera")]
    [SerializeField]
    private List<bool> m_ActiveCameras = new List<bool>();

    [HideInInspector] [SerializeField]
    private Shader m_MergeShader;
    private Material m_MergeMaterial;
    
    private static readonly string LogPrefix = $"[{nameof(CameraStackDepthFeature)}] ";
    
    private static readonly int BackDepthTex = Shader.PropertyToID("_BackDepthTex");
    private static readonly int FrontDepthTex = Shader.PropertyToID("_FrontDepthTex");
    
    private List<Camera> m_CameraStack;
    private List<RTHandle> m_DepthTextureStack;
    private List<RTHandle> m_MergeTextureStack;

    private bool m_NeedRefreshStack;

    CameraStackDepthPass m_CameraStackCameraStackDepthPass;
    
    /// <inheritdoc/>
    public override void Create()
    {
        m_CameraStackCameraStackDepthPass = new CameraStackDepthPass();
        m_CameraStackCameraStackDepthPass.renderPassEvent = m_PassEvent;
        
        m_DepthTextureStack = new List<RTHandle>();
        m_MergeTextureStack = new List<RTHandle>();
        
        // Get shader.
        if (!m_MergeShader)
        {
            m_MergeShader = Shader.Find("Shader Graphs/Merge Depth");
            if (!m_MergeShader)
            {
                Debug.LogWarning(LogPrefix + 
                                 "Included shader 'Merge Depth' could not be found. " +
                                 "Ensure the URPCameraStackDepthUtility shader files are present " +
                                 "and have not been moved or deleted.");
                return;
            }
        }
        
        // Create material.
        m_MergeMaterial = CoreUtils.CreateEngineMaterial(m_MergeShader);
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.camera.cameraType != CameraType.Game) return;
        
        var mainCamera = TryGetMainCamera();
        if (!mainCamera) return;

        if (!m_BlitMaterial)
        {
            Debug.LogWarning(LogPrefix +
                             "Missing Display Material. Please assign 'URPCameraStackDepthUtility/Shaders/Blit To Screen.mat' " +
                             "in the Renderer Feature settings. You may replace it with your own material or " +
                             "rewrite the renderer feature and passe to meet your need.");
            return;
        }
        
        if (m_NeedRefreshStack)
        {
            RefreshStackIfNeeded();
            m_NeedRefreshStack = false;
        }
        
        // Get camera count.
        var additionalData = mainCamera.GetUniversalAdditionalCameraData();
        if (m_CameraStack == null)
            m_CameraStack = new List<Camera>();
        
        if (!HasActiveCamera(m_ActiveCameras)) return;

        m_CameraStack.Clear();
        m_CameraStack.Add(mainCamera);
        m_CameraStack.AddRange(additionalData.cameraStack);
            
        RefreshActiveCamerasListIfNeeded(additionalData);
        RefreshStackIfNeeded();
        UpdateMergeKeyword();
        
        // Set up the correct data for the render pass, and transfers the data from the renderer feature to the render pass.
        m_CameraStackCameraStackDepthPass.Setup(m_CameraStack, m_ActiveCameras, ref m_DepthTextureStack, ref m_MergeTextureStack, m_BlitMaterial, m_MergeMaterial);
        renderer.EnqueuePass(m_CameraStackCameraStackDepthPass);
    }

    protected override void Dispose(bool disposing)
    {
        m_CameraStackCameraStackDepthPass = null;
        
        foreach (var depthTexture in m_DepthTextureStack)
        {
            depthTexture?.Release();
        }

        foreach (var mergeTexture in m_MergeTextureStack)
        {
            mergeTexture?.Release();
        }
        m_DepthTextureStack.Clear();
        m_MergeTextureStack.Clear();
        
        CoreUtils.Destroy(m_MergeMaterial);
    }

    // Called when inspector value change.
    private void OnValidate()
    {
        // var mainCamera = TryGetMainCamera();
        var mainCamera = Camera.main;  // Here, we use Camera.main instead of TryGetMainCamera() to prevent
                                       // logging warning when project first opened.
        if (!mainCamera) return;

        var additionalData = mainCamera.GetUniversalAdditionalCameraData();

        m_NeedRefreshStack = true;
    }
    
    private void UpdateMergeKeyword()
    {
        if (!m_MergeMaterial) return;

        var maximumKeyword = new LocalKeyword(m_MergeShader, "_MERGEMODE_MAXIMUM");
        var overlayKeyword = new LocalKeyword(m_MergeShader, "_MERGEMODE_OVERLAY");

        switch (m_MergeMode)
        {
            case MergeMode.Maximum:
                m_MergeMaterial.SetKeyword(maximumKeyword, true);
                m_MergeMaterial.SetKeyword(overlayKeyword, false);
                break;

            case MergeMode.Overlay:
                m_MergeMaterial.SetKeyword(maximumKeyword, false);
                m_MergeMaterial.SetKeyword(overlayKeyword, true);
                break;
        }
    }

    private static Camera TryGetMainCamera()
    {
        var mainCamera = Camera.main;
        if (mainCamera) return mainCamera;

        Debug.LogWarning(LogPrefix +
                         "No Main Camera found in the scene. " +
                         "Ensure a camera is tagged as 'MainCamera'.");
        return null;
    }

    private static bool HasActiveCamera(List<bool> activeCameras)
    {
        if (activeCameras.Any(active => active)) return true;

        Debug.LogWarning(LogPrefix + 
                         "No active camera found. Please at least set one camera to active in the renderer feature.");
        return false;
    }

    private void RefreshActiveCamerasListIfNeeded(UniversalAdditionalCameraData data)
    {
        if (data.cameraStack.Count + 1 == m_ActiveCameras.Count) return;

        m_ActiveCameras.Clear();
        for (int i = 0; i < data.cameraStack.Count + 1; i++)
        {
            m_ActiveCameras.Add(true);
        }
    }
    
    private void RefreshStackIfNeeded()
    {
        var activeCameraCount = m_ActiveCameras.Count(active => active);

        if (activeCameraCount == m_DepthTextureStack.Count &&
            activeCameraCount == m_MergeTextureStack.Count + 1) return;
        
        // Dispose of texture.
        foreach (var depthTexture in m_DepthTextureStack)
        {
            depthTexture?.Release();
        }

        foreach (var mergeTexture in m_MergeTextureStack)
        {
            mergeTexture?.Release();
        }
        m_DepthTextureStack.Clear();
        m_MergeTextureStack.Clear();
        
        // Setup descriptor.
        RenderTextureDescriptor desc = new RenderTextureDescriptor(Screen.width, Screen.height);
        desc.graphicsFormat = GraphicsFormat.R32_SFloat;
        desc.depthStencilFormat = GraphicsFormat.None;
        desc.msaaSamples = 1;
        
        // Setup depth stack.
        for (int i = 0; i < activeCameraCount; i++)
        {
            RTHandle depthTextureHandle = null;
            RenderingUtils.ReAllocateHandleIfNeeded(ref depthTextureHandle, desc, name: "_StackedDepthTexture" + (i + 1));
            
            m_DepthTextureStack.Add(depthTextureHandle);
        }
        
        // Setup merge stack.
        for (int i = 0; i < activeCameraCount - 1; i++)
        {
            RTHandle mergeTextureHandle = null;
            RenderingUtils.ReAllocateHandleIfNeeded(ref mergeTextureHandle, desc, name: "_MergedDepthTexture" + (i + 1));
            
            m_MergeTextureStack.Add(mergeTextureHandle);
        }
    }
}