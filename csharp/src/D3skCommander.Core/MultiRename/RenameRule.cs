namespace D3skCommander.Core.MultiRename;

/// <summary>Masks, search, case and counter for one multi-rename batch. Defaults match v0.1.</summary>
public sealed class RenameRule
{
    public RenameRule()
    {
        NameMask = "[N]";
        ExtMask = "[E]";
        Search = "";
        Replace = "";
        UseRegex = false;
        CaseSensitive = true;
        CaseMode = CaseMode.Unchanged;
        CounterStart = 1;
        CounterStep = 1;
        CounterDigits = 1;
    }

    public string NameMask { get; set; }

    public string ExtMask { get; set; }

    public string Search { get; set; }

    public string Replace { get; set; }

    public bool UseRegex { get; set; }

    public bool CaseSensitive { get; set; }

    public CaseMode CaseMode { get; set; }

    public long CounterStart { get; set; }

    public long CounterStep { get; set; }

    /// <summary>Zero-pad width of <c>[C]</c> when the placeholder does not set its own.</summary>
    public int CounterDigits { get; set; }
}
