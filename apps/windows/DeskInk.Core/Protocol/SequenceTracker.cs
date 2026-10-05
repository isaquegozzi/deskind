namespace DeskInk.Core.Protocol;

public enum SequenceDisposition
{
    Accepted,
    AcceptedWithGap,
    Duplicate,
    ReorderedOrStale,
}

public readonly record struct SequenceResult(SequenceDisposition Disposition, uint Gap);

public sealed class SequenceTracker
{
    private bool _hasValue;
    private uint _last;

    public SequenceResult Observe(uint sequence)
    {
        if (!_hasValue)
        {
            _hasValue = true;
            _last = sequence;
            return new SequenceResult(SequenceDisposition.Accepted, 0);
        }

        var delta = unchecked(sequence - _last);
        if (delta == 0) return new SequenceResult(SequenceDisposition.Duplicate, 0);
        if (delta >= 0x8000_0000) return new SequenceResult(SequenceDisposition.ReorderedOrStale, 0);

        _last = sequence;
        return delta == 1
            ? new SequenceResult(SequenceDisposition.Accepted, 0)
            : new SequenceResult(SequenceDisposition.AcceptedWithGap, delta - 1);
    }

    public void Reset()
    {
        _hasValue = false;
        _last = 0;
    }
}
