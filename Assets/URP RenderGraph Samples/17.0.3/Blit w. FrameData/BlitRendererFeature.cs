using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 使用 frameData 通过多个 ScriptableRenderPass 来处理 Blit 操作的示例。
public class BlitRendererFeature : ScriptableRendererFeature
{
    // 存储在 frameData 中的类。它负责管理纹理资源。
    public class BlitData : ContextItem, IDisposable
    {
        // 用于 Blit 操作的纹理。
        RTHandle m_TextureFront;
        RTHandle m_TextureBack;
        // Render Graph 纹理句柄。
        TextureHandle m_TextureHandleFront;
        TextureHandle m_TextureHandleBack;

        // ScaleBias 用于控制 Blit 操作的缩放和偏移。
        // x 和 y 控制缩放，z 和 w 控制偏移。
        static Vector4 scaleBias = new Vector4(1f, 1f, 0f, 0f);

        // 用于记录哪一个纹理是最新的。
        bool m_IsFront = true;

        // 包含最近一次 Blit 操作后颜色缓冲区的纹理。
        public TextureHandle texture;

        // 初始化 BlitData 的函数。每帧使用此类前都应调用。
        public void Init(RenderGraph renderGraph, RenderTextureDescriptor targetDescriptor, string textureName = null)
        {
            // 检查纹理名称是否有效，如果无效则使用默认值。
            var texName = String.IsNullOrEmpty(textureName) ? "_BlitTextureData" : textureName;
            // 如果 RTHandle 尚未初始化，或者 targetDescriptor 相比上一帧已改变，则重新分配。
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TextureFront, targetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: texName + "Front");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TextureBack, targetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: texName + "Back");
            // 通过将 RTHandle 导入 Render Graph 来创建纹理句柄。
            m_TextureHandleFront = renderGraph.ImportTexture(m_TextureFront);
            m_TextureHandleBack = renderGraph.ImportTexture(m_TextureBack);
            // 将活动纹理设置为前缓冲。
            texture = m_TextureHandleFront;
        }

        // 每帧都需要重置纹理句柄以避免持有无效的纹理句柄，
        // 因为纹理句柄只在一帧内有效。
        public override void Reset()
        {
            // 重置颜色缓冲区，避免将无效的引用带到下一帧。
            // 这些可能是上一帧的 BlitData 纹理句柄，现在已经无效。
            m_TextureHandleFront = TextureHandle.nullHandle;
            m_TextureHandleBack = TextureHandle.nullHandle;
            texture = TextureHandle.nullHandle;
            // 重置活动纹理为前缓冲。
            m_IsFront = true;
        }

        // 用于向渲染函数传递数据的类。
        class PassData
        {
            // 进行 Blit 操作时需要一个源纹理、一个目标纹理以及一个材质。
            // 源纹理与目标纹理用于指定从哪复制到哪。
            public TextureHandle source;
            public TextureHandle destination;
            // 材质用于在复制颜色缓冲时进行变换。
            public Material material;
        }

        // 该函数不接收材质参数，目的是展示我们应该重置未使用的值，
        // 以避免遗留上一帧的状态。
        public void RecordBlitColor(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 检查 BlitData 的纹理是否有效，如果无效则进行初始化。
            if (!texture.IsValid())
            {
                // 设置 BlitData 使用的描述符，应以相机目标的描述符为起点。
                var cameraData = frameData.Get<UniversalCameraData>();
                var descriptor = cameraData.cameraTargetDescriptor;
                // 禁用 MSAA（多重采样抗锯齿），因为 Blit 操作不需要它。
                descriptor.msaaSamples = 1;
                // 禁用深度缓冲，因为我们只对颜色缓冲做变换。
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                Init(renderGraph, descriptor);
            }

            // 开始记录 Render Graph Pass，给定 Pass 名称
            // 并输出用于渲染函数执行时的数据。
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlitColorPass", out var passData))
            {
                // 从 frameData 获取 UniversalResourceData，以检索相机的活动颜色附件。
                var resourceData = frameData.Get<UniversalResourceData>();

                // 重置材质，否则会使用上一帧的材质。
                passData.material = null;
                passData.source = resourceData.activeColorTexture;
                passData.destination = texture;

                // 将输入附件设置为相机颜色缓冲区。
                builder.UseTexture(passData.source);
                // 将输出附件 0 设置为 BlitData 的活动纹理。
                builder.SetRenderAttachment(passData.destination, 0);

                // 设置渲染函数。
                builder.SetRenderFunc((PassData passData, RasterGraphContext rgContext) => ExecutePass(passData, rgContext));
            }
        }

        // 记录一个 Render Graph Pass，将 BlitData 的活动纹理 Blit 回相机的颜色附件。
        public void RecordBlitBackToColor(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 检查 BlitData 的纹理是否有效，如果无效说明未初始化或发生了错误。
            if (!texture.IsValid()) return;

            // 开始记录 Render Graph Pass，给定 Pass 名称
            // 并输出用于渲染函数执行时的数据。
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"BlitBackToColorPass", out var passData))
            {
                // 从 frameData 获取 UniversalResourceData，以检索相机的活动颜色附件。
                var resourceData = frameData.Get<UniversalResourceData>();

                // 重置材质，否则会使用上一帧的材质。
                passData.material = null;
                passData.source = texture;
                passData.destination = resourceData.activeColorTexture;

                // 将输入附件设置为 BlitData 的活动纹理。
                builder.UseTexture(passData.source);
                // 将输出附件 0 设置为相机颜色缓冲区。
                builder.SetRenderAttachment(passData.destination, 0);

                // 设置渲染函数。
                builder.SetRenderFunc((PassData passData, RasterGraphContext rgContext) => ExecutePass(passData, rgContext));
            }
        }

        // 此函数使用指定的材质执行全屏 Blit。
        public void RecordFullScreenPass(RenderGraph renderGraph, string passName, Material material)
        {
            // 检查数据是否已初始化以及材质是否有效。
            if (!texture.IsValid() || material == null)
            {
                Debug.LogWarning("输入的纹理句柄无效，将跳过全屏 Pass。");
                return;
            }

            // 开始记录 Render Graph Pass，给定 Pass 名称
            // 并输出用于渲染函数执行时的数据。
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                // 切换活动纹理句柄，避免重复 Blit。
                // 如果我们需要最近的纹理，可以直接使用 texture 变量。
                m_IsFront = !m_IsFront;

                // 设置渲染函数执行时的数据。
                passData.material = material;
                passData.source = texture;

                // 交换活动纹理。
                if (m_IsFront)
                    passData.destination = m_TextureHandleFront;
                else
                    passData.destination = m_TextureHandleBack;

                // 将输入附件设置为 BlitData 的旧活动纹理。
                builder.UseTexture(passData.source);
                // 将输出附件 0 设置为 BlitData 的新活动纹理。
                builder.SetRenderAttachment(passData.destination, 0);

                // 更新活动纹理。
                texture = passData.destination;

                // 设置渲染函数。
                builder.SetRenderFunc((PassData passData, RasterGraphContext rgContext) => ExecutePass(passData, rgContext));
            }
        }

        // ExecutePass 是每个 Blit Render Graph Pass 的渲染函数。
        // 最佳实践是避免在 lambda 外部使用变量。
        // 将其设置为 static 以避免使用可能导致意外行为的成员变量。
        static void ExecutePass(PassData data, RasterGraphContext rgContext)
        {
            // 我们可以使用带材质或不带材质的 Blit 操作，
            // 两种情况都使用静态的 scaleBias 以避免重复分配。
            if (data.material == null)
                Blitter.BlitTexture(rgContext.cmd, data.source, scaleBias, 0, false);
            else
                Blitter.BlitTexture(rgContext.cmd, data.source, scaleBias, data.material, 0);
        }

        // 需要在渲染器释放时释放纹理，这会销毁 frameData 内的所有资源
        // （包括前几帧创建的数据类型）。
        public void Dispose()
        {
            m_TextureFront?.Release();
            m_TextureBack?.Release();
        }
    }

    // 渲染器功能的初始渲染 Pass，
    // 用于在 frameData 中初始化数据，并将相机颜色附件复制到 BlitData 内部的纹理中，
    // 以便后续使用 Blit 进行变换。
    class BlitStartRenderPass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 在 frameData 中创建 BlitData。
            var blitTextureData = frameData.Create<BlitData>();
            // 将相机颜色附件复制到 BlitData 内部的纹理。
            blitTextureData.RecordBlitColor(renderGraph, frameData);
        }
    }

    // 用于对渲染器功能提供的每个材质进行 Blit 的渲染 Pass。
    class BlitRenderPass : ScriptableRenderPass
    {
        List<Material> m_Materials;

        // 设置函数，用于从渲染器功能中获取材质。
        public void Setup(List<Material> materials)
        {
            m_Materials = materials;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 从当前帧获取 BlitData。
            var blitTextureData = frameData.Get<BlitData>();
            foreach (var material in m_Materials)
            {
                // 如果材质为空则跳过当前循环，因为没有材质意味着不需要 Blit。
                if (material == null) continue;
                // 记录材质的 Blit Pass。
                blitTextureData.RecordFullScreenPass(renderGraph, $"Blit {material.name} Pass", material);
            }
        }
    }

    // 将纹理 Blit 回相机颜色附件的最终渲染 Pass。
    class BlitEndRenderPass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 从当前帧获取 BlitData 并将其 Blit 回相机颜色附件。
            var blitTextureData = frameData.Get<BlitData>();
            blitTextureData.RecordBlitBackToColor(renderGraph, frameData);
        }
    }

    [SerializeField]
    [Tooltip("用于 Blit 的材质。它们将按照列表中的顺序进行 Blit，从索引 0 开始。")]
    List<Material> m_Materials;

    BlitStartRenderPass m_StartPass;
    BlitRenderPass m_BlitPass;
    BlitEndRenderPass m_EndPass;

    // 在此处可以创建 Pass 并进行初始化。此方法会在每次序列化时调用。
    public override void Create()
    {
        m_StartPass = new BlitStartRenderPass();
        m_BlitPass = new BlitRenderPass();
        m_EndPass = new BlitEndRenderPass();

        // 配置渲染 Pass 的注入位置。
        m_StartPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        m_BlitPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        m_EndPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    // 在此处向渲染器中注入一个或多个渲染 Pass。
    // 该方法会在为每个相机设置渲染器时调用一次。
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 如果没有可 Blit 的纹理则直接返回。
        if (m_Materials == null || m_Materials.Count == 0) return;

        // 将材质传递给 Blit 渲染 Pass。
        m_BlitPass.Setup(m_Materials);

        // 由于它们具有相同的 RenderPassEvent，入队顺序非常重要。
        renderer.EnqueuePass(m_StartPass);
        renderer.EnqueuePass(m_BlitPass);
        renderer.EnqueuePass(m_EndPass);
    }
}
