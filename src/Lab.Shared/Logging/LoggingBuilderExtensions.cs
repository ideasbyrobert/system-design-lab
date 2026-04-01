using Lab.Shared.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lab.Shared.Logging;

public static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddLabOperationalFileLogging(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<SafeFileAppender>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, LabOperationalFileLoggerProvider>());

        return builder;
    }
}
