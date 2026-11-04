namespace D3skCommander.App.Dialogs;

/// <summary>What the user answered. A cancel leaves <see cref="Accepted"/> false.</summary>
public sealed class DialogResult
{
    private DialogResult(bool accepted, string? text, int? choice)
    {
        Accepted = accepted;
        Text = text;
        Choice = choice;
    }

    public bool Accepted { get; }

    public string? Text { get; }

    public int? Choice { get; }

    public static DialogResult Ok(string? text = null) => new(true, text, null);

    public static DialogResult Cancel() => new(false, null, null);

    public static DialogResult Picked(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "A choice index cannot be negative.");
        }

        return new DialogResult(true, null, index);
    }
}
