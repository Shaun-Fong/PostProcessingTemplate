using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 这个示例中，我们将活跃的颜色纹理复制到一个新纹理，然后对源纹理进行两次降采样。
// 该示例主要演示 API 用法，生成的新纹理在本帧中未被其他地方使用，你可以通过帧调试器验证它们的内容。
// 本示例的核心概念是 UnsafePass 的使用：此类 Pass 是不安全的，允许使用如 SetRenderTarget() 的命令，
// 这些命令不兼容普通的 RasterRenderPass。
// 使用 UnsafePass 意味着 RenderGraph 不会试图通过将其合并到 NativeRenderPass 中来优化该 Pass。
// 在某些情况下，使用 UnsafePass 是合理的，例如当我们知道一组相邻的 Pass 不能合并时，
// 这样可以优化 RenderGraph 的编译时间，并简化多个 Pass 的设置。
public class UnsafePassRenderFeature : ScriptableRendererFeature
{
    class UnsafePass : ScriptableRenderPass
    {
        // 该类保存 Pass 执行所需的数据，通过委托传入执行函数
        private class PassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
            internal TextureHandle destinationHalf;
            internal TextureHandle destinationQuarter;
        }

        // 静态执行函数，作为 RenderGraph 渲染 Pass 的 RenderFunc 委托传入
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            // 手动设置每次 blit 的 RenderTarget。
            // 每次 SetRenderTarget 调用都需要单独的 RasterCommandPass，如果想合并 Pass 就会复杂化。
            // 这里知道这 3 个子 Pass 不能合并（因为目标纹理尺寸不同），
            // 所以用 UnsafePass 简化代码，也节省 RenderGraph 处理时间。

            // 拷贝当前场景颜色
            CommandBuffer unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            context.cmd.SetRenderTarget(data.destination);
            Blitter.BlitTexture(unsafeCmd, data.source, new Vector4(1, 1, 0, 0), 0, false);

            // 降采样 2 倍
            context.cmd.SetRenderTarget(data.destinationHalf);
            Blitter.BlitTexture(unsafeCmd, data.destination, new Vector4(1, 1, 0, 0), 0, false);

            context.cmd.SetRenderTarget(data.destinationQuarter);
            Blitter.BlitTexture(unsafeCmd, data.destinationHalf, new Vector4(1, 1, 0, 0), 0, false);

            // 放大 2 倍
            context.cmd.SetRenderTarget(data.destinationHalf);
            Blitter.BlitTexture(unsafeCmd, data.destinationQuarter, new Vector4(1, 1, 0, 0), 0, false);

            context.cmd.SetRenderTarget(data.destination);
            Blitter.BlitTexture(unsafeCmd, data.destinationHalf, new Vector4(1, 1, 0, 0), 0, false);
        }

        // 这里可以访问 RenderGraph 句柄。
        // 每个 ScriptableRenderPass 可以用 RenderGraph 句柄向 RenderGraph 添加多个渲染 Pass。
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            string passName = "Unsafe Pass";

            // 添加一个 Raster Render Pass，指定名称和执行函数使用的数据类型
            using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData))
            {
                // UniversalResourceData 包含渲染器使用的所有纹理句柄，包括活跃的颜色和深度纹理
                // 活跃的颜色和深度纹理是摄像机渲染的主要颜色和深度缓冲区
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // 填充 Pass 所需数据

                // 获取当前活跃的颜色纹理，作为 blit 的源纹理
                passData.source = resourceData.activeColorTexture;

                // 创建目标纹理，尺寸同活跃颜色纹理，关闭深度缓冲和多重采样（MSAA）
                var descriptor = passData.source.GetDescriptor(renderGraph);
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.clearBuffer = false;

                // 创建新的临时纹理用于存储 blit 结果
                descriptor.name = "UnsafeTexture";
                var destination = renderGraph.CreateTexture(descriptor);

                // 目标纹理尺寸减半
                descriptor.width /= 2;
                descriptor.height /= 2;
                descriptor.name = "UnsafeTexture2";
                var destinationHalf = renderGraph.CreateTexture(descriptor);

                // 目标纹理尺寸再次减半（四分之一尺寸）
                descriptor.width /= 2;
                descriptor.height /= 2;
                descriptor.name = "UnsafeTexture3";
                var destinationQuarter = renderGraph.CreateTexture(descriptor);

                passData.destination = destination;
                passData.destinationHalf = destinationHalf;
                passData.destinationQuarter = destinationQuarter;

                // 声明源纹理为该 Pass 的输入依赖
                builder.UseTexture(passData.source);

                // UnsafePass 不使用 UseTextureFragment/UseTextureFragmentDepth 来设置输出，应通过 UseTexture 指定写入纹理
                builder.UseTexture(passData.destination, AccessFlags.WriteAll);
                builder.UseTexture(passData.destinationHalf, AccessFlags.WriteAll);
                builder.UseTexture(passData.destinationQuarter, AccessFlags.WriteAll);

                // 关闭剔除，此 Pass 出于演示目的不被剔除（因为生成的纹理没有被其他 Pass 使用）
                builder.AllowPassCulling(false);

                // 设置执行函数
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
    }

    UnsafePass m_UnsafePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_UnsafePass = new UnsafePass();

        // 配置渲染通道插入时机
        m_UnsafePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    // 这里可以注入一个或多个渲染通道，每个摄像机调用一次
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_UnsafePass);
    }
}
