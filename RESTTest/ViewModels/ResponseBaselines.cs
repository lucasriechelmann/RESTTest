using System.Runtime.CompilerServices;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// Remembers, per request, the last two responses and an optional pinned one, so a response can be
/// compared with "what this request returned last time" or with a response the user kept. In memory
/// only: a response is never persisted, and a request that is deleted takes its entry with it.
/// </summary>
public sealed class ResponseBaselines
{
    private readonly ConditionalWeakTable<RequestViewModel, Entry> _entries = [];

    private sealed class Entry
    {
        public ResponseBaseline? Last { get; set; }

        public ResponseBaseline? Prior { get; set; }

        public ResponseBaseline? Pinned { get; set; }
    }

    /// <summary>What a response about to arrive should be compared with: the pinned one, else the last one.</summary>
    public ResponseBaseline? ForNextSend(RequestViewModel request) =>
        _entries.TryGetValue(request, out var entry) ? entry.Pinned ?? entry.Last : null;

    /// <summary>What the response now showing should be compared with: the pinned one, else the one before it.</summary>
    public ResponseBaseline? ForCurrent(RequestViewModel request) =>
        _entries.TryGetValue(request, out var entry) ? entry.Pinned ?? entry.Prior : null;

    /// <summary>Notes a received response as the request's latest. Failures are not responses and are not recorded.</summary>
    public void Record(RequestViewModel request, HttpExchangeResult result)
    {
        if (!result.Completed)
        {
            return;
        }

        var entry = _entries.GetOrCreateValue(request);
        entry.Prior = entry.Last;
        entry.Last = new ResponseBaseline(result, DateTime.Now, Pinned: false);
    }

    /// <summary>Keeps the latest response as the baseline until it is unpinned. False when there is none yet.</summary>
    public bool PinCurrent(RequestViewModel request)
    {
        if (!_entries.TryGetValue(request, out var entry) || entry.Last is not { } last)
        {
            return false;
        }

        entry.Pinned = last with { Pinned = true };
        return true;
    }

    public void Unpin(RequestViewModel request)
    {
        if (_entries.TryGetValue(request, out var entry))
        {
            entry.Pinned = null;
        }
    }
}
