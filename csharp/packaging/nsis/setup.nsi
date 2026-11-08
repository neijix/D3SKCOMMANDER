; Windows setup for the self-contained publish (D3skCommander.exe and the native
; libraries beside it). `d3sk-release build` compiles this with makensis.
;
; The cargo-packager template packaging/nsis/installer.nsi is not compiled: it still
; carries the same settings-folder page, and this script is the one that ships.
;
; The page after the install folder asks where preferences go; the default is
; <install>\config. The choice is written as UTF-16LE (BOM) to
; $INSTDIR\config-location.txt, and as the relative word "config" when the default is
; kept, so the install folder can move. An existing pointer is left alone, and the
; page is skipped. Silent (/S) and passive (/P) skip the page and record the default.
; /NS skips the shortcuts. /R starts the program after a silent or passive install.
; /D=<folder> (last on the command line) chooses the install folder; without it the
; folder recorded in HKLM, then in HKCU, then %LOCALAPPDATA%\d3skcommander is used.
; Silent mode never creates a Desktop shortcut; the Finish page offers one.
; A silent update waits at most 30 s for the running program to be released, then
; exits with code 2 and leaves the old install alone (with /R it starts again).
;
; Elevation. The setup itself stays RequestExecutionLevel user, so the app can start
; it directly. When the install folder (its nearest existing parent) is not writable,
; the setup starts $EXEPATH again through the "runas" verb with /S /ELEVATED, the
; user's SID, the settings folder, the Start-menu folder, /NS if given and /D= last,
; waits for it and maps its exit code: 0 done, 2 app still running, 3 UAC declined.
; The elevated child installs per machine (HKLM, all-users Start menu, Modify on
; <install>\config for that SID). Only the unelevated parent starts the program
; (/R, Finish page), so the app never runs elevated. A folder the user can write is
; installed per user (HKCU), as before. The uninstaller of a per-machine install
; elevates the same way. The uninstaller removes only the files it installed. It
; leaves the pointer and the settings folder (RMDir is not recursive).
;
; Command-line defines: VERSION, VERSIONQUAD, OUTFILE, ICON, LICENSE, FILELIST, ESTIMATEDSIZE.

!ifndef OUTFILE
  !error "Compile csharp/packaging/nsis/setup.nsi with d3sk-release build"
!endif

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user
ManifestDPIAware true

!include MUI2.nsh
!include FileFunc.nsh
!include LogicLib.nsh
!insertmacro GetParent
!insertmacro un.GetParent
!insertmacro un.GetOptions

!define PRODUCT "d3skcommander"
!define PUBLISHER "neijix"
!define EXE "D3skCommander.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}"
!define APPKEY "Software\${PUBLISHER}\${PRODUCT}"
!define FLAG_SILENT "/S"
!define WAIT_STEP_MS 500
!define WAIT_MAX_MS 30000

Name "${PRODUCT}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\${PRODUCT}"
BrandingText "${PUBLISHER}"
ShowInstDetails show
AllowRootDirInstall false

VIProductVersion "${VERSIONQUAD}"
VIAddVersionKey "ProductName" "${PRODUCT}"
VIAddVersionKey "FileDescription" "Two-panel file manager"
VIAddVersionKey "LegalCopyright" "${PUBLISHER}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING

!include "${FILELIST}"

Var ConfigDir
Var StartMenuFolder
Var PassiveMode
Var Elevated
Var NeedElevate
Var Machine
Var UserSid

!macro SkipPageWhenPassive
  !define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfPassive
!macroend

; Probe: can a file be created in the nearest existing parent of $R0? Out: $R1 = 1/0.
!macro ProbeWritableFunc P
Function ${P}ProbeWritable
  Push $R2
  Push $R3
  ${Do}
    ${If} ${FileExists} "$R0\*.*"
      ${Break}
    ${EndIf}
    ${${P}GetParent} $R0 $R2
    ${If} $R2 == ""
    ${OrIf} $R2 == $R0
      StrCpy $R1 0
      Goto probeDone
    ${EndIf}
    StrCpy $R0 $R2
  ${Loop}
  ClearErrors
  StrCpy $R2 "$R0\d3sk-write-test.tmp"
  FileOpen $R3 $R2 w
  ${If} ${Errors}
    StrCpy $R1 0
  ${Else}
    FileClose $R3
    Delete $R2
    StrCpy $R1 1
  ${EndIf}
  probeDone:
  Pop $R3
  Pop $R2
FunctionEnd
!macroend

; Start $EXEPATH again as administrator ("runas") with the parameters in $R0 and wait.
; Out: $R1 = 1 when the child ran (0 when the prompt was declined), $R2 = its exit
; code, 3 when declined. Plain ShellExecuteExW through the bundled System plug-in.
!macro RunElevatedFunc P
Function ${P}RunElevated
  Push $0
  Push $3
  StrCpy $R1 $EXEPATH
  StrCpy $R2 3
  System::Call '*(i 60, i 0x40, i 0, t "runas", t R1, t R0, t 0, i 1, i 0, i 0, i 0, i 0, i 0, i 0, i 0) p .r0'
  System::Call 'shell32::ShellExecuteExW(p r0) i .r3'
  ${If} $3 == 0
    StrCpy $R1 0
    StrCpy $R2 3
  ${Else}
    System::Call '*$0(i, i, i, t, t, t, t, i, i, i, i, i, i, i, p .r3)'
    System::Call 'kernel32::WaitForSingleObject(p r3, i -1)'
    System::Call 'kernel32::GetExitCodeProcess(p r3, *i .R2)'
    System::Call 'kernel32::CloseHandle(p r3)'
    StrCpy $R1 1
  ${EndIf}
  System::Free $0
  Pop $3
  Pop $0
FunctionEnd
!macroend

!insertmacro ProbeWritableFunc ""
!insertmacro ProbeWritableFunc "un."
!insertmacro RunElevatedFunc ""
!insertmacro RunElevatedFunc "un."

!insertmacro SkipPageWhenPassive
!insertmacro MUI_PAGE_WELCOME

!insertmacro SkipPageWhenPassive
!insertmacro MUI_PAGE_LICENSE "${LICENSE}"

!insertmacro SkipPageWhenPassive
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirLeave
!insertmacro MUI_PAGE_DIRECTORY

; Settings folder. Default <install>\config, skipped when a pointer already exists.
!define MUI_PAGE_CUSTOMFUNCTION_PRE ConfigDirPre
!define MUI_DIRECTORYPAGE_VARIABLE $ConfigDir
!define MUI_PAGE_HEADER_TEXT "Settings folder"
!define MUI_PAGE_HEADER_SUBTEXT "Where ${PRODUCT} keeps your preferences."
!define MUI_DIRECTORYPAGE_TEXT_TOP "${PRODUCT} keeps your preferences, its password store and its operation log in this folder. You can change it later in Configuration > Settings folder."
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "Settings folder"
!insertmacro MUI_PAGE_DIRECTORY

!insertmacro SkipPageWhenPassive
!define MUI_STARTMENUPAGE_DEFAULTFOLDER "${PRODUCT}"
!insertmacro MUI_PAGE_STARTMENU Application $StartMenuFolder

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_NOAUTOCLOSE
!define MUI_FINISHPAGE_SHOWREADME
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Create a desktop shortcut"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortcut
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!insertmacro SkipPageWhenPassive
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  SetShellVarContext current
  SetRegView 64
  StrCpy $PassiveMode 0
  StrCpy $Elevated 0
  StrCpy $NeedElevate 0
  StrCpy $Machine 0
  StrCpy $UserSid ""
  StrCpy $StartMenuFolder "${PRODUCT}"
  ${GetOptions} $CMDLINE "/P" $0
  IfErrors +2 0
    StrCpy $PassiveMode 1

  ; Folder order: /D= (NSIS has already put it in $INSTDIR, which then differs from
  ; the built-in default), then InstallLocation in HKLM (64-bit view), then in HKCU.
  ${If} $INSTDIR == "$LOCALAPPDATA\${PRODUCT}"
    ReadRegStr $R0 HKLM "${APPKEY}" "InstallLocation"
    ${If} $R0 == ""
      ReadRegStr $R0 HKCU "${APPKEY}" "InstallLocation"
    ${EndIf}
    ${If} $R0 != ""
      StrCpy $INSTDIR $R0
    ${EndIf}
  ${EndIf}

  ClearErrors
  ${GetOptions} $CMDLINE "/ELEVATED" $0
  ${IfNot} ${Errors}
    ; The elevated child: per machine, values handed over by the parent.
    StrCpy $Elevated 1
    StrCpy $Machine 1
    SetShellVarContext all
    ${GetOptions} $CMDLINE "/SID=" $UserSid
    ${GetOptions} $CMDLINE "/CFG=" $ConfigDir
    ${GetOptions} $CMDLINE "/SMF=" $R0
    ${If} $R0 != ""
      StrCpy $StartMenuFolder $R0
    ${EndIf}
  ${ElseIf} ${Silent}
  ${OrIf} $PassiveMode == 1
    ; No folder page: probe now. The wizard probes in DirLeave.
    StrCpy $R0 $INSTDIR
    Call ProbeWritable
    ${If} $R1 != 1
      StrCpy $NeedElevate 1
    ${EndIf}
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext current
  StrCpy $Machine 0
  StrCpy $Elevated 0
  ClearErrors
  ${un.GetOptions} $CMDLINE "/ELEVATED" $R9
  ${IfNot} ${Errors}
    StrCpy $Elevated 1
  ${EndIf}
  ; A per-machine install: the HKLM key is present, or the folder is not writable.
  ReadRegStr $R0 HKLM "${UNINSTKEY}" "InstallLocation"
  ${If} $R0 != ""
    StrCpy $Machine 1
  ${EndIf}
  StrCpy $R0 $INSTDIR
  Call un.ProbeWritable
  ${If} $R1 != 1
    StrCpy $Machine 1
  ${EndIf}
  ${If} $Machine == 1
    SetShellVarContext all
    ${If} $R1 != 1
    ${AndIf} $Elevated != 1
      StrCpy $R0 '/ELEVATED _?=$INSTDIR'
      ${If} ${Silent}
        StrCpy $R0 '${FLAG_SILENT} /ELEVATED _?=$INSTDIR'
      ${EndIf}
      Call un.RunElevated
      ${If} $R1 == 0
        SetErrorLevel 3
      ${Else}
        SetErrorLevel $R2
      ${EndIf}
      Quit
    ${EndIf}
  ${EndIf}
FunctionEnd

Function SkipIfPassive
  ${If} $PassiveMode == 1
    Abort
  ${EndIf}
FunctionEnd

; Directory page left: a folder the user cannot write needs the elevated child.
Function DirLeave
  StrCpy $NeedElevate 0
  ${If} $Elevated != 1
    StrCpy $R0 $INSTDIR
    Call ProbeWritable
    ${If} $R1 != 1
      StrCpy $NeedElevate 1
    ${EndIf}
  ${EndIf}
FunctionEnd

Function ConfigDirPre
  ${If} $PassiveMode == 1
    Abort
  ${EndIf}
  ${If} ${FileExists} "$INSTDIR\config-location.txt"
    Abort
  ${EndIf}
  ${If} $ConfigDir == ""
    StrCpy $ConfigDir "$INSTDIR\config"
  ${EndIf}
FunctionEnd

Function WriteSettingsPointer
  ${If} ${FileExists} "$INSTDIR\config-location.txt"
    Return
  ${EndIf}
  ${If} $ConfigDir == ""
    StrCpy $ConfigDir "$INSTDIR\config"
  ${EndIf}
  CreateDirectory "$ConfigDir"
  FileOpen $0 "$INSTDIR\config-location.txt" w
  FileWriteWord $0 0xFEFF
  ${If} $ConfigDir == "$INSTDIR\config"
    FileWriteUTF16LE $0 "config$\r$\n"
  ${Else}
    FileWriteUTF16LE $0 "$ConfigDir$\r$\n"
  ${EndIf}
  FileClose $0
FunctionEnd

Function CreateDesktopShortcut
  CreateShortcut "$DESKTOP\${PRODUCT}.lnk" "$INSTDIR\${EXE}"
FunctionEnd

Function CreateStartMenuShortcut
  CreateDirectory "$SMPROGRAMS\$StartMenuFolder"
  CreateShortcut "$SMPROGRAMS\$StartMenuFolder\${PRODUCT}.lnk" "$INSTDIR\${EXE}"
FunctionEnd

; The user's SID, read before elevation (the child's own token may be another account).
Function ReadUserSid
  Push $R2
  Push $R3
  Push $R4
  Push $R5
  Push $R6
  StrCpy $UserSid ""
  System::Call 'kernel32::GetCurrentProcess() p .R2'
  System::Call 'advapi32::OpenProcessToken(p R2, i 8, *p .R3) i .R6'
  ${If} $R6 != 0
    System::Call 'advapi32::GetTokenInformation(p R3, i 1, p 0, i 0, *i .R4) i .R6'
    System::Alloc $R4
    Pop $R5
    System::Call 'advapi32::GetTokenInformation(p R3, i 1, p R5, i R4, *i .R4) i .R6'
    ${If} $R6 != 0
      System::Call '*$R5(p .R2)'
      System::Call 'advapi32::ConvertSidToStringSidW(p R2, *p .R4) i .R6'
      ${If} $R6 != 0
        System::Call '*$R4(&w${NSIS_MAX_STRLEN} .R2)'
        StrCpy $UserSid $R2
        System::Call 'kernel32::LocalFree(p R4)'
      ${EndIf}
    ${EndIf}
    System::Free $R5
    System::Call 'kernel32::CloseHandle(p R3)'
  ${EndIf}
  Pop $R6
  Pop $R5
  Pop $R4
  Pop $R3
  Pop $R2
FunctionEnd

; Start the old version again after a failed update (only with /R).
Function RestartOld
  ClearErrors
  ${GetOptions} $CMDLINE "/R" $R9
  ${IfNot} ${Errors}
  ${AndIf} ${FileExists} "$INSTDIR\${EXE}"
    Exec '"$INSTDIR\${EXE}"'
  ${EndIf}
FunctionEnd

; The folder is not writable: run the elevated child and map its exit code.
Function ElevateAndRelay
  Call ReadUserSid
  StrCpy $R0 '/S /ELEVATED /SID=$UserSid'
  ${If} $ConfigDir != ""
    StrCpy $R0 '$R0 /CFG="$ConfigDir"'
  ${EndIf}
  ${If} $StartMenuFolder != ""
    StrCpy $R0 '$R0 /SMF="$StartMenuFolder"'
  ${EndIf}
  ClearErrors
  ${GetOptions} $CMDLINE "/NS" $R1
  ${IfNot} ${Errors}
    StrCpy $R0 '$R0 /NS'
  ${EndIf}
  StrCpy $R0 '$R0 /D=$INSTDIR'
  Call RunElevated
  ${Switch} $R2
    ${Case} 0
      Return
    ${Case} 2
      Call RestartOld
      SetErrorLevel 2
      Quit
    ${Case} 3
      Call RestartOld
      SetErrorLevel 3
      Quit
    ${Default}
      Call RestartOld
      SetErrorLevel 1
      Quit
  ${EndSwitch}
FunctionEnd

; Silent update: wait for the running program to be released (30 s, every 500 ms).
Function WaitForApp
  ${IfNot} ${Silent}
    Return
  ${EndIf}
  ${IfNot} ${FileExists} "$INSTDIR\${EXE}"
    Return
  ${EndIf}
  Push $R0
  Push $R1
  StrCpy $R0 0
  ${Do}
    ClearErrors
    FileOpen $R1 "$INSTDIR\${EXE}" a
    ${IfNot} ${Errors}
      FileClose $R1
      ${Break}
    ${EndIf}
    ${If} $R0 >= ${WAIT_MAX_MS}
      Call RestartOld
      SetErrorLevel 2
      Quit
    ${EndIf}
    Sleep ${WAIT_STEP_MS}
    IntOp $R0 $R0 + ${WAIT_STEP_MS}
  ${Loop}
  Pop $R1
  Pop $R0
FunctionEnd

Function WriteKeysUser
  WriteRegStr HKCU "${APPKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${PRODUCT}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$\"$INSTDIR\${EXE}$\""
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" ${ESTIMATEDSIZE}
FunctionEnd

Function WriteKeysMachine
  SetRegView 64
  WriteRegStr HKLM "${APPKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${PRODUCT}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$\"$INSTDIR\${EXE}$\""
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "EstimatedSize" ${ESTIMATEDSIZE}
FunctionEnd

Function WriteStartMenuKey
  ${If} $Machine == 1
    Call WriteStartMenuKeyMachine
  ${Else}
    WriteRegStr HKCU "${APPKEY}" "StartMenu" "$StartMenuFolder"
  ${EndIf}
FunctionEnd

Function WriteStartMenuKeyMachine
  SetRegView 64
  WriteRegStr HKLM "${APPKEY}" "StartMenu" "$StartMenuFolder"
FunctionEnd

Section Install
  ${If} $NeedElevate == 1
    Call ElevateAndRelay
    Return
  ${EndIf}
  Call WaitForApp

  SetOutPath "$INSTDIR"
  Call WriteSettingsPointer
  !insertmacro InstallPublishedFiles
  WriteUninstaller "$INSTDIR\uninstall.exe"

  ${If} $Elevated == 1
    CreateDirectory "$INSTDIR\config"
    ${If} $UserSid != ""
      nsExec::ExecToLog 'icacls "$INSTDIR\config" /grant *$UserSid:(OI)(CI)M'
      Pop $0
    ${EndIf}
    Call WriteKeysMachine
  ${Else}
    Call WriteKeysUser
  ${EndIf}

  ; /NS on a silent or passive install: no shortcuts. The ">" prefix is what
  ; MUI_STARTMENU_WRITE_BEGIN treats as "the user declined the start menu entry".
  ; A silent install never makes a Desktop shortcut; a passive one does, as before.
  ${If} ${Silent}
  ${OrIf} $PassiveMode == 1
    ClearErrors
    ${GetOptions} $CMDLINE "/NS" $0
    ${IfNot} ${Errors}
      StrCpy $StartMenuFolder ">"
    ${Else}
      ${IfNot} ${Silent}
        Call CreateDesktopShortcut
      ${EndIf}
    ${EndIf}
  ${EndIf}
  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    Call CreateStartMenuShortcut
  !insertmacro MUI_STARTMENU_WRITE_END

  StrCpy $0 $StartMenuFolder 1
  ${If} $0 != ">"
  ${AndIf} $StartMenuFolder != ""
    Call WriteStartMenuKey
  ${EndIf}

  ${If} $PassiveMode == 1
    SetAutoClose true
  ${EndIf}
SectionEnd

; Only the unelevated parent starts the program; the elevated child never gets /R.
Function .onInstSuccess
  ${If} $Elevated != 1
    ${If} ${Silent}
    ${OrIf} $PassiveMode == 1
      ClearErrors
      ${GetOptions} $CMDLINE "/R" $0
      ${IfNot} ${Errors}
        Exec '"$INSTDIR\${EXE}"'
      ${EndIf}
    ${EndIf}
  ${EndIf}
FunctionEnd

Section Uninstall
  SetRegView 64
  ${If} $Machine == 1
    ReadRegStr $0 HKLM "${APPKEY}" "StartMenu"
  ${Else}
    ReadRegStr $0 HKCU "${APPKEY}" "StartMenu"
  ${EndIf}

  !insertmacro UninstallPublishedFiles
  Delete "$INSTDIR\uninstall.exe"

  ${If} $0 != ""
    Delete "$SMPROGRAMS\$0\${PRODUCT}.lnk"
    RMDir "$SMPROGRAMS\$0"
  ${EndIf}
  Delete "$DESKTOP\${PRODUCT}.lnk"

  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "${APPKEY}"
  ${If} $Machine == 1
    DeleteRegKey HKLM "${UNINSTKEY}"
    DeleteRegKey HKLM "${APPKEY}"
  ${EndIf}
  ; config-location.txt and the settings folder stay (this RMDir is not recursive).
  RMDir "$INSTDIR"
SectionEnd
