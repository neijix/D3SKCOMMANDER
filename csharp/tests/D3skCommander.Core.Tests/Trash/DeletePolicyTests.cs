using D3skCommander.Core.Commands;
using D3skCommander.Core.Trash;
using Xunit;

namespace D3skCommander.Core.Tests.Trash;

/// <summary>A6: trash, permanent, and confirmation are decided in Core.</summary>
public class DeletePolicyTests
{
    [Fact]
    public void UseTrashSendsDeleteToTheTrash()
    {
        var decision = DeletePolicy.Decide(new DeleteSettings(), CommandId.Delete);

        Assert.Equal(DeleteMode.Trash, decision.Mode);
        Assert.True(decision.GoesToTrash);
        Assert.Equal(DeletePolicy.TrashPromptKey, decision.PromptKey);
        Assert.True(decision.NeedsConfirmation);
    }

    [Fact]
    public void TurningTrashOffDeletesPermanently()
    {
        var settings = new DeleteSettings(UseTrash: false, ConfirmDelete: true);

        var decision = DeletePolicy.Decide(settings, CommandId.Delete);

        Assert.Equal(DeleteMode.Permanent, decision.Mode);
        Assert.Equal(DeletePolicy.PermanentPromptKey, decision.PromptKey);
        Assert.True(decision.NeedsConfirmation);
    }

    [Fact]
    public void ConfirmDeleteAsksFirst()
    {
        var settings = new DeleteSettings(UseTrash: true, ConfirmDelete: true);

        var decision = DeletePolicy.Decide(settings, CommandId.Delete);

        Assert.True(decision.NeedsConfirmation);
        Assert.Equal(DeleteMode.Trash, decision.Mode);
    }

    [Fact]
    public void ConfirmOffDoesNotAsk()
    {
        var trash = DeletePolicy.Decide(new DeleteSettings(UseTrash: true, ConfirmDelete: false), CommandId.Delete);
        var permanent = DeletePolicy.Decide(new DeleteSettings(UseTrash: false, ConfirmDelete: false), CommandId.Delete);

        Assert.False(trash.NeedsConfirmation);
        Assert.Equal(DeleteMode.Trash, trash.Mode);
        Assert.False(permanent.NeedsConfirmation);
        Assert.Equal(DeleteMode.Permanent, permanent.Mode);
    }

    [Fact]
    public void DeletePermanentAlwaysDeletesPermanently()
    {
        var shiftF8 = new Gesture(GestureModifiers.Shift, GestureKey.F8);
        var shiftDel = new Gesture(GestureModifiers.Shift, GestureKey.Delete);
        Assert.Equal(CommandId.DeletePermanent, CommandCatalog.Match(shiftF8));
        Assert.Equal(CommandId.DeletePermanent, CommandCatalog.Match(shiftDel));

        var asking = new DeleteSettings(UseTrash: true, ConfirmDelete: true);
        var quiet = new DeleteSettings(UseTrash: true, ConfirmDelete: false);

        var asked = DeletePolicy.Decide(asking, CommandId.DeletePermanent);
        var silent = DeletePolicy.Decide(quiet, CommandId.DeletePermanent);

        Assert.Equal(DeleteMode.Permanent, asked.Mode);
        Assert.Equal(DeletePolicy.PermanentPromptKey, asked.PromptKey);
        Assert.True(asked.NeedsConfirmation);
        Assert.Equal(DeleteMode.Permanent, silent.Mode);
        Assert.False(silent.NeedsConfirmation);
    }

    [Fact]
    public void TrashUnavailableOffersAPermanentDelete()
    {
        var settings = new DeleteSettings(UseTrash: true, ConfirmDelete: false);

        var decision = DeletePolicy.OfferPermanent(settings);

        Assert.Equal(DeleteMode.Permanent, decision.Mode);
        Assert.Equal(DeletePolicy.PermanentPromptKey, decision.PromptKey);
        Assert.True(decision.NeedsConfirmation);
    }

    [Fact]
    public void DontAskAgainSkipsTheMissingTrashPrompt()
    {
        var settings = new DeleteSettings(UseTrash: true, ConfirmDelete: true, ConfirmTrashFallback: false);

        var decision = DeletePolicy.OfferPermanent(settings);

        Assert.Equal(DeleteMode.Permanent, decision.Mode);
        Assert.False(decision.NeedsConfirmation);
    }

    [Theory]
    [InlineData(0, TrashFallbackAnswer.Delete)]
    [InlineData(1, TrashFallbackAnswer.DeleteAndStopAsking)]
    [InlineData(null, TrashFallbackAnswer.Cancel)]
    [InlineData(7, TrashFallbackAnswer.Cancel)]
    public void TrashFallbackChoicesMapBack(int? choice, TrashFallbackAnswer expected)
    {
        Assert.Equal(2, TrashFallback.ChoiceKeys.Count);
        Assert.Equal(expected, TrashFallback.Map(choice));
    }

    [Fact]
    public void OnlyDeleteCommandsAreDecided()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => DeletePolicy.Decide(new DeleteSettings(), CommandId.Copy));

        Assert.Equal("command", error.ParamName);
    }
}
