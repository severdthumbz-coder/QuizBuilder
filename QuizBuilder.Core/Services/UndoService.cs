using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Snapshot-based undo/redo over <see cref="IQuizDocumentService"/>.
///
/// <para>
/// Each step is the document serialised to JSON. Serialising rather than
/// holding object references is the whole point: the live document is mutated
/// in place by the editors, so a reference would age into a copy of the
/// present rather than a record of the past.
/// </para>
/// </summary>
public sealed class UndoService : IUndoService
{
    private readonly IQuizDocumentService _document;

    // Snapshots paired with what the user did, so the menu can say
    // "Undo Delete section" rather than a bare "Undo".
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();

    private int _depth = UndoSettings.DefaultDepth;

    /// <summary>
    /// Set while restoring, so the DocumentReplaced raised by the restore is
    /// not mistaken for a user edit and pushed back onto the stack.
    /// </summary>
    private bool _restoring;

    public UndoService(IQuizDocumentService document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.DocumentChanged += OnDocumentChanged;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoDepth => _undo.Count;
    public int RedoDepth => _redo.Count;

    public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    public event EventHandler? StateChanged;

    public void CaptureBeforeChange(string label)
    {
        if (_depth <= 0) return;
        if (_restoring) return;

        var json = Serialise(_document.Current);
        if (json is null) return;

        _undo.Add(new Snapshot(json, label));
        TrimOldest(_undo);

        // A new edit invalidates the redo branch: the future it would have
        // replayed is no longer reachable from here.
        _redo.Clear();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0) return;

        _undo.Clear();
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDepth(int depth)
    {
        _depth = Math.Clamp(depth, UndoSettings.MinDepth, UndoSettings.MaxDepth);

        if (_depth == 0)
        {
            Clear();
            return;
        }

        TrimOldest(_undo);
        TrimOldest(_redo);

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves one snapshot from <paramref name="from"/> to <paramref name="to"/>,
    /// putting the current document on <paramref name="to"/> first so the move
    /// is reversible. Undo and redo are the same operation with the stacks
    /// swapped; writing it once keeps them exactly symmetrical.
    /// </summary>
    private bool Step(List<Snapshot> from, List<Snapshot> to)
    {
        if (from.Count == 0) return false;

        var current = Serialise(_document.Current);
        if (current is null) return false;

        var target = from[^1];
        from.RemoveAt(from.Count - 1);

        var restored = Deserialise(target.Json);
        if (restored is null)
        {
            // The snapshot is unusable. Dropping it (done above) and stopping
            // is better than half-applying it: the document on screen is still
            // coherent, which it would not be after a partial restore.
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        // Label the opposite stack with what this step reverses, so redo reads
        // as the same action the user originally took.
        to.Add(new Snapshot(current, target.Label));
        TrimOldest(to);

        _restoring = true;
        try
        {
            _document.RestoreDocument(restored);
        }
        finally
        {
            _restoring = false;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Drops the oldest entries once the stack exceeds the configured depth.
    /// Oldest rather than newest: the recent past is what a user reaches for.
    /// </summary>
    private void TrimOldest(List<Snapshot> stack)
    {
        var excess = stack.Count - _depth;
        if (excess > 0) stack.RemoveRange(0, excess);
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        // Opening or starting a document makes the history meaningless: those
        // steps belong to a document that is no longer on screen.
        if (e.Kind == DocumentChangeKind.DocumentReplaced && !_restoring)
            Clear();
    }

    private static string? Serialise(QuizDocument document)
    {
        try
        {
            return JsonSerializer.Serialize(document, SettingsService.JsonOptions);
        }
        catch (JsonException)
        {
            // Never let undo bookkeeping take down an edit the user asked for.
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static QuizDocument? Deserialise(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<QuizDocument>(json, SettingsService.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private readonly record struct Snapshot(string Json, string Label);
}
