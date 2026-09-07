using Microsoft.AspNetCore.Mvc.Filters;

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
/// Clearing <see cref="ActionExecutingContext.ModelState"/> here — with <see cref="Order"/> set
/// to the lowest possible value so this filter runs before the framework's built-in
/// ModelStateInvalidFilter — prevents that automatic 400 for this action only, letting the
/// action's own per-row guard (see StudentBulkImportService.RequiredFieldError) make the actual
/// per-row decision instead. Scoped to this one action; every other controller/action keeps the
/// normal automatic model validation behavior.
/// </summary>
public sealed class SkipModelValidationAttribute : ActionFilterAttribute
{
    public SkipModelValidationAttribute() => Order = int.MinValue;

    public override void OnActionExecuting(ActionExecutingContext context) => context.ModelState.Clear();
}
