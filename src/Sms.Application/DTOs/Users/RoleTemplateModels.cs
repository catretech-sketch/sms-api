namespace Sms.Application.DTOs.Users;

public sealed record RoleTemplateOverrideDto(string Role, string Module, string Cap, string Effect);
public sealed record SetRoleTemplateRequest(RoleTemplateOverrideDto[] Overrides);
