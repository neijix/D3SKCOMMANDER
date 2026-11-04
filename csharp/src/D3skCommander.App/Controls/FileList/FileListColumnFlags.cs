namespace D3skCommander.App.Controls.FileList;

/// <summary>Optional columns of the full view. The name column is always shown.</summary>
[Flags]
public enum FileListColumnFlags
{
    None = 0,
    Extension = 1,
    Size = 2,
    Date = 4,
    Attributes = 8,
    All = Extension | Size | Date | Attributes,
}
