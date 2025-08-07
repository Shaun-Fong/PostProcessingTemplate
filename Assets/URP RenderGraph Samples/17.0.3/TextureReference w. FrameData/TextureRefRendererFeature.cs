using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule.Util;

// 在这个示例中，我们将在 frameData 中创建一个纹理引用 ContextItem，用于存储后续 Pass 使用的引用。
// 这样做可以避免额外的来回拷贝（blit）操作到摄像机的颜色附件。
// 不是在 blit 操作后将结果拷贝回去，而是直接更新引用指向新的 blit 目标，用于后续的 Pass。
// 这是在 Pass 之间共享资源的推荐方式。过去常用全局纹理实现此目的，但尽量避免使用全局纹理更好。
public class TextureRefRendererFeature : ScriptableRendererFeature
{
    // 用于存储纹理引用的 ContextItem
    public class TexRefData : ContextItem
    {
        // 纹理引用变量
        public TextureHandle texture = TextureHandle.nullHandle;

        // ContextItem 要求实现的 Reset 函数，用于重置所有不会被带入下一帧的变量
        public override void Reset()
        {
            // 纹理句柄仅在当前帧有效，需重置
            texture = TextureHandle.nullHandle;
        }
    }

    // 该 Pass 在使用材质和摄像机颜色附件做 blit 操作时更新纹理引用
    class UpdateRefPass : ScriptableRenderPass
    {
        // 需要传递给渲染函数的数据
        class PassData
        {
            public TextureHandle source;      // blit 的源纹理
            public TextureHandle destination; // blit 的目标纹理
            public Material material;         // blit 使用的材质
        }

        // 用于从源纹理 blit 到目标纹理时的缩放和偏移（x,y为缩放，z,w为偏移）
        static Vector4 scaleBias = new Vector4(1f, 1f, 0f, 0f);

        Material[] m_DisplayMaterials; // 用于 blit 操作的材质数组

        // 将材质数组从 RendererFeature 传递到该 Pass
        public void Setup(Material[] materials)
        {
            m_DisplayMaterials = materials;
        }

        // 录制 RenderGraph 的 RenderPass，执行 blit 操作
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            foreach (var mat in m_DisplayMaterials)
            {
                if (mat == null)
                {
                    Debug.LogWarning($"跳过未分配的材质的渲染通道。");
                    continue;
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"UpdateRefPass_{mat.name}", out var passData))
                {
                    var texRefExist = frameData.Contains<TexRefData>();
                    var texRef = frameData.GetOrCreate<TexRefData>();

                    // 第一次运行时，从活动颜色缓冲获取引用
                    if (!texRefExist)
                    {
                        var resourceData = frameData.Get<UniversalResourceData>();
                        texRef.texture = resourceData.activeColorTexture;
                    }

                    passData.source = texRef.texture;

                    var descriptor = passData.source.GetDescriptor(renderGraph);
                    descriptor.msaaSamples = MSAASamples.None; // 禁用 MSAA
                    descriptor.name = $"BlitMaterialRefTex_{mat.name}";
                    descriptor.clearBuffer = false;

                    passData.destination = renderGraph.CreateTexture(descriptor);
                    passData.material = mat;

                    // 更新纹理引用指向新的 blit 目标
                    texRef.texture = passData.destination;

                    builder.UseTexture(passData.source);
                    builder.SetRenderAttachment(passData.destination, 0);

                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                }
            }
        }

        // blit 的具体执行函数
        static void ExecutePass(PassData data, RasterGraphContext rgContext)
        {
            Blitter.BlitTexture(rgContext.cmd, data.source, scaleBias, data.material, 0);
        }
    }

    // 更新引用后，需要把结果拷贝回摄像机颜色附件
    class CopyBackRefPass : ScriptableRenderPass
    {
        // 该函数将纹理引用 blit 回摄像机颜色附件
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<TexRefData>()) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var texRef = frameData.Get<TexRefData>();

            renderGraph.AddBlitPass(texRef.texture, resourceData.activeColorTexture, Vector2.one, Vector2.zero, passName: "Blit Back Pass");
        }
    }

    [Tooltip("执行 blit 操作时使用的材质数组。")]
    public Material[] displayMaterials = new Material[1];

    UpdateRefPass m_UpdateRefPass;
    CopyBackRefPass m_CopyBackRefPass;

    // 初始化 Pass，序列化时会调用
    public override void Create()
    {
        m_UpdateRefPass = new UpdateRefPass();
        m_CopyBackRefPass = new CopyBackRefPass();

        m_UpdateRefPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        m_CopyBackRefPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    // 在渲染器中注入渲染通道，单摄像机调用一次
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (displayMaterials == null)
        {
            Debug.LogWarning("TextureRefRendererFeature 材质数组为空，跳过该特性。");
            return;
        }

        m_UpdateRefPass.Setup(displayMaterials);
        renderer.EnqueuePass(m_UpdateRefPass);
        renderer.EnqueuePass(m_CopyBackRefPass);
    }
}
