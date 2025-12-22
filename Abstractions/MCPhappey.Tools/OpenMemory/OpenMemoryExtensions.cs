using MCPhappey.Auth.Extensions;
using Microsoft.KernelMemory;

namespace MCPhappey.Tools.OpenMemory;

public static class OpenMemoryExtensions
{
    public const string MemoryPurpose = "Memory";

    public static TagCollection ToTagCollection(this IServiceProvider serviceProvider) => new()
    {
            { MemoryPurpose, serviceProvider.GetUserId() }
    };

    public static MemoryFilter ToMemoryFilter(this IServiceProvider serviceProvider) => new()
    {
            { MemoryPurpose, serviceProvider.GetUserId() }
    };

}

