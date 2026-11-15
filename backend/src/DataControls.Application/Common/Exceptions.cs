using DataControls.Application.Validation;

namespace DataControls.Application.Common;

/// <summary>שגיאה שההודעה שלה מיועדת להצגה למשתמש העסקי (בעברית, ללא פרטים טכניים).</summary>
public abstract class UserFacingException : Exception
{
    protected UserFacingException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class NotFoundException : UserFacingException
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class DefinitionValidationException : UserFacingException
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public DefinitionValidationException(IReadOnlyList<ValidationError> errors)
        : base("הגדרת הבקרה אינה תקינה")
    {
        Errors = errors;
    }
}

/// <summary>כשל בהרצת שאילתה (Timeout, שינוי מבנה DB וכו').</summary>
public sealed class QueryExecutionException : UserFacingException
{
    public bool IsTimeout { get; }

    public QueryExecutionException(string message, Exception? inner = null, bool isTimeout = false)
        : base(message, inner)
    {
        IsTimeout = isTimeout;
    }
}

/// <summary>שירות ה-AI אינו זמין או החזיר תשובה שאינה בפורמט המוסכם.</summary>
public sealed class AiServiceException : UserFacingException
{
    public AiServiceException(string message, Exception? inner = null) : base(message, inner) { }
}
