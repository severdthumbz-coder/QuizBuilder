using System.Collections.ObjectModel;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// One study card in the editor. Text edits flow straight to the document
/// service, which coalesces no-op changes, so per-keystroke edits are cheap.
/// </summary>
public sealed class StudyCardRowViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly IQuizPackageService _images;
    private readonly StudyCard _card;
    private string _front;
    private string _back;

    public StudyCardRowViewModel(IQuizDocumentService document, IQuizPackageService images, StudyCard card)
    {
        _document = document;
        _images = images;
        _card = card;
        Id = card.Id;
        _front = card.Front;
        _back = card.Back;

        RemoveFrontImageCommand = new RelayCommand(RemoveFrontImage, () => HasFrontImage);
        RemoveBackImageCommand = new RelayCommand(RemoveBackImage, () => HasBackImage);
    }

    public Guid Id { get; }

    public RelayCommand RemoveFrontImageCommand { get; }
    public RelayCommand RemoveBackImageCommand { get; }

    public bool HasFrontImage => !string.IsNullOrEmpty(_card.FrontImageRelativePath);
    public bool HasBackImage => !string.IsNullOrEmpty(_card.BackImageRelativePath);

    public byte[]? FrontImageBytes =>
        string.IsNullOrEmpty(_card.FrontImageRelativePath) ? null : _images.GetImage(_card.FrontImageRelativePath);

    public byte[]? BackImageBytes =>
        string.IsNullOrEmpty(_card.BackImageRelativePath) ? null : _images.GetImage(_card.BackImageRelativePath);

    /// <summary>
    /// Called before a structural edit on this card, so it can be undone.
    /// Attaching or removing an image is a discrete action; typing in the front
    /// or back field is not, and is left to the text box's own undo.
    /// </summary>
    public Action<string>? CaptureBeforeChange { get; set; }

    public void AttachFrontImage(byte[] bytes, string fileName)
    {
        // Before the mutation: the snapshot is the state undo returns to.
        CaptureBeforeChange?.Invoke("Add card image");

        _card.FrontImageRelativePath = _images.AddImage(bytes, fileName);
        _document.UpdateStudyCard(Id, _front, _back);   // marks dirty
        OnPropertyChanged(nameof(HasFrontImage));
        OnPropertyChanged(nameof(FrontImageBytes));
        RelayCommand.RaiseCanExecuteChanged();
    }

    public void AttachBackImage(byte[] bytes, string fileName)
    {
        CaptureBeforeChange?.Invoke("Add card image");

        _card.BackImageRelativePath = _images.AddImage(bytes, fileName);
        _document.UpdateStudyCard(Id, _front, _back);
        OnPropertyChanged(nameof(HasBackImage));
        OnPropertyChanged(nameof(BackImageBytes));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void RemoveFrontImage()
    {
        CaptureBeforeChange?.Invoke("Remove card image");

        _card.FrontImageRelativePath = null;
        _document.UpdateStudyCard(Id, _front, _back);
        OnPropertyChanged(nameof(HasFrontImage));
        OnPropertyChanged(nameof(FrontImageBytes));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void RemoveBackImage()
    {
        CaptureBeforeChange?.Invoke("Remove card image");

        _card.BackImageRelativePath = null;
        _document.UpdateStudyCard(Id, _front, _back);
        OnPropertyChanged(nameof(HasBackImage));
        OnPropertyChanged(nameof(BackImageBytes));
        RelayCommand.RaiseCanExecuteChanged();
    }

    public string Front
    {
        get => _front;
        set
        {
            if (_front == value) return;

            _front = value;
            OnPropertyChanged();
            _document.UpdateStudyCard(Id, _front, _back);
        }
    }

    public string Back
    {
        get => _back;
        set
        {
            if (_back == value) return;

            _back = value;
            OnPropertyChanged();
            _document.UpdateStudyCard(Id, _front, _back);
        }
    }
}

/// <summary>
/// The Study Cards tab: author front/back cards that feed the Flash Cards tab.
///
/// These are not quiz questions -- no grading, no points -- so they get their
/// own tab rather than cluttering the Quiz Builder. The Flash Cards tab picks
/// them up through the source setting (Quiz / Study cards / Both).
///
/// The card list is rebuilt from the document only on a document REPLACE (open
/// or new). Add, remove, and move edit the collection and the document in
/// lockstep, and a row's text edits go straight to the document -- so typing
/// never triggers a full rebuild that would reset the caret and focus.
/// </summary>
public sealed class StudyCardsViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly IQuizPackageService _images;
    private readonly IUndoService _undo;

    private bool _isVisible;
    private bool _isStale = true;

    public StudyCardsViewModel(
        IQuizDocumentService document,
        IQuizPackageService images,
        IUndoService undo)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));

        Cards = new ObservableCollection<StudyCardRowViewModel>();

        _document.DocumentChanged += (_, e) =>
        {
            // Only a replaced document needs a full reload. Our own structural
            // edits keep the collection in step themselves, and text edits must
            // not rebuild the list mid-type.
            if (e.Kind == DocumentChangeKind.DocumentReplaced) MarkStale();
        };

        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(p => Remove(p as StudyCardRowViewModel));
        MoveUpCommand = new RelayCommand(p => Move(p as StudyCardRowViewModel, -1));
        MoveDownCommand = new RelayCommand(p => Move(p as StudyCardRowViewModel, +1));
    }

    public ObservableCollection<StudyCardRowViewModel> Cards { get; }

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public bool HasCards => Cards.Count > 0;

    public string EmptyMessage =>
        "No study cards yet. Add cards with a term or question on the front and its "
        + "answer on the back — they show up in the Flash Cards tab when its source "
        + "includes study cards.";

    private void MarkStale()
    {
        if (_isVisible) Reload();
        else _isStale = true;
    }

    public void OnActivated()
    {
        _isVisible = true;
        if (_isStale) Reload();
    }

    public void OnDeactivated() => _isVisible = false;

    private void Reload()
    {
        _isStale = false;

        Cards.Clear();

        foreach (var card in _document.Current.StudyCards)
            Cards.Add(NewRow(card));

        OnPropertyChanged(nameof(HasCards));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void Add()
    {
        _undo.CaptureBeforeChange("Add study card");
        var card = _document.AddStudyCard();

        Cards.Add(NewRow(card));

        OnPropertyChanged(nameof(HasCards));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Builds a row with its undo hook attached. Rows are created in more than
    /// one place, and a row built without the hook silently loses undo for its
    /// images -- so construction goes through here rather than being repeated.
    /// </summary>
    private StudyCardRowViewModel NewRow(StudyCard card) =>
        new(_document, _images, card) { CaptureBeforeChange = _undo.CaptureBeforeChange };

    private void Remove(StudyCardRowViewModel? row)
    {
        if (row is null) return;

        _undo.CaptureBeforeChange("Delete study card");
        _document.RemoveStudyCard(row.Id);
        Cards.Remove(row);

        OnPropertyChanged(nameof(HasCards));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void Move(StudyCardRowViewModel? row, int delta)
    {
        if (row is null) return;

        var index = Cards.IndexOf(row);
        var target = index + delta;

        if (index < 0 || target < 0 || target >= Cards.Count) return;

        _undo.CaptureBeforeChange("Move study card");
        _document.MoveStudyCard(row.Id, target);
        Cards.Move(index, target);
    }
}
