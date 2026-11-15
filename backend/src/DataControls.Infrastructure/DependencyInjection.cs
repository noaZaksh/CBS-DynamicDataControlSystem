using DataControls.Application.Abstractions;
using DataControls.Application.Ai;
using DataControls.Application.Execution;
using DataControls.Application.Metadata;
using DataControls.Infrastructure.Ai;
using DataControls.Infrastructure.Metadata;
using DataControls.Infrastructure.Persistence;
using DataControls.Infrastructure.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.Configure<AiServiceOptions>(configuration.GetSection(AiServiceOptions.Section));

        services.AddMemoryCache();

        // מילון נתונים
        services.AddSingleton<IMetadataProvider, SqlMetadataProvider>();
        services.AddScoped<ISchemaDiscoveryService, SqlSchemaDiscoveryService>();

        // מנוע: תרגום והרצה (ניתן להחליף ל-DB אחר ע"י מימוש IQueryCompiler/IQueryExecutor אחרים)
        services.AddSingleton<IQueryCompiler, SqlServerQueryCompiler>();
        services.AddScoped<IQueryExecutor, SqlQueryExecutor>();

        // אחסון
        services.AddScoped<IControlRepository, SqlControlRepository>();
        services.AddScoped<IControlRunRepository, SqlControlRunRepository>();

        // AI - מתאם HTTP לשירות Python. החלפת ספק = החלפת המימוש של IAiControlAssistant בלבד.
        services.AddHttpClient<IAiControlAssistant, PythonAiControlAssistant>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
