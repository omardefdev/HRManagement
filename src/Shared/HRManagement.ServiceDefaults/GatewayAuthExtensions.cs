using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace HRManagement.ServiceDefaults;

public static class GatewayAuthExtensions
{
    public static IReverseProxyBuilder AddGatewayTokenForwarding(this IReverseProxyBuilder builder)
    {
        builder.AddTransforms<GatewayAccessTokenTransformProvider>();
        return builder;
    }
}
