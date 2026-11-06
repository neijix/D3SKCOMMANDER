namespace D3skCommander.Core.Index;

/// <summary>The smallest <c>capacity</c> keys offered so far, kept in a max-heap so a worse key is rejected in one compare.</summary>
internal sealed class TopKeys(int capacity)
{
    private readonly long[] _heap = new long[Math.Max(1, capacity)];
    private int _count;

    public ReadOnlySpan<long> Keys => _heap.AsSpan(0, _count);

    public void Add(long key)
    {
        if (_count < _heap.Length)
        {
            var at = _count++;
            while (at > 0)
            {
                var parent = (at - 1) / 2;
                if (_heap[parent] >= key)
                {
                    break;
                }

                _heap[at] = _heap[parent];
                at = parent;
            }

            _heap[at] = key;
            return;
        }

        if (key < _heap[0])
        {
            SiftDown(key);
        }
    }

    private void SiftDown(long key)
    {
        var at = 0;
        while (true)
        {
            var child = (2 * at) + 1;
            if (child >= _count)
            {
                break;
            }

            if (child + 1 < _count && _heap[child + 1] > _heap[child])
            {
                child++;
            }

            if (_heap[child] <= key)
            {
                break;
            }

            _heap[at] = _heap[child];
            at = child;
        }

        _heap[at] = key;
    }
}
