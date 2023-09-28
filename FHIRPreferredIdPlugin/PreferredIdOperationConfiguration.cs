using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vonk.Core.Context;
using Vonk.Core.Metadata;
using Vonk.Core.Pluggability;
using Vonk.Core.Pluggability.ContextAware;
using Vonk.Core.Repository;

namespace Vonk.Plugin.PreferredIdOperation
{
    [VonkConfiguration(order: 10001)]
    public static class PreferredIdOperationConfiguration
    {
        public static IServiceCollection ConfigureServices(this IServiceCollection services) 
        {
            services.TryAddScoped<IdentificationService>();
            return services;
        }

        public static IApplicationBuilder Configure(this IApplicationBuilder builder)
        {
            //Register interactions
            builder
                .OnCustomInteraction(VonkInteraction.type_custom, "preferred-id")
                .AndMethod("GET")
                .HandleAsyncWith<IdentificationService>(
                    (svc, context) => svc.PreferredIdGET(context)
                );

            return builder;
        }
    }
}
