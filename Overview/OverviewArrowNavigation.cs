using System.Windows.Input;

namespace Spaces4Win.Overview;

/// <summary>
/// Overview arrow policy: horizontal stays on one workspace strip (crossing only to the
/// same workspace id on the left/right monitor); vertical changes workspace on the same
/// monitor. No wrapping/cycling at edges.
/// </summary>
public static class OverviewArrowNavigation
{
    public readonly record struct Group(
        int MonitorIndex,
        double MonitorLeft,
        int WorkspaceId,
        int WorkspaceOrder,
        int CardCount);

    public readonly record struct Position(int GroupIndex, int CardIndex);

    public static Position? TryMove(
        IReadOnlyList<Group> groups,
        Position current,
        Key key)
    {
        if (groups.Count == 0 ||
            current.GroupIndex < 0 ||
            current.GroupIndex >= groups.Count)
        {
            return null;
        }

        var group = groups[current.GroupIndex];
        if (group.CardCount <= 0)
        {
            return null;
        }

        var card = Math.Clamp(current.CardIndex, 0, group.CardCount - 1);

        return key switch
        {
            Key.Right => MoveHorizontal(groups, current.GroupIndex, card, +1),
            Key.Left => MoveHorizontal(groups, current.GroupIndex, card, -1),
            Key.Down => MoveVertical(groups, current.GroupIndex, card, +1),
            Key.Up => MoveVertical(groups, current.GroupIndex, card, -1),
            _ => null
        };
    }

    private static Position MoveHorizontal(
        IReadOnlyList<Group> groups,
        int groupIndex,
        int cardIndex,
        int direction)
    {
        var group = groups[groupIndex];
        var nextCard = cardIndex + direction;
        if (nextCard >= 0 && nextCard < group.CardCount)
        {
            return new Position(groupIndex, nextCard);
        }

        // Cross only to the adjacent monitor (by left edge) with the same workspace id.
        var targetMonitor = group.MonitorIndex + direction;
        for (var i = 0; i < groups.Count; i++)
        {
            var candidate = groups[i];
            if (candidate.MonitorIndex != targetMonitor ||
                candidate.WorkspaceId != group.WorkspaceId ||
                candidate.CardCount <= 0)
            {
                continue;
            }

            var landing = direction > 0
                ? 0
                : candidate.CardCount - 1;
            return new Position(i, landing);
        }

        return new Position(groupIndex, cardIndex);
    }

    private static Position MoveVertical(
        IReadOnlyList<Group> groups,
        int groupIndex,
        int cardIndex,
        int direction)
    {
        var group = groups[groupIndex];
        var targetOrder = group.WorkspaceOrder + direction;

        var bestIndex = -1;
        for (var i = 0; i < groups.Count; i++)
        {
            var candidate = groups[i];
            if (candidate.MonitorIndex != group.MonitorIndex ||
                candidate.WorkspaceOrder != targetOrder ||
                candidate.CardCount <= 0)
            {
                continue;
            }

            bestIndex = i;
            break;
        }

        if (bestIndex < 0)
        {
            return new Position(groupIndex, cardIndex);
        }

        var landing = Math.Clamp(cardIndex, 0, groups[bestIndex].CardCount - 1);
        return new Position(bestIndex, landing);
    }
}
