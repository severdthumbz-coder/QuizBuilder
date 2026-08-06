using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

/// <summary>
/// Picks the answer-input template that matches a question presenter's concrete
/// type. The eight templates are defined in TakePage.xaml and assigned here, so
/// the page's ContentView just binds its Content and the right widgets appear.
/// </summary>
public sealed class QuestionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SingleChoice { get; set; }
    public DataTemplate? MultiChoice { get; set; }
    public DataTemplate? TrueFalse { get; set; }
    public DataTemplate? ShortAnswer { get; set; }
    public DataTemplate? FillBlank { get; set; }
    public DataTemplate? Matching { get; set; }
    public DataTemplate? Sequence { get; set; }
    public DataTemplate? Numeric { get; set; }
    public DataTemplate? Dropdown { get; set; }
    public DataTemplate? Essay { get; set; }
    public DataTemplate? Fallback { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) => item switch
    {
        SingleChoicePresenter => SingleChoice,
        MultiChoicePresenter => MultiChoice,
        TrueFalsePresenter => TrueFalse,
        ShortAnswerPresenter => ShortAnswer,
        FillBlankPresenter => FillBlank,
        MatchingPresenter => Matching,
        SequencePresenter => Sequence,
        NumericPresenter => Numeric,
        DropdownPresenter => Dropdown,
        EssayPresenter => Essay,
        _ => Fallback,
    };
}
