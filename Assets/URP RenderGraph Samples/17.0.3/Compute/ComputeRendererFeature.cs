using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


// 此 RendererFeature 演示了如何将 Compute Shader 与 RenderGraph 一起使用。

// 本示例没有展示的一点是，它可以和渲染 Pass 一起运行。
// 如果 Compute Shader 使用了渲染 Pass 也会用到的资源，那么这两个 Pass 之间会像普通渲染 Pass 一样自动建立依赖关系。
public class ComputeRendererFeature : ScriptableRendererFeature
{
    // 我们会将 Compute Pass 当作普通的 ScriptableRenderPass 处理。
    class ComputePass : ScriptableRenderPass
    {
        // Compute Shader。
        ComputeShader cs;

        // Compute 缓冲区：
        GraphicsBuffer inputBuffer;
        GraphicsBuffer outputBuffer;

        // 输出数据的缓存。使用预分配的数组来避免每帧的内存分配。
        int[] outputData = new int[20];

        // 构造函数用于初始化 Compute 缓冲区。
        public ComputePass()
        {
            BufferDesc desc = new BufferDesc(20, sizeof(int));
            inputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 20, sizeof(int));
            var list = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                list.Add(i);
            }
            inputBuffer.SetData(list);
            outputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 20, sizeof(int));
            // 通常我们不需要初始化输出缓冲区，
            // 但在这里我们会在每帧开始时读取上帧的结果以进行调试。
            outputBuffer.SetData(list);
        }

        // Setup 方法用于将 Compute Shader 从 RendererFeature 传递到渲染 Pass。
        public void Setup(ComputeShader cs)
        {
            this.cs = cs;
        }

        // PassData 用于在录制时向执行阶段传递数据。
        class PassData
        {
            // Compute Shader。
            public ComputeShader cs;
            // Compute 缓冲区的 Buffer 句柄。
            public BufferHandle input;
            public BufferHandle output;
        }

        // 记录一个 RenderGraph Pass，将 Compute 结果写回（示例中描述为将 BlitData 的活动纹理写回到相机的颜色附件）。
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 读取上一帧的数据（如果存在）。
            outputBuffer.GetData(outputData);
            Debug.Log($"Compute Shader 输出结果: {string.Join(", ", outputData)}");

            // 如果缓冲区是在 RenderGraph 外部创建的，则需要导入它们。
            BufferHandle inputHandle = renderGraph.ImportBuffer(inputBuffer);
            BufferHandle outputHandle = renderGraph.ImportBuffer(outputBuffer);

            // 开始录制 RenderGraph Pass，指定 Pass 名称，
            // 并生成用于在执行阶段传递数据的 PassData。
            // 注意：在处理 Compute Pass 时，我们需要使用 "AddComputePass"。
            using (var builder = renderGraph.AddComputePass("ComputePass", out PassData passData))
            {
                // 设置 PassData，用于将录制阶段的数据传递到执行阶段。
                passData.cs = cs;
                passData.input = inputHandle;
                passData.output = outputHandle;

                // UseBuffer 用于设置 RenderGraph 的依赖关系，并指定读写标记。
                builder.UseBuffer(passData.input);
                builder.UseBuffer(passData.output, AccessFlags.Write);
                // 对于 Compute Pass，执行函数同样通过 SetRenderFunc 设置。
                builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
            }
        }

        // ExecutePass 是 RenderGraph Pass 的执行函数。
        // 这种做法可以避免使用 Lambda 之外的变量。
        // 将其设为 static 是为了避免使用成员变量导致的意外行为。
        static void ExecutePass(PassData data, ComputeGraphContext cgContext)
        {
            // 绑定 Compute 缓冲区。
            cgContext.cmd.SetComputeBufferParam(data.cs, data.cs.FindKernel("CSMain"), "inputData", data.input);
            cgContext.cmd.SetComputeBufferParam(data.cs, data.cs.FindKernel("CSMain"), "outputData", data.output);
            // 调度 Compute Shader，指定要执行的内核。
            // 线程组的数量决定了执行该内核的次数。
            cgContext.cmd.DispatchCompute(data.cs, data.cs.FindKernel("CSMain"), 1, 1, 1);
        }
    }

    [SerializeField]
    ComputeShader computeShader;

    ComputePass m_ComputePass;

    /// <inheritdoc/>
    public override void Create()
    {
        // 初始化 Compute Pass。
        m_ComputePass = new ComputePass();
        // 设置 RendererFeature 在渲染之前执行。
        m_ComputePass.renderPassEvent = RenderPassEvent.BeforeRendering;
    }

    // 你可以在此方法中向渲染器注入一个或多个 Render Pass。
    // 该方法会在每个摄像机设置渲染器时调用一次。
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 检查系统是否支持 Compute Shader，如果不支持则提前退出。
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogWarning("设备不支持 Compute Shader，此 Pass 将被跳过。");
            return;
        }
        // 如果 Compute Shader 为空，则跳过此 Pass。
        if (computeShader == null)
        {
            Debug.LogWarning("Compute Shader 为空，此 Pass 将被跳过。");
            return;
        }
        // 调用 Setup 方法，将 Compute Shader 传递到渲染 Pass。
        m_ComputePass.Setup(computeShader);
        // 将 Compute Pass 入队。
        renderer.EnqueuePass(m_ComputePass);
    }
}
