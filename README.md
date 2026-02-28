# URPCameraStackDepthUtility (Work in progress)
A custom URP Renderer Feature for Unity 6+ that captures and combines depth textures from stacked cameras.  
It preserves depth per selected camera )and merges them progressively using a configurable merge mode.

## Overview
For each selected camera in the stack:
1. The camera depth texture is copied into an internal RTHandle list.
2. If more than one camera is selected, depth is merged progressively in stack order.

You can access:
- The preserved depth of any selected camera.
- The final merged depth of all selected cameras

## Features
- Access per-camera depth  
- Access stacked depth from selected cameras  
- Configurable depth merge mode  
- Compatible with Render Graph (Unity 6+)  
- Lightweight and extensible design

## Setup
1. Open your `Universal Renderer Data`.
2. Add `CameraStackDepthFeature`
3. Assign the included `Blit To Screen` material (or your own).
4. Select the cameras you want depth access to.
5. Chooes a merge mode.
<img width="675" height="257" alt="image" src="https://github.com/user-attachments/assets/4d17d0da-3ca0-4312-b91e-61258b07f574" />


### Merge Mode
Controls how depth is combined during each merge step.  
- **Overlay** - Front camera depth overrides back camera depth.  
- **Maximum** - Keeps the larger depth value between the two layers.

Depth is merged in the order cameras render.

## How to use
### Access Per-Camera Depth
Each selected camera’s depth is preserved into an internal RTHandle.  
You can access it through `m_DepthTextureStack` in your own render pass.

**Example: Using Depth in a Custom Render Pass**



Inside `CameraStackDepthFeature.cs`:
```C#
class CameraStackDepthPass : ScriptableRenderPass
{
    // ......


    class MyPassData
    {
        public Material myMaterial;
        public TextureHandle depth;
    }

    Material m_MyMaterial;

    static void ExecuteMyPass(MyPassData data, RasterGraphContext context)
    {
        // Set the texure to your material.
        data.myMaterial.SetTexture("_TextureName", data.depth);

        // Blit it to the screen.
        Blitter.BlitTexture(context.cmd, data.depth, new Vector4(1, 1, 0, 0), data.myMaterial, 0); // The source doesn't matter in this case as the final output depends on myMaterial.
    }

    public void Setup(List<Camera> cameraStack, List<bool> selectedCameras, ref List<RTHandle> depthTextureStack,
        ref List<RTHandle> mergeTextureStack, Material displayMaterial, Material mergeMaterial, Material myMaterial)
    {
        // ......


        m_MyMaterial = myMaterial;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resourceData = frameData.Get<UniversalResourceData>();


        // ......


        // Only execute the pass in the last camera or the camera after the depth texture you want.
        if (cameraId == m_CameraStack.Count - 1)
        {
            using (var builder = renderGraph.AddRasterRenderPass<MyPassData>("My Pass", out var passData))
            {
                // Get the depth texture in the second overlay camera.
                // If you want the depth texture of the base camera, you can simply replace index 1 with 0.
                var depthTexture = renderGraph.ImportTexture(m_DepthTextureStack[1]);
                
                passData.depth = depthTexture;
                passData.myMaterial = m_Material;
                
                builder.UseTexture(passData.depth);

                // Set your render target.
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
        
                builder.SetRenderFunc((MyPassData data, RasterGraphContext context) =>
                    ExecuteMyPass(data, context));
            }
        }
    }
}
```
