using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace CoffeePeek.Gateway.Extensions;

/// <summary>
/// YARP transform provider that strips incoming <see cref="GatewayHeaderConsts"/> headers
/// (to prevent client impersonation) and then re-adds them from the validated JWT claims.
/// Applied globally to every proxied route.
/// </summary>
public class ClaimsToHeadersTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }

    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            var requestHeaders = transformContext.ProxyRequest.Headers;
            requestHeaders.Remove(GatewayHeaderConsts.XUserId);
            requestHeaders.Remove(GatewayHeaderConsts.XUserName);
            requestHeaders.Remove(GatewayHeaderConsts.XUserRole);
            requestHeaders.Remove(GatewayHeaderConsts.XUserEmail);
            requestHeaders.Remove(GatewayHeaderConsts.XUserTokenExpiresAt);
            requestHeaders.Remove(GatewayHeaderConsts.XGatewayAuth);

            var gatewayAuth = transformContext.HttpContext.RequestServices
                .GetRequiredService<IOptions<GatewayAuthOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(gatewayAuth.SecretKey))
                requestHeaders.TryAddWithoutValidation(GatewayHeaderConsts.XGatewayAuth, gatewayAuth.SecretKey);

            var claimsHeaders = ClaimsTransformationExtensions.ExtractClaimsAsHeaders(transformContext.HttpContext.User);
            foreach (var (key, value) in claimsHeaders)
                requestHeaders.TryAddWithoutValidation(key, value);

            return ValueTask.CompletedTask;
        });
    }
}
