using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application.Operations;

/// <summary>Leva um conflito da thread de trabalho para a UI e aguarda a decisão do usuário.</summary>
internal sealed class UiConflictInteraction(AppController controller, OperationItem operation) : IExtractionInteraction, IConflictInteraction
{
    public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => answer.TrySetResult(new ConflictDecision(ConflictChoice.Cancel)));
        controller.Post(() =>
        {
            if (answer.Task.IsCompleted) return;
            controller.Operations.SetWaiting(operation, true);
            controller.ShowConflictDialog(conflict, operation, answer);
        });
        return answer.Task;
    }
}
