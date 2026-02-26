using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class CameraStackDepthFeature : ScriptableRendererFeature
{
    private static readonly string LogPrefix = $"[{nameof(CameraStackDepthFeature)}] ";
    
    private static readonly int DepthTex1 = Shader.PropertyToID("_DepthTex1");
    private static readonly int DepthTex2 = Shader.PropertyToID("_DepthTex2");
    
    class CameraStackDepthPass : ScriptableRenderPass
    {
        List<Camera> m_CameraStack;
        private List<RTHandle> m_DepthTextureStack;
        private List<RTHandle> m_MergeTextureStack;

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
        
        public void Setup(List<Camera> cameraStack, ref List<RTHandle> depthTextureStack, ref List<RTHandle> mergeTextureStack, Material displayMaterial, Material mergeMaterial)
        {
            m_CameraStack = cameraStack;
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
            
            // Get camera id.  index starts from 1, which is the base camera.
            var cameraId = -1;
            if (cameraData.renderType == CameraRenderType.Base)
            {
                cameraId = 1;
            }
            else
            {
                for (int i = 0; i < m_CameraStack.Count; i++)
                {
                    if (cameraData.camera == m_CameraStack[i])
                    {
                        cameraId = i + 2;
                        break;
                    }
                }
            }
            
            // Debug.Log("RenderGraph called. Camera ID: " + cameraId);
            
            // --- Depth Preservation Pass ---
            using (var builder = renderGraph.AddRasterRenderPass<DepthPassData>("Depth Preservation Pass", out var passData))
            {
                // Set render target.
                var destination = renderGraph.ImportTexture(m_DepthTextureStack[cameraId - 1]);
                builder.SetRenderAttachment(destination, 0);
                
                // Set depth texture.
                passData.source = resourceData.cameraDepthTexture;
                
                // Set the depth texture as readable.
                builder.UseTexture(passData.source);
                
                builder.SetRenderFunc((DepthPassData data, RasterGraphContext context) => ExecuteDepthPreservationPass(data, context));
            }
            
            // --- Depth Merge Pass ---
            if (cameraId != 1)  // Skip base camera.
            {
                using (var builder =
                       renderGraph.AddRasterRenderPass<MergePassData>("Depth Merge Pass", out var passData))
                {
                    // Set render target.
                    var destination = renderGraph.ImportTexture(m_MergeTextureStack[cameraId - 2]);
                    builder.SetRenderAttachment(destination, 0);
                    
                    // Set depth to merge.
                    if (cameraId == 2)
                    {
                        passData.depth1 = renderGraph.ImportTexture(m_DepthTextureStack[0]);
                    }
                    else
                    {
                        passData.depth1 = renderGraph.ImportTexture(m_MergeTextureStack[cameraId - 3]);
                    }
                    passData.depth2 = renderGraph.ImportTexture(m_DepthTextureStack[cameraId - 1]);
                    
                    // Set material.
                    passData.material = m_MergeMaterial;
                    builder.UseTexture(passData.depth1);
                    builder.UseTexture(passData.depth2);
                    
                    builder.SetRenderFunc((MergePassData data, RasterGraphContext context) => ExecuteDepthMergePass(data, context));
                }
            }

            // --- Blit Texture Pass ---
            if (cameraId == m_CameraStack.Count + 1) // Only called in the last camera.
            {
                var source = renderGraph.ImportTexture(m_MergeTextureStack[^1]);

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

    [HideInInspector] [SerializeField] 
    private Shader m_MergeShader;
    private Material m_MergeMaterial;
    
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
        Camera mainCamera = Camera.main;
        if (!mainCamera)
        {
            Debug.LogWarning(LogPrefix +
                             "No Main Camera found in the scene. " +
                             "Ensure a camera is tagged as 'MainCamera'.");
            return;
        }

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
        m_CameraStack.Clear();
        m_CameraStack.AddRange(additionalData.cameraStack);

        if (m_CameraStack.Count == 0)
        {
            Debug.LogWarning(LogPrefix +
                             "No Overlay Cameras detected in the Base Camera stack. " +
                             "Depth stacking requires Camera Stacking with at least one Overlay Camera.");
            return;
        }
        
        // Check stack size.
        RefreshStack(m_CameraStack.Count + 1);  // Plus one is for base camera.
        
        // Setup the correct data for the render pass, and transfers the data from the renderer feature to the render pass.
        m_CameraStackCameraStackDepthPass.Setup(m_CameraStack, ref m_DepthTextureStack, ref m_MergeTextureStack, m_DisplayMaterial, m_MergeMaterial);
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
    
    private void RefreshStack(int cameraCount)
    {
        if (cameraCount == m_DepthTextureStack.Count && cameraCount == m_MergeTextureStack.Count + 1) return;
        
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
        for (int i = 0; i < cameraCount; i++)
        {
            RTHandle depthTextureHandle = null;
            RenderingUtils.ReAllocateHandleIfNeeded(ref depthTextureHandle, desc, name: "_StackedDepthTexture" + (i + 1));
            
            m_DepthTextureStack.Add(depthTextureHandle);
        }
        
        // Setup merge stack.
        for (int i = 0; i < cameraCount - 1; i++)
        {
            RTHandle mergeTextureHandle = null;
            RenderingUtils.ReAllocateHandleIfNeeded(ref mergeTextureHandle, desc, name: "_MergedDepthTexture" + (i + 1));
            
            m_MergeTextureStack.Add(mergeTextureHandle);
        }
    }
}
