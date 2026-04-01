using Lab.Shared.Contracts;
using Microsoft.AspNetCore.Builder;

namespace Lab.Telemetry.AspNetCore;

public static class EndpointConventionBuilderExtensions
{
    public static TBuilder WithOperationContract<TBuilder>(
        this TBuilder builder,
        OperationContractDescriptor contract)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(contract);

        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(new OperationContractMetadata(contract)));
        return builder;
    }
}
