using DataControls.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Infrastructure;

/// <summary>
/// ממפה חריגות ל-ProblemDetails (RFC 7807) עם הודעות בעברית למשתמש.
/// פרטים טכניים נרשמים בלוג בלבד ואינם נחשפים ללקוח.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            DefinitionValidationException => (StatusCodes.Status400BadRequest, exception.Message),
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            QueryExecutionException { IsTimeout: true } => (StatusCodes.Status504GatewayTimeout, exception.Message),
            QueryExecutionException => (StatusCodes.Status422UnprocessableEntity, exception.Message),
            AiServiceException => (StatusCodes.Status503ServiceUnavailable, exception.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "הבקשה אינה תקינה"),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "הבקשה בוטלה"),
            _ => (StatusCodes.Status500InternalServerError, "אירעה שגיאה בלתי צפויה. הפרטים נרשמו ביומן המערכת.")
        };

        if (status >= 500)
            _logger.LogError(exception, "Unhandled error on {Path}", context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (exception is DefinitionValidationException dve)
            problem.Extensions["errors"] = dve.Errors;

        context.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
