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

### Merge Mode
Controls how depth is combined during each merge step.  
- **Overlay** - Front camera depth overrides back camera depth.  
- **Maximum** - Keeps the larger depth value between the two layers.

Depth is merged in the order cameras render.

## How to use
