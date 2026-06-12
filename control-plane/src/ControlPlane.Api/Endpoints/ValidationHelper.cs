using System.ComponentModel.DataAnnotations;

namespace ControlPlane.Api.Endpoints;

public static class ValidationHelper
{
    /// Returns a 400 ValidationProblem result, or null when the request is valid.
    public static IResult? Validate(MonitorRequest request)
    {
        var errors = new Dictionary<string, List<string>>();

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        foreach (var result in results)
        {
            foreach (var member in result.MemberNames.DefaultIfEmpty(string.Empty))
            {
                AddError(errors, member, result.ErrorMessage ?? "Invalid value.");
            }
        }

        // Cross-field rules DataAnnotations can't express.
        if (request.Url is not null &&
            (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            AddError(errors, nameof(request.Url), "Url must be an absolute http or https URI.");
        }

        if (request.TimeoutSeconds >= request.IntervalSeconds)
        {
            AddError(errors, nameof(request.TimeoutSeconds), "TimeoutSeconds must be less than IntervalSeconds.");
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    private static void AddError(Dictionary<string, List<string>> errors, string member, string message)
    {
        if (!errors.TryGetValue(member, out var messages))
        {
            errors[member] = messages = [];
        }

        messages.Add(message);
    }
}
