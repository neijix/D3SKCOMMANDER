using System.Text.RegularExpressions;
using Xunit;

namespace D3skCommander.Core.Tests.Packaging;

public class NsisTemplateTests
{
    private static string Script() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "packaging", "setup.nsi"));

    // The text of one Function/Section block, from its header line to the next header.
    private static string Block(string text, string header)
    {
        var start = text.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing block: {header}");
        var next = Regex.Match(text[(start + header.Length)..], @"^(Function|Section|!macro) ", RegexOptions.Multiline);
        return next.Success ? text.Substring(start, header.Length + next.Index) : text[start..];
    }

    private static void AssertOrder(string text, params string[] parts)
    {
        var at = -1;
        foreach (var part in parts)
        {
            var found = text.IndexOf(part, at + 1, StringComparison.Ordinal);
            Assert.True(found > at, $"'{part}' missing or out of order");
            at = found;
        }
    }

    [Fact]
    public void KeepsTheSettingsFolderPageAndItsDefault()
    {
        var text = Script();
        Assert.Contains("Settings folder", text, StringComparison.Ordinal);
        Assert.Contains("MUI_DIRECTORYPAGE_VARIABLE $ConfigDir", text, StringComparison.Ordinal);
        Assert.Contains("$INSTDIR\\config", text, StringComparison.Ordinal);
        Assert.Contains("config-location.txt", text, StringComparison.Ordinal);
        Assert.Contains("FileWriteUTF16LE $0 \"config$\\r$\\n\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheSevenDefinesAndTheTwoKeys()
    {
        var text = Script();
        foreach (var define in new[] { "VERSION", "VERSIONQUAD", "OUTFILE", "ICON", "LICENSE", "FILELIST", "ESTIMATEDSIZE" })
            Assert.Contains("${" + define + "}", text, StringComparison.Ordinal);
        Assert.Contains("!insertmacro InstallPublishedFiles", text, StringComparison.Ordinal);
        Assert.Contains("!insertmacro UninstallPublishedFiles", text, StringComparison.Ordinal);
        Assert.Contains("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${PRODUCT}", text, StringComparison.Ordinal);
        Assert.Contains("Software\\${PUBLISHER}\\${PRODUCT}", text, StringComparison.Ordinal);
        Assert.Contains("WriteRegStr HKCU \"${UNINSTKEY}\"", text, StringComparison.Ordinal);
        Assert.Contains("WriteRegStr HKLM \"${UNINSTKEY}\"", text, StringComparison.Ordinal);
        Assert.Contains("WriteRegStr HKCU \"${APPKEY}\"", text, StringComparison.Ordinal);
        Assert.Contains("WriteRegStr HKLM \"${APPKEY}\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowsTheFiveFlags()
    {
        var text = Script();
        foreach (var flag in new[] { "\"/S\"", "\"/R\"", "\"/NS\"", "\"/P\"" })
            Assert.Contains(flag, text, StringComparison.Ordinal);
        Assert.Contains("/D=", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorNamesTheReleaseTool()
    {
        var text = Script();
        Assert.Contains("!error", text, StringComparison.Ordinal);
        Assert.Contains("d3sk-release build", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Compile csharp/packaging/nsis/setup.nsi with csharp/packaging/setup-windows.sh", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SilentUpdateWaitsThirtySecondsEveryHalfSecondThenExitsTwo()
    {
        var text = Script();
        Assert.Contains("!define WAIT_STEP_MS 500", text, StringComparison.Ordinal);
        Assert.Contains("!define WAIT_MAX_MS 30000", text, StringComparison.Ordinal);
        var wait = Block(text, "Function WaitForApp");
        Assert.Contains("${Silent}", wait, StringComparison.Ordinal);
        Assert.Contains("Sleep ${WAIT_STEP_MS}", wait, StringComparison.Ordinal);
        Assert.Contains("SetErrorLevel 2", wait, StringComparison.Ordinal);
        Assert.Contains("Call RestartOld", wait, StringComparison.Ordinal);
        // The wait runs before any file is written.
        var install = Block(text, "Section Install");
        AssertOrder(install, "Call WaitForApp", "!insertmacro InstallPublishedFiles");
    }

    [Fact]
    public void StaysAnUnelevatedProcessAndProbesTheNearestExistingParent()
    {
        var text = Script();
        Assert.Contains("RequestExecutionLevel user", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestExecutionLevel admin", text, StringComparison.Ordinal);
        var probe = Block(text, "Function ${P}ProbeWritable");
        Assert.Contains("GetParent}", probe, StringComparison.Ordinal);
        Assert.Contains("FileOpen", probe, StringComparison.Ordinal);
        Assert.Contains("Delete", probe, StringComparison.Ordinal);
        Assert.Contains("Call ProbeWritable", Block(text, "Function DirLeave"), StringComparison.Ordinal);
        Assert.Contains("MUI_PAGE_CUSTOMFUNCTION_LEAVE DirLeave", text, StringComparison.Ordinal);
        Assert.Contains("Call ProbeWritable", Block(text, "Function .onInit"), StringComparison.Ordinal);
    }

    [Fact]
    public void RelaunchesThroughRunasWithEveryArgumentAndDirectoryLast()
    {
        var text = Script();
        var run = Block(text, "Function ${P}RunElevated");
        Assert.Contains("\"runas\"", run, StringComparison.Ordinal);
        Assert.Contains("ShellExecuteExW", run, StringComparison.Ordinal);
        Assert.Contains("WaitForSingleObject", run, StringComparison.Ordinal);
        Assert.Contains("GetExitCodeProcess", run, StringComparison.Ordinal);
        var relay = Block(text, "Function ElevateAndRelay");
        foreach (var arg in new[] { "/S ", "/ELEVATED", "/SID=$UserSid", "/CFG=", "/SMF=", "/NS" })
            Assert.Contains(arg, relay, StringComparison.Ordinal);
        Assert.Contains("Call ReadUserSid", relay, StringComparison.Ordinal);
        // The /D= argument is appended last, then the call.
        Assert.Matches(@"StrCpy \$R0 '\$R0 /D=\$INSTDIR'\s+Call RunElevated", relay);
        Assert.Single(Regex.Matches(relay, @"/D="));
    }

    [Fact]
    public void MapsTheChildExitCodes()
    {
        var text = Script();
        var relay = Block(text, "Function ElevateAndRelay");
        Assert.Contains("${Case} 0", relay, StringComparison.Ordinal);
        Assert.Contains("${Case} 2", relay, StringComparison.Ordinal);
        Assert.Contains("${Case} 3", relay, StringComparison.Ordinal);
        Assert.Contains("SetErrorLevel 2", relay, StringComparison.Ordinal);
        Assert.Contains("SetErrorLevel 3", relay, StringComparison.Ordinal);
        // A declined prompt reports 3 before any file is touched.
        var run = Block(text, "Function ${P}RunElevated");
        Assert.Contains("StrCpy $R2 3", run, StringComparison.Ordinal);
        var install = Block(text, "Section Install");
        AssertOrder(install, "Call ElevateAndRelay", "Return", "Call WaitForApp");
    }

    [Fact]
    public void OnlyTheUnelevatedParentLaunchesTheApp()
    {
        var text = Script();
        var success = Block(text, "Function .onInstSuccess");
        Assert.Contains("$Elevated != 1", success, StringComparison.Ordinal);
        Assert.Contains("\"/R\"", success, StringComparison.Ordinal);
        Assert.Contains("Exec ", success, StringComparison.Ordinal);
        Assert.Contains("MUI_FINISHPAGE_RUN \"$INSTDIR\\${EXE}\"", text, StringComparison.Ordinal);
        // The only other launch is the old version after a failed update, also in the parent.
        Assert.Contains("\"/R\"", Block(text, "Function RestartOld"), StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(text, @"^\s*Exec '", RegexOptions.Multiline).Count);
        Assert.DoesNotContain("\"/R\"", Block(text, "Function ElevateAndRelay"), StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedChildIsPerMachineAndGrantsConfigToThePassedSid()
    {
        var text = Script();
        var onInit = Block(text, "Function .onInit");
        AssertOrder(onInit, "/ELEVATED", "StrCpy $Elevated 1", "SetShellVarContext all");
        var install = Block(text, "Section Install");
        Assert.Contains("icacls \"$INSTDIR\\config\" /grant *$UserSid:(OI)(CI)M", install, StringComparison.Ordinal);
        Assert.Contains("Call WriteKeysMachine", install, StringComparison.Ordinal);
        Assert.Contains("Call WriteKeysUser", install, StringComparison.Ordinal);
        Assert.Contains("SetShellVarContext current", onInit, StringComparison.Ordinal);
    }

    [Fact]
    public void SetsTheRegistryViewBeforeEveryHklmAccess()
    {
        var text = Script();
        var blocks = Regex.Split(text, @"^(?=(?:Function|Section) )", RegexOptions.Multiline);
        var checkedBlocks = 0;
        foreach (var block in blocks)
        {
            var hklm = Regex.Match(block, @"^\s*(?:Read|Write|Delete)\w*\s+(?:\S+\s+)?HKLM\b", RegexOptions.Multiline);
            if (!hklm.Success) continue;
            checkedBlocks++;
            var view = block.IndexOf("SetRegView 64", StringComparison.Ordinal);
            Assert.True(view >= 0 && view < hklm.Index, $"HKLM before SetRegView 64 in: {block.Split('\n')[0]}");
        }
        Assert.True(checkedBlocks >= 4, "installer and uninstaller touch HKLM");
        Assert.Contains("SetRegView 64", Block(text, "Function .onInit"), StringComparison.Ordinal);
        Assert.Contains("SetRegView 64", Block(text, "Function un.onInit"), StringComparison.Ordinal);
    }

    [Fact]
    public void PicksTheFolderFromDirectoryArgumentThenHklmThenHkcu()
    {
        var text = Script();
        var onInit = Block(text, "Function .onInit");
        AssertOrder(onInit, "/D=", "ReadRegStr $R0 HKLM", "ReadRegStr $R0 HKCU");
        Assert.DoesNotContain("InstallDirRegKey", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UninstallerDetectsPerMachineElevatesAndRemovesHklm()
    {
        var text = Script();
        var init = Block(text, "Function un.onInit");
        Assert.Contains("ReadRegStr $R0 HKLM", init, StringComparison.Ordinal);
        Assert.Contains("Call un.ProbeWritable", init, StringComparison.Ordinal);
        Assert.Contains("Call un.RunElevated", init, StringComparison.Ordinal);
        Assert.Contains("SetShellVarContext all", init, StringComparison.Ordinal);
        Assert.Contains("SetErrorLevel 3", init, StringComparison.Ordinal);
        var section = Block(text, "Section Uninstall");
        Assert.Contains("DeleteRegKey HKLM \"${UNINSTKEY}\"", section, StringComparison.Ordinal);
        Assert.Contains("DeleteRegKey HKLM \"${APPKEY}\"", section, StringComparison.Ordinal);
        Assert.Contains("DeleteRegKey HKCU \"${UNINSTKEY}\"", section, StringComparison.Ordinal);
        Assert.Contains("RMDir \"$INSTDIR\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("RMDir /r", section, StringComparison.Ordinal);
    }

    [Fact]
    public void SilentModeNeverCreatesADesktopShortcut()
    {
        var text = Script();
        var install = Block(text, "Section Install");
        Assert.Matches(@"\$\{IfNot\} \$\{Silent\}\s+Call CreateDesktopShortcut", install);
        Assert.Single(Regex.Matches(install, "Call CreateDesktopShortcut"));
        // The Finish page still offers the shortcut through its own button.
        Assert.Contains("MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortcut", text, StringComparison.Ordinal);
    }
}
