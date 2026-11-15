using DataControls.Application.Ai;
using DataControls.Application.Controls;
using DataControls.Application.Describing;
using DataControls.Application.Execution;
using DataControls.Application.Metadata;
using DataControls.Application.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataControls.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EngineOptions>(configuration.GetSection(EngineOptions.Section));

        services.AddSingleton<ControlDefinitionValidator>();
        services.AddSingleton<DefinitionDescriber>();
        services.AddSingleton<ReasonBuilder>();

        services.AddScoped<ControlRunner>();
        services.AddScoped<ControlService>();
        services.AddScoped<MetadataService>();
        services.AddScoped<AiControlService>();
        return services;
    }
}
