using Microsoft.AspNetCore.ResponseCaching;

namespace CoffeePeek.Gateway.Extensions;

public static class QueryAwareResponseCachingExtensions
{
    public static IApplicationBuilder UseQueryAwareResponseCaching(this IApplicationBuilder app)
    {
        app.UseResponseCaching();
        return app.Use((context, next) =>
        {
            var feature = context.Features.Get<IResponseCachingFeature>();
            if (feature is not null)
                feature.VaryByQueryKeys = ["*"];
            return next(context);
        });
    }
}
