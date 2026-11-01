namespace ApexAudio.Contracts;

public sealed record OperationError(string Code, string Message, string NextAction,
    IReadOnlyDictionary<string, string>? Details = null);

public class OperationException : IOException
{
    public OperationError Error { get; }
    public OperationException(string code, string message, string nextAction,
        IReadOnlyDictionary<string, string>? details = null,Exception? innerException=null) : base(message,innerException)
        => Error = new(code, message, nextAction, details);
    public static OperationException WithCleanup(Exception primary,Exception cleanup)
    {
        var original=(primary as OperationException)?.Error;
        var details=original?.Details==null?new Dictionary<string,string>():new(original.Details);
        details["primaryMessage"]=primary.Message;details["cleanupMessage"]=cleanup.Message;
        details["cleanupCode"]=(cleanup as OperationException)?.Error.Code ?? "cleanup_incomplete";
        return new(original?.Code ?? "operation_failed",primary.Message+"；资源清理也未完成："+cleanup.Message,"retry_cleanup",details,new AggregateException(primary,cleanup));
    }
}

public sealed record PackageVersionRef(string Id, string Version, string ManifestSha256);
