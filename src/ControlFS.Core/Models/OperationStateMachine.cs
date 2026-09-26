namespace ControlFS.Core.Models;

/// <summary>Transições válidas de uma operação. Comandos incompatíveis com o estado atual são recusados.</summary>
public static class OperationStateMachine
{
    private static readonly Dictionary<OperationState, OperationState[]> Allowed = new()
    {
        [OperationState.Queued] = [OperationState.Planning, OperationState.CancelRequested, OperationState.Cancelled],
        [OperationState.Planning] = [OperationState.Running, OperationState.WaitingForUser, OperationState.CancelRequested, OperationState.Failed],
        [OperationState.Running] = [OperationState.WaitingForUser, OperationState.Paused, OperationState.CancelRequested, OperationState.Completed, OperationState.CompletedWithWarnings, OperationState.Failed],
        [OperationState.WaitingForUser] = [OperationState.Running, OperationState.CancelRequested, OperationState.Failed],
        [OperationState.Paused] = [OperationState.Running, OperationState.CancelRequested],
        [OperationState.CancelRequested] = [OperationState.Cancelled, OperationState.Failed, OperationState.Completed, OperationState.CompletedWithWarnings],
        [OperationState.Cancelled] = [],
        [OperationState.Completed] = [],
        [OperationState.CompletedWithWarnings] = [],
        [OperationState.Failed] = [],
    };

    public static bool CanTransition(OperationState from, OperationState to) => Allowed[from].Contains(to);

    public static bool IsTerminal(OperationState state) => Allowed[state].Length == 0;

    public static OperationState Transition(OperationState from, OperationState to) =>
        CanTransition(from, to) ? to : throw new InvalidOperationException($"Transição inválida: {from} -> {to}");
}
