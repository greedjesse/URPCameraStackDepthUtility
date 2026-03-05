# URPCameraStackDepthUtility

A custom URP Renderer Feature for Unity 6+ that provides access to depth textures from camera stacking.  
It preserves depth for selected cameras and merges them progressively using a configurable merge mode.

URP does not expose a combined depth texture for stacked cameras by default.  
This feature provides that functionality in a reusable and extensible way.

---

## Overview

For each selected camera in the stack:

1. The camera depth texture is copied into an internal RTHandle list.  
2. If more than one camera is selected, depth is merged progressively in stack order.

You can access:

- The preserved depth of any selected camera  
- The final merged depth of all selected cameras  

---

## Features

- Access per-camera depth  
- Access merged depth from selected cameras  
- Configurable depth merge mode  
- Compatible with Render Graph (Unity 6+)  
- Lightweight and extensible design  

---

## Setup

1. Open your **Universal Renderer Data**.  
2. Add `CameraStackDepthFeature`.  
3. Assign the included `Blit To Screen` material (or your own).  
4. Select the cameras you want depth access to.  
5. Choose a merge mode.

**!Impotant!**
Please make sure stacked cameras have the same clipping planes.
<img width="640" height="44" alt="image" src="https://github.com/user-attachments/assets/2f8ec71c-10a1-44f1-acc0-4c2fc890b8aa" />


---

## Merge Mode

Controls how depth is combined during each merge step.

- **Overlay** — Front camera depth overrides back camera depth.  
- **Maximum** — Keeps the larger depth value between the two layers.  

Depth is merged in the order the cameras render.

---

## How to Use

### Access Per-Camera Depth

Each selected camera’s depth is preserved in an internal RTHandle.  
You can access it through `m_DepthTextureStack` in your own render pass.

Example usage inside a custom Render Graph pass:

```csharp
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
        // Set the texture to your material.
        data.myMaterial.SetTexture("_TextureName", data.depth);

        // Blit it to the screen.
        Blitter.BlitTexture(context.cmd, data.depth, new Vector4(1, 1, 0, 0), data.myMaterial, 0);
    }

    public void Setup(......, Material myMaterial)
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
                // If you want the depth texture of the base camera, replace index 1 with 0.
                var depthTexture = renderGraph.ImportTexture(m_DepthTextureStack[1]);

                passData.depth = depthTexture;
                passData.myMaterial = m_MyMaterial;

                builder.UseTexture(passData.depth);

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc((MyPassData data, RasterGraphContext context) =>
                    ExecuteMyPass(data, context));
            }
        }
    }
}
```

---

### Access Final Merged Depth

If more than one camera is selected:

- Depth is merged progressively.  
- The final result is stored in the last merge RTHandle.  
- It becomes valid after the last selected camera renders.  

You can access the final merged depth through:

```csharp
m_MergeTextureStack[^1];
```

---

## Visual Examples

### Scene Setup
1 base camera, 2 overlay cameras.  
1 plane, 5 cubes (one is under the plane).

<img src="https://github.com/user-attachments/assets/e9bb01b6-0367-4871-ae16-c655f4ca5b3b" width="700">

---

### Per-Camera Depth

| **Main Camera (Base)** | **Overlay Camera 1** | **Overlay Camera 2** |
|------------------------|----------------------|----------------------|
| <img src="https://github.com/user-attachments/assets/a951d97f-1eea-46d9-a1e2-b0b746f083d1" width="400"> | <img src="https://github.com/user-attachments/assets/eff1cb54-a553-4b82-9a68-09ef2bcdb0e1" width="400"> | <img src="https://github.com/user-attachments/assets/d0033f90-2dab-4788-aa6a-9ceb8747c11c" width="400"> |

---

### Merge Result Comparison

| **Overlay Mode** | **Maximum Mode** |
|------------------|------------------|
| <img src="https://github.com/user-attachments/assets/192fa138-18ce-4fb2-886a-283d46943e6b" width="500"> | <img src="https://github.com/user-attachments/assets/37ea577d-73d9-4632-962a-e010420583c9" width="500"> |
