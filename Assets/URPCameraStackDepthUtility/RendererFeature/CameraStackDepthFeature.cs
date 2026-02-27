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
            public TextureHandle depth1;
            public TextureHandle depth2;
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
            data.material.SetTexture(DepthTex1, data.depth1);
            data.material.SetTexture(DepthTex2,  data.depth2);
            Blitter.BlitTexture(context.cmd, data.depth1, new Vector4(1, 1, 0, 0), data.material, 0);  // The source doesn't matter
        }
        
        public void Setup(List<Camera> cameraStack, List<bool> activeCameras, ref List<RTHandle> depthTextureStack, ref List<RTHandle> mergeTextureStack, Material displayMaterial, Material mergeMaterial)
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
                using (var builder = renderGraph.AddRasterRenderPass<DepthPassData>("Depth Preservation Pass", out var passData))
                {
                    // Set render target.
                    var destination = renderGraph.ImportTexture(m_DepthTextureStack[activeCameraId]);
                    builder.SetRenderAttachment(destination, 0);
                    
                    // Set depth texture.
                    passData.source = resourceData.cameraDepthTexture;
                    
                    // Set the depth texture as readable.
                    builder.UseTexture(passData.source);
                    
                    builder.SetRenderFunc((DepthPassData data, RasterGraphContext context) => ExecuteDepthPreservationPass(data, context));
                }
                
                // --- Depth Merge Pass ---
                if (activeCameraId != 0)  // Skip first camera.
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
                            passData.depth1 = renderGraph.ImportTexture(m_DepthTextureStack[0]);
                        }
                        else
                        {
                            passData.depth1 = renderGraph.ImportTexture(m_MergeTextureStack[activeCameraId - 2]);
                        }
                        passData.depth2 = renderGraph.ImportTexture(m_DepthTextureStack[activeCameraId]);
                        
                        // Set material.
                        passData.material = m_MergeMaterial;
                        builder.UseTexture(passData.depth1);
                        builder.UseTexture(passData.depth2);
                        
                        builder.SetRenderFunc((MergePassData data, RasterGraphContext context) => ExecuteDepthMergePass(data, context));
                    }
                }
            }

            // --- Blit Texture Pass ---
            if (cameraId == m_CameraStack.Count - 1) // Only called in the last camera.
            {
                var source = renderGraph.ImportTexture(activeCameraCount > 1 ? m_MergeTextureStack[^1] : m_DepthTextureStack[0]);
            
                RenderGraphUtils.BlitMaterialParameters param = new(source, resourceData.activeColorTexture,
                    m_DisplayMaterial, 0);
                param.sourceTexturePropertyID = Shader.PropertyToID("_DepthTexture");
                renderGraph.AddBlitPass(param, passName: "Blit Depth Texture Pass");
            }
        }
    }
    
    [SerializeField]
    RenderPassEvent m_PassEvent = RenderPassEvent.AfterRenderingTransparents;
    
    [SerializeField] 
    private Material m_DisplayMaterial;

    [Header("Camera")]
    [SerializeField]
    private List<bool> m_ActiveCameras = new List<bool>();

    [HideInInspector] [SerializeField]
    private Shader m_MergeShader;
    private Material m_MergeMaterial;
    
    private static readonly string LogPrefix = $"[{nameof(CameraStackDepthFeature)}] ";
    
    private static readonly int DepthTex1 = Shader.PropertyToID("_DepthTex1");
    private static readonly int DepthTex2 = Shader.PropertyToID("_DepthTex2");
    
    private List<Camera> m_CameraStack;
    private List<RTHandle> m_DepthTextureStack;
    private List<RTHandle> m_MergeTextureStack;

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
        var mainCamera = TryGetMainCamera();
        if (!mainCamera) return;

        if (!m_DisplayMaterial)
        {
            Debug.LogWarning(LogPrefix +
                             "Missing Display Material. Please assign 'URPCameraStackDepthUtility/Shaders/Show Depth.mat' " +
                             "in the Renderer Feature settings. You may replace it with your own material or " +
                             "rewrite the renderer feature and passe to meet your need.");
            return;
        }

        if (!m_MergeMaterial)
        {
            Debug.LogWarning(LogPrefix +
                             "Missing Merge Material. Please assign 'URPCameraStackDepthUtility/Shaders/Merge Depth.mat' " +
                             "in the Renderer Feature settings.");
            return;
        }
        
        // Get camera count.
        var additionalData = mainCamera.GetUniversalAdditionalCameraData();
        if (m_CameraStack == null)
            m_CameraStack = new List<Camera>();

        if (!HasOverlayCamera(additionalData)) return;
        if (!HasActiveCamera(m_ActiveCameras)) return;

        m_CameraStack.Clear();
        m_CameraStack.Add(mainCamera);
        m_CameraStack.AddRange(additionalData.cameraStack);
            
        RefreshActiveCamerasListIfNeeded(additionalData);
        RefreshStackIfNeeded();
        
        // Set up the correct data for the render pass, and transfers the data from the renderer feature to the render pass.
        m_CameraStackCameraStackDepthPass.Setup(m_CameraStack, m_ActiveCameras, ref m_DepthTextureStack, ref m_MergeTextureStack, m_DisplayMaterial, m_MergeMaterial);
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
    void OnValidate()
    {
        var mainCamera = TryGetMainCamera();
        if (!mainCamera) return;

        var additionalData = mainCamera.GetUniversalAdditionalCameraData();
        
        if (!HasOverlayCamera(additionalData)) return;

        RefreshStackIfNeeded();
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

    private static bool HasOverlayCamera(UniversalAdditionalCameraData data)
    {
        if (data.cameraStack.Count != 0) return true;
        
        Debug.LogWarning(LogPrefix +
                         "No Overlay Cameras detected in the Base Camera stack. " +
                         "Depth stacking requires Camera Stacking with at least one Overlay Camera.");
        return false;

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
