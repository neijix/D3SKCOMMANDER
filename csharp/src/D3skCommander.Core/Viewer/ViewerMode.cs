namespace D3skCommander.Core.Viewer;

/// <summary>What the lister is showing. The window draws one of these.</summary>
public enum ViewerMode
{
    Text,
    Hex,
    Image,
    Table,
}

/// <summary>Find missed. The lister window turns this into the bilingual "Not found" line.</summary>
public enum ViewerNotice
{
    None,
    NotFound,
}
