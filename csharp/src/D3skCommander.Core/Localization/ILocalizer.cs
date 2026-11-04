namespace D3skCommander.Core.Localization;

/// <summary>Injected en/fr text. The current language switches at run time.</summary>
public interface ILocalizer
{
    Language Language { get; }

    void Use(Language language);

    /// <summary>Translation of <paramref name="key"/>, or the key itself when it is missing (v0.1 <c>tr</c>).</summary>
    string Get(string key);

    /// <summary>Inline bilingual text (v0.1 <c>t</c>).</summary>
    string Text(string english, string french);
}
