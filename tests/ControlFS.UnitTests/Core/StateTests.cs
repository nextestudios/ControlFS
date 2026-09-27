using ControlFS.Application.State;
using ControlFS.Core.Models;

namespace ControlFS.UnitTests.Core;

public class StateTests
{
    private static FileEntry F(string name, long size = 0, bool dir = false) =>
        new(name, name, dir ? EntryKind.Directory : EntryKind.File, dir ? null : size);

    [Fact]
    public void Focus_survives_resort_by_identity()
    {
        var list = new FileListState();
        list.SetItems([F("b.txt", 5), F("a.txt", 10), F("c.txt", 1), F("pasta", dir: true)]);
        Assert.Equal("pasta", list.FocusedId); // pastas primeiro
        list.FocusById("b.txt");
        list.SetSort(new SortOrder(SortField.Size, Descending: true));
        Assert.Equal("b.txt", list.FocusedId);
        Assert.Equal(["pasta", "a.txt", "b.txt", "c.txt"], list.Items.Select(i => i.Id));
    }

    [Fact]
    public void Removed_focused_item_moves_focus_to_neighbor()
    {
        var list = new FileListState();
        list.SetItems([F("a"), F("b"), F("c")]);
        list.FocusById("b");
        list.SetItems([F("a"), F("c")]);
        Assert.Equal("c", list.FocusedId);
        list.FocusById("c");
        list.SetItems([F("a")]);
        Assert.Equal("a", list.FocusedId);
    }

    [Fact]
    public void Deleting_marked_items_moves_focus_to_the_next_survivor_not_the_same_index()
    {
        // Bug: com b e c excluídos e o foco em c, o foco caía no índice antigo (e), pulando d.
        var list = new FileListState();
        list.SetItems([F("a"), F("b"), F("c"), F("d"), F("e")]);
        list.FocusById("c");
        list.SetItems([F("a"), F("d"), F("e")]);
        Assert.Equal("d", list.FocusedId);
        list.FocusById("e");
        list.SetItems([F("a"), F("d")]);
        Assert.Equal("d", list.FocusedId);
    }

    [Fact]
    public void Natural_sort_orders_numbers_numerically()
    {
        var list = new FileListState();
        list.SetItems([F("arquivo10"), F("arquivo2"), F("Arquivo1")]);
        Assert.Equal(["Arquivo1", "arquivo2", "arquivo10"], list.Items.Select(i => i.Name));
    }

    [Fact]
    public void Selection_is_independent_of_focus()
    {
        var list = new FileListState();
        list.SetItems([F("a"), F("b")]);
        list.ToggleFocusedSelection();
        list.Move(1);
        Assert.Equal("b", list.FocusedId);
        Assert.Equal(["a"], list.SelectedIds);
        list.SetItems([F("a"), F("b"), F("c")], keepSelection: true);
        Assert.Equal(["a"], list.SelectedIds);
    }

    [Theory]
    [InlineData(OperationState.Queued, OperationState.Planning, true)]
    [InlineData(OperationState.Running, OperationState.WaitingForUser, true)]
    [InlineData(OperationState.WaitingForUser, OperationState.Running, true)]
    [InlineData(OperationState.Running, OperationState.CancelRequested, true)]
    [InlineData(OperationState.CancelRequested, OperationState.Cancelled, true)]
    [InlineData(OperationState.Completed, OperationState.Running, false)]
    [InlineData(OperationState.Cancelled, OperationState.Running, false)]
    [InlineData(OperationState.Queued, OperationState.Completed, false)]
    [InlineData(OperationState.Paused, OperationState.Completed, false)]
    public void Operation_transitions_are_explicit(OperationState from, OperationState to, bool allowed) =>
        Assert.Equal(allowed, OperationStateMachine.CanTransition(from, to));
}
