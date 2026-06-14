using Dapper;

namespace Sms.Shared.Kernel.Data;

public static class DapperSnakeCaseConfig
{
    private static bool _applied;
    public static void Apply()
    {
        if (_applied) return;
        DefaultTypeMap.MatchNamesWithUnderscores = true; // maps admission_no -> AdmissionNo
        _applied = true;
    }
}
