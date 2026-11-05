namespace D3skCommander.Core.TextDocument;

/// <summary>
/// The outcome of save, find or replace. The view translates it
/// (Saved, Not found, "{n} replaced"); <see cref="TextDocument.NoticeCount"/> is n.
/// </summary>
public enum TextNotice
{
    None,
    Saved,
    NotFound,
    Replaced,
    Failed,
}
