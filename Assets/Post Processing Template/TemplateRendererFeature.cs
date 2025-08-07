using UnityEngine;                            // 引用Unity引擎核心命名空间
using UnityEngine.Rendering;                  // 引用Unity渲染管线相关命名空间
using UnityEngine.Rendering.RenderGraphModule;// 引用RenderGraph模块命名空间（URP中的新渲染框架）
using UnityEngine.Rendering.Universal;       // 引用Universal Render Pipeline命名空间（URP）

// 这是用于创建后处理效果的ScriptableRendererFeature模板脚本
public sealed class TemplateRendererFeature : ScriptableRendererFeature
{
    #region FEATURE_FIELDS

    [SerializeField]                           // 序列化字段，使其可保存及包含在构建中
    [HideInInspector]                         // 在编辑器Inspector中隐藏该字段
    private Material m_Material;               // 后处理效果使用的材质

    private CustomPostRenderPass m_FullScreenPass; // 负责渲染后处理效果的自定义RenderPass实例

    #endregion

    #region FEATURE_METHODS

    public override void Create()
    {
#if UNITY_EDITOR                            // 仅在编辑器模式下执行
        if (m_Material == null)               // 如果材质未赋值
            // 通过路径加载Unity内置的反色材质（编辑器内操作）
            m_Material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/FullscreenInvertColors.mat");
#endif

        if (m_Material)                        // 若材质存在，则创建自定义渲染通道实例
            m_FullScreenPass = new CustomPostRenderPass(name, m_Material);
    }

    // 每个摄像机设置渲染器时调用，注入渲染通道
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Material == null || m_FullScreenPass == null)  // 材质或渲染通道为空则跳过
            return;

        // 跳过预览摄像机和反射摄像机，不对其做后处理
        if (renderingData.cameraData.cameraType == CameraType.Preview
            || renderingData.cameraData.cameraType == CameraType.Reflection)
            return;

        // 通过Volume系统查询自定义VolumeComponent，控制后处理开启状态
        TemplateVolumeComponent myVolume = VolumeManager.instance.stack?.GetComponent<TemplateVolumeComponent>();
        if (myVolume == null || !myVolume.IsActive())  // 未启用则跳过
            return;

        // 设置该渲染通道在渲染流程中的注入时机，后处理执行后
        m_FullScreenPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        // 告诉渲染通道不需要额外输入纹理（深度、法线等），也可以配置需要的输入
        m_FullScreenPass.ConfigureInput(ScriptableRenderPassInput.None);

        // 将自定义渲染通道加入执行队列
        renderer.EnqueuePass(m_FullScreenPass);
    }

    // 释放资源，析构时调用
    protected override void Dispose(bool disposing)
    {
        m_FullScreenPass.Dispose();
    }

    #endregion

    // 自定义后处理渲染通道类
    private class CustomPostRenderPass : ScriptableRenderPass
    {
        #region PASS_FIELDS

        private Material m_Material;               // 渲染材质

        private RTHandle m_CopiedColor;            // 临时渲染纹理句柄（非RenderGraph路径用）

        private static MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();  // 共享属性块，减少GC

        private static readonly bool kSampleActiveColor = true;    // 是否采样活动颜色纹理

        private static readonly bool kBindDepthStencilAttachment = false;  // 是否绑定深度模板缓冲

        private static readonly int kBlitTexturePropertyId = Shader.PropertyToID("_BlitTexture");  // 材质属性ID：采样纹理
        private static readonly int kBlitScaleBiasPropertyId = Shader.PropertyToID("_BlitScaleBias");// 材质属性ID：缩放偏移

        #endregion

        // 构造函数，传入通道名称和材质
        public CustomPostRenderPass(string passName, Material material)
        {
            profilingSampler = new ProfilingSampler(passName);    // 创建性能采样器
            m_Material = material;                                 // 保存材质

            // 需要采样活动颜色时，要求使用中间纹理
            requiresIntermediateTexture = kSampleActiveColor;
        }

        #region PASS_SHARED_RENDERING_CODE

        // 非RenderGraph路径：执行颜色拷贝操作
        private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sourceTexture)
        {
            Blitter.BlitTexture(cmd, sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);  // 直接拷贝
        }

        // 非RenderGraph路径：执行后处理主渲染操作
        private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle sourceTexture, Material material)
        {
            s_SharedPropertyBlock.Clear();                         // 清理属性块

            if (sourceTexture != null)                              // 如果有输入纹理
                s_SharedPropertyBlock.SetTexture(kBlitTexturePropertyId, sourceTexture); // 绑定纹理到材质属性

            // 设置缩放偏移，通常固定为(1,1,0,0)
            s_SharedPropertyBlock.SetVector(kBlitScaleBiasPropertyId, new Vector4(1, 1, 0, 0));

            // 从Volume系统读取自定义参数传给材质
            TemplateVolumeComponent myVolume = VolumeManager.instance.stack?.GetComponent<TemplateVolumeComponent>();
            if (myVolume != null)
                s_SharedPropertyBlock.SetFloat("_Intensity", myVolume.intensity.value);  // 设置强度参数

            // 绘制三角形面片，触发全屏渲染
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
        }

        // 获取拷贝纹理的描述符
        private static RenderTextureDescriptor GetCopyPassTextureDescriptor(RenderTextureDescriptor desc)
        {
            desc.msaaSamples = 1;                   // 禁用MSAA采样，设置为单样本
            desc.depthBufferBits = (int)DepthBits.None;   // 不需要深度缓冲区
            return desc;
        }

        #endregion

        #region PASS_NON_RENDER_GRAPH_PATH

        // 非RenderGraph路径：设置摄像机渲染目标，分配临时纹理
        [System.Obsolete("该路径为兼容旧版，推荐使用RenderGraph API。", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ResetTarget();   // 重置渲染目标，避免与默认目标冲突

            if (kSampleActiveColor)  // 若需要采样颜色纹理，分配或重新分配临时纹理
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_CopiedColor,
                    GetCopyPassTextureDescriptor(renderingData.cameraData.cameraTargetDescriptor),
                    name: "_CustomPostPassCopyColor");
        }

        // 非RenderGraph路径：执行渲染
        [System.Obsolete("该路径为兼容旧版，推荐使用RenderGraph API。", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;    // 获取摄像机数据
            var cmd = CommandBufferPool.Get();                    // 申请命令缓冲区

            using (new ProfilingScope(cmd, profilingSampler))    // 性能采样开始
            {
                RasterCommandBuffer rasterCmd = CommandBufferHelpers.GetRasterCommandBuffer(cmd);  // 转换为RasterCommandBuffer

                if (kSampleActiveColor)                           // 拷贝活动颜色纹理
                {
                    CoreUtils.SetRenderTarget(cmd, m_CopiedColor);
                    ExecuteCopyColorPass(rasterCmd, cameraData.renderer.cameraColorTargetHandle);
                }

                if (kBindDepthStencilAttachment)                  // 绑定深度模板缓冲（若开启）
                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle, cameraData.renderer.cameraDepthTargetHandle);
                else
                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle); // 只绑定颜色缓冲

                ExecuteMainPass(rasterCmd, kSampleActiveColor ? m_CopiedColor : null, m_Material);  // 执行后处理主渲染
            }

            context.ExecuteCommandBuffer(cmd);    // 执行命令缓冲
            cmd.Clear();                          // 清理命令缓冲
            CommandBufferPool.Release(cmd);      // 释放命令缓冲区
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // 非RenderGraph路径渲染后清理（这里无操作）
        }

        public void Dispose()
        {
            m_CopiedColor?.Release();   // 释放临时纹理
        }

        #endregion

        #region PASS_RENDER_GRAPH_PATH

        // RenderGraph路径：拷贝通道数据结构
        private class CopyPassData
        {
            public TextureHandle inputTexture;   // 输入纹理句柄
        }

        // RenderGraph路径：主渲染通道数据结构
        private class MainPassData
        {
            public Material material;            // 材质
            public TextureHandle inputTexture;  // 输入纹理句柄
        }

        // RenderGraph路径：执行拷贝通道操作
        private static void ExecuteCopyColorPass(CopyPassData data, RasterGraphContext context)
        {
            ExecuteCopyColorPass(context.cmd, data.inputTexture);
        }

        // RenderGraph路径：执行主后处理渲染
        private static void ExecuteMainPass(MainPassData data, RasterGraphContext context)
        {
            ExecuteMainPass(context.cmd, data.inputTexture.IsValid() ? data.inputTexture : null, data.material);
        }

        // RenderGraph路径：记录渲染通道
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();  // 获取资源数据
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();        // 获取摄像机数据

            // 添加渲染通道，绑定性能采样器
            using (var builder = renderGraph.AddRasterRenderPass<MainPassData>(passName, out var passData, profilingSampler))
            {
                passData.material = m_Material;          // 绑定材质

                TextureHandle destination;                // 目标纹理句柄

                // GPU不能同时采样和写入同一纹理
                // 通过交换颜色纹理引用避免额外拷贝，要求材质写满所有像素，否则需拷贝
                if (kSampleActiveColor)
                {
                    var cameraColorDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);  // 获取颜色纹理描述
                    cameraColorDesc.name = "_CameraColorCustomPostProcessing";                   // 命名
                    cameraColorDesc.clearBuffer = false;                                        // 不清理缓冲

                    destination = renderGraph.CreateTexture(cameraColorDesc);                    // 创建临时纹理
                    passData.inputTexture = resourcesData.cameraColor;                          // 输入原始颜色纹理

                    builder.UseTexture(passData.inputTexture, AccessFlags.Read);                // 声明读取依赖纹理
                }
                else
                {
                    destination = resourcesData.cameraColor;                                   // 直接使用当前颜色纹理
                    passData.inputTexture = TextureHandle.nullHandle;                          // 空句柄
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);               // 设置渲染目标为 destination，颜色附着点 0

                if (kBindDepthStencilAttachment)                                              // 绑定深度模板附着点（如果启用）
                    builder.SetRenderAttachmentDepth(resourcesData.activeDepthTexture, AccessFlags.Write);

                builder.SetRenderFunc((MainPassData data, RasterGraphContext context) => ExecuteMainPass(data, context));  // 设置渲染回调

                if (kSampleActiveColor)                                                        // 交换颜色纹理引用，供后续通道使用
                {
                    resourcesData.cameraColor = destination;
                }
            }
        }

        #endregion
    }
}
