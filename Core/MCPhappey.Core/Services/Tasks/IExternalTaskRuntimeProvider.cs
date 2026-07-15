using ModelContextProtocol.Protocol;
using ModelContextProtocol.Extensions.Tasks;

namespace MCPhappey.Core.Services.Tasks;

#pragma warning disable MCPEXP001
public interface IExternalTaskRuntimeProvider
{
    IMcpTaskStore CreateTaskStore(ExternalTaskRuntimeContext runtimeContext);

    void TryMutateInitializeResult(JsonRpcResponse response, ExternalTaskRuntimeContext runtimeContext);
}
#pragma warning restore MCPEXP001

