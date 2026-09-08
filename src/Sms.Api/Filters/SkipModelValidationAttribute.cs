using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Sms.Api.Filters;

/// <summary>
/// Applied to actions whose request DTO nests a shared type with non-nullable reference-type
/// properties (e.g. bulk import rows nesting the shared <c>CreateStudentRequest</c>, whose
/// <c>Name</c> is non-nullable). [ApiController]'s automatic model-state validation infers those
/// properties as implicitly required and would otherwise reject the ENTIRE request with 400 the
/// moment any single nested row has a null/missing value — before the action's own per-row
/// validation guard ever runs, silently breaking "one bad row must never affect the others in
/// the batch" at the framework level, for the whole batch rather than just the offending row.
///
/// This filter suppresses only those inferred-<c>[Required]</c> VALIDATION errors, with
/// <see cref="Order"/> set to the lowest possible value so it runs before the framework's
/// built-in ModelStateInvalidFilter. Every other controller/action keeps the normal automatic
/// model validation behavior.
///
/// It deliberately does NOT clear model-BINDING errors — entries carrying a
/// <see cref="ModelError.Exception"/>, which is how the System.Text.Json input formatter reports
/// a body it could not deserialize at all (a non-GUID <c>route_id</c>/<c>import_id</c>, malformed
/// JSON, a wrong-typed field). A blanket <c>ModelState.Clear()</c> used to swallow those too: the
/// request DTO then bound to <c>null</c>, ModelStateInvalidFilter no longer returned 400, and the
/// action ran on a null request — a 500 NullReferenceException that failed the whole batch instead
/// of a 400 naming the bad field. It also silently coerced a malformed <c>import_id</c> to
/// <see cref="Guid.Empty"/>, turning it into an idempotency key shared across unrelated imports.
/// Binding errors must stay in ModelState so the framework returns a clean, actionable 400.
/// </summary>
public sealed class SkipModelValidationAttribute : ActionFilterAttribute
{
    public SkipModelValidationAttribute() => Order = int.MinValue;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // System.Text.Json aborts on the first unconvertible value, so a body-binding failure
        // always means the WHOLE request object failed to bind and the action's parameter is
        // absent/null. In that case leave ModelState exactly as the framework left it, so
        // ModelStateInvalidFilter answers with a 400 naming the offending JSON path.
        var anyParameterFailedToBind = context.ActionDescriptor.Parameters.Any(p =>
            p.ParameterType != typeof(CancellationToken) &&
            (!context.ActionArguments.TryGetValue(p.Name, out var value) || value is null));
        if (anyParameterFailedToBind)
            return;

        // Otherwise drop only the inferred-required VALIDATION errors on the nested rows.
        // Input-formatter (JSON deserialization) errors are keyed by JSON path — "$.rows[0]..." —
        // whereas validation errors are keyed by model-binding path with no "$" prefix; keep the
        // former regardless, they are never the "one bad row" case this attribute exists for.
        // Snapshot the keys first: removing entries mutates the dictionary being enumerated.
        var validationOnlyKeys = context.ModelState
            .Where(kvp => kvp.Value is { Errors.Count: > 0 } && !kvp.Key.StartsWith('$'))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in validationOnlyKeys)
            context.ModelState.Remove(key);
    }
}
