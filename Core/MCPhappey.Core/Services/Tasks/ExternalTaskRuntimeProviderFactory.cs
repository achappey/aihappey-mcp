using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Core.Services.Tasks;

public static class ExternalTaskRuntimeProviderFactory
{
    public static IExternalTaskRuntimeProvider Create(
        IServiceProvider serviceProvider,
        ExternalTaskRuntimeContext runtimeContext)
    {
        var configuredType = runtimeContext.Options.Provider;
        if (string.IsNullOrWhiteSpace(configuredType))
        {
            throw new InvalidOperationException("Task runtime requires a provider type name in server.tasks.provider.");
        }

        var providerType = Type.GetType(configuredType, throwOnError: false, ignoreCase: false);
        if (providerType == null)
        {
            throw new InvalidOperationException($"Could not resolve external task provider type '{configuredType}'.");
        }

        if (!typeof(IExternalTaskRuntimeProvider).IsAssignableFrom(providerType))
        {
            throw new InvalidOperationException(
                $"Configured provider '{configuredType}' does not implement {nameof(IExternalTaskRuntimeProvider)}.");
        }

        var provider = (IExternalTaskRuntimeProvider)ActivatorUtilities.CreateInstance(serviceProvider, providerType);
        return provider;
    }
}

