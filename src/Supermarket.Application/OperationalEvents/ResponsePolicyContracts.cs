namespace Supermarket.Application;

public sealed record AutomaticTaskPolicyRequest(bool IsEnabled, string? Title, string? Instructions);
public sealed record ResponsePolicySaveRequest(bool IsEnabled, int? ExpectedVersion, AutomaticTaskPolicyRequest? Action);
public sealed record ResponsePolicyDeleteRequest(int ExpectedVersion);

public sealed record AutomaticTaskPolicyView(
    Guid ActionId,
    bool IsEnabled,
    string ActionType,
    string ExecutionMode,
    string? Title,
    string? Instructions);

public sealed record ResponsePolicyView(
    Guid PolicyId,
    Guid RuleId,
    string EventPriority,
    bool IsEnabled,
    int Version,
    DateTime UpdatedAt,
    AutomaticTaskPolicyView? Action,
    bool ReadyForAutomaticTask,
    IReadOnlyList<string> Issues);
