using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace AdPulse.Api.Contracts;

/// <summary>
/// Single place that shapes every error as RFC 7807 <c>application/problem+json</c>.
/// <c>type</c> is a stable URI whose last segment is the machine-readable code (clients switch on it),
/// <c>traceId</c> ties the response to the Serilog/ELK request log line.
/// </summary>
public static class Problems
{
    public const string TypeBase = "https://adpulse.dev/errors/";

    public static IResult Create(HttpContext http, int status, string code, string detail, IDictionary<string, object?>? extensions = null)
    {
        var ext = new Dictionary<string, object?>(extensions ?? new Dictionary<string, object?>()) { ["traceId"] = http.TraceIdentifier };
        return Results.Problem(detail: detail, statusCode: status, title: Title(status), type: TypeBase + code, extensions: ext);
    }

    public static Task Write(HttpContext http, int status, string code, string detail)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json";
        var pd = new ProblemDetails { Status = status, Title = Title(status), Type = TypeBase + code, Detail = detail };
        pd.Extensions["traceId"] = http.TraceIdentifier;
        return http.Response.WriteAsJsonAsync(pd, options: null, contentType: "application/problem+json");
    }

    public static IResult? Validate(HttpContext http, object model)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true)) return null;
        var errors = results
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? "")
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? "invalid").ToArray());
        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Validation failed",
            type: TypeBase + "validation_error", extensions: new Dictionary<string, object?> { ["traceId"] = http.TraceIdentifier });
    }

    public static string Title(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        _ => "Internal Server Error",
    };
}
