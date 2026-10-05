using DeskInk.Core.Protocol;

namespace DeskInk.Core.Input;

public enum PenLifecycleState
{
    OutOfRange,
    Hover,
    Contact,
    SuppressedUntilPhysicalRelease,
}

public enum PenTransition
{
    None,
    Hover,
    Down,
    Update,
    Up,
    Suppressed,
}

public sealed class PenStateMachine
{
    public PenLifecycleState State { get; private set; } = PenLifecycleState.OutOfRange;

    public PenTransition Apply(PenSample sample)
    {
        var inRange = sample.State.HasFlag(PenSampleState.InRange);
        var contact = sample.State.HasFlag(PenSampleState.Contact);
        if (contact && !inRange) throw new ProtocolException("CONTACT requires IN_RANGE");

        if (sample.State.HasFlag(PenSampleState.Canceled))
        {
            var transition = State == PenLifecycleState.Contact ? PenTransition.Up : PenTransition.Suppressed;
            State = PenLifecycleState.SuppressedUntilPhysicalRelease;
            return transition;
        }

        if (State == PenLifecycleState.SuppressedUntilPhysicalRelease)
        {
            if (contact) return PenTransition.Suppressed;
            State = inRange ? PenLifecycleState.Hover : PenLifecycleState.OutOfRange;
            return PenTransition.None;
        }

        var prior = State;
        State = contact
            ? PenLifecycleState.Contact
            : inRange
                ? PenLifecycleState.Hover
                : PenLifecycleState.OutOfRange;

        return (prior, State) switch
        {
            (PenLifecycleState.Contact, PenLifecycleState.Contact) => PenTransition.Update,
            (PenLifecycleState.Contact, _) => PenTransition.Up,
            (_, PenLifecycleState.Contact) => PenTransition.Down,
            (_, PenLifecycleState.Hover) => PenTransition.Hover,
            _ => PenTransition.None,
        };
    }

    public PenTransition ReleaseAll()
    {
        var transition = State == PenLifecycleState.Contact ? PenTransition.Up : PenTransition.None;
        State = PenLifecycleState.SuppressedUntilPhysicalRelease;
        return transition;
    }
}
