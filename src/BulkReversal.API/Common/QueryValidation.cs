using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Common;

// ActionResult (not IActionResult) is used as the return type throughout so callers returning
// either Task<IActionResult> or Task<ActionResult<T>> can both use these guards directly —
// ActionResult converts implicitly to both, while IActionResult does not convert to ActionResult<T>.

/// <summary>
/// Shared query-parameter guards for GET endpoints: page/pageSize and date ranges. Without this, an
/// out-of-range pageSize (zero, negative, or absurdly large) got silently clamped deep in a
/// repository instead of telling the caller their input was invalid, and an unbounded pageSize is a
/// cheap way to force the database to materialize a huge result set. This makes it an explicit 400
/// instead.
/// </summary>
public static class QueryValidation
{
    public const int MaxPageSize = 200;

    public static ActionResult? ValidatePagination(this ControllerBase controller, int page, int pageSize, int maxPageSize = MaxPageSize)
    {
        var errors = new Dictionary<string, string[]>();

        if (page < 1)
            errors["page"] = ["page must be 1 or greater."];

        if (pageSize < 1)
            errors["pageSize"] = ["pageSize must be 1 or greater."];
        else if (pageSize > maxPageSize)
            errors["pageSize"] = [$"pageSize must not exceed {maxPageSize}."];

        return errors.Count == 0 ? null : controller.ValidationProblem(new ValidationProblemDetails(errors));
    }

    public static ActionResult? ValidateDateRange(this ControllerBase controller, DateOnly? fromDate, DateOnly? toDate)
    {
        if (fromDate is null || toDate is null || fromDate <= toDate)
            return null;

        var errors = new Dictionary<string, string[]> { ["fromDate"] = ["fromDate must not be later than toDate."] };
        return controller.ValidationProblem(new ValidationProblemDetails(errors));
    }
}
