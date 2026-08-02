using System.IO;
using System.Reflection;
using QuizBuilder.Core.Interfaces;
using WeCantSpell.Hunspell;

namespace QuizBuilder.App.Services;

/// <summary>
/// The offline dictionary: a thin adapter from Core's <see cref="ISpellDictionary"/>
/// to WeCantSpell.Hunspell, loading the en_US .dic/.aff pair embedded in this
/// assembly (Resources/Dictionaries). This is the one piece of feature B that a
/// plain CI runner cannot verify — whether the dictionary loads under a
/// single-file publish and whether Hunspell flags real misspellings — so it is
/// deliberately tiny: construction loads the streams, and the two methods
/// delegate straight to Hunspell. All the surrounding logic lives in Core's
/// <see cref="QuizBuilder.Core.Services.SpellReviewEngine"/>, which is tested.
///
/// <para>
/// Pure-managed: WeCantSpell.Hunspell has no System.Drawing / native
/// dependency, so it is safe under net8.0-windows and needs nothing extra at
/// publish. The dictionary is embedded rather than shipped beside the exe so it
/// travels inside the single-file bundle and honours the portability invariant.
/// </para>
/// </summary>
public sealed class HunspellDictionary : ISpellDictionary
{
    // Resource names are "{RootNamespace}.{folder-with-dots}.{file}". RootNamespace
    // is QuizBuilder.App (see csproj), folder Resources/Dictionaries.
    private const string DicResource = "QuizBuilder.App.Resources.Dictionaries.en_US.dic";
    private const string AffResource = "QuizBuilder.App.Resources.Dictionaries.en_US.aff";

    private readonly WordList _wordList;

    public HunspellDictionary()
    {
        var asm = typeof(HunspellDictionary).Assembly;

        using var dic = OpenResource(asm, DicResource);
        using var aff = OpenResource(asm, AffResource);

        // WordList.CreateFromStreams takes (dictionaryStream, affixStream).
        _wordList = WordList.CreateFromStreams(dic, aff);
    }

    public bool IsKnown(string word) => _wordList.Check(word);

    public IReadOnlyList<string> Suggest(string word) =>
        _wordList.Suggest(word) is var s && s is not null
            ? s.ToList()
            : Array.Empty<string>();

    private static Stream OpenResource(Assembly asm, string name)
    {
        var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
        {
            // A misnamed resource is a build-time packaging error, not a runtime
            // condition to limp past. Name the resource so the fix is obvious.
            var available = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded dictionary resource '{name}' was not found. " +
                $"Available resources: {available}. Check the EmbeddedResource " +
                "items in QuizBuilder.App.csproj and the file names under " +
                "Resources/Dictionaries.");
        }
        return stream;
    }
}
