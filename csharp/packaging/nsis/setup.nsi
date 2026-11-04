; Windows setup for the self-contained publish (D3skCommander.exe and the native
; libraries beside it). csharp/packaging/setup-windows.sh compiles this with makensis.
;
; The cargo-packager template packaging/nsis/installer.nsi is not compiled: it still
; carries the same settings-folder page, and this script is the one that ships.
; Per user (no administrator). The page after the install folder asks where preferences
; go; the default is <install>\config. The choice is written as UTF-16LE (BOM) to
; $INSTDIR\config-location.txt, and as the relative word "config" when the default is
; kept, so the install folder can move. An existing pointer is left alone, and the
; page is skipped. Silent (/S) and passive (/P) skip the page and record the default.
; /NS skips the shortcuts. /R starts the program after a silent or passive install.
; The uninstaller removes only the files it installed. It leaves the pointer and the
; settings folder (RMDir is not recursive).
;
; Command-line defines: VERSION, VERSIONQUAD, OUTFILE, ICON, LICENSE, FILELIST, ESTIMATEDSIZE.

!ifndef OUTFILE
  !error "Compile csharp/packaging/nsis/setup.nsi with csharp/packaging/setup-windows.sh"
!endif

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user
ManifestDPIAware true

!include MUI2.nsh
!include FileFunc.nsh
!include LogicLib.nsh

!define PRODUCT "d3skcommander"
!define PUBLISHER "neijix"
!define EXE "D3skCommander.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}"
!define APPKEY "Software\${PUBLISHER}\${PRODUCT}"

Name "${PRODUCT}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\${PRODUCT}"
InstallDirRegKey HKCU "${APPKEY}" "InstallLocation"
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

!macro SkipPageWhenPassive
  !define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfPassive
!macroend

!insertmacro SkipPageWhenPassive
!insertmacro MUI_PAGE_WELCOME

!insertmacro SkipPageWhenPassive
!insertmacro MUI_PAGE_LICENSE "${LICENSE}"

!insertmacro SkipPageWhenPassive
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
  StrCpy $PassiveMode 0
  StrCpy $StartMenuFolder "${PRODUCT}"
  ${GetOptions} $CMDLINE "/P" $0
  IfErrors +2 0
    StrCpy $PassiveMode 1
FunctionEnd

Function un.onInit
  SetShellVarContext current
FunctionEnd

Function SkipIfPassive
  ${If} $PassiveMode == 1
    Abort
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

Section Install
  SetOutPath "$INSTDIR"
  Call WriteSettingsPointer
  !insertmacro InstallPublishedFiles
  WriteUninstaller "$INSTDIR\uninstall.exe"

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

  ; /NS on a silent or passive install: no shortcuts. The ">" prefix is what
  ; MUI_STARTMENU_WRITE_BEGIN treats as "the user declined the start menu entry".
  ${If} ${Silent}
  ${OrIf} $PassiveMode == 1
    ${GetOptions} $CMDLINE "/NS" $0
    ${IfNot} ${Errors}
      StrCpy $StartMenuFolder ">"
    ${Else}
      Call CreateDesktopShortcut
    ${EndIf}
  ${EndIf}
  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    Call CreateStartMenuShortcut
  !insertmacro MUI_STARTMENU_WRITE_END

  StrCpy $0 $StartMenuFolder 1
  ${If} $0 != ">"
  ${AndIf} $StartMenuFolder != ""
    WriteRegStr HKCU "${APPKEY}" "StartMenu" "$StartMenuFolder"
  ${EndIf}

  ${If} $PassiveMode == 1
    SetAutoClose true
  ${EndIf}
SectionEnd

Function .onInstSuccess
  ${If} ${Silent}
  ${OrIf} $PassiveMode == 1
    ${GetOptions} $CMDLINE "/R" $0
    ${IfNot} ${Errors}
      Exec '"$INSTDIR\${EXE}"'
    ${EndIf}
  ${EndIf}
FunctionEnd

Section Uninstall
  !insertmacro UninstallPublishedFiles
  Delete "$INSTDIR\uninstall.exe"

  ReadRegStr $0 HKCU "${APPKEY}" "StartMenu"
  ${If} $0 != ""
    Delete "$SMPROGRAMS\$0\${PRODUCT}.lnk"
    RMDir "$SMPROGRAMS\$0"
  ${EndIf}
  Delete "$DESKTOP\${PRODUCT}.lnk"

  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "${APPKEY}"
  ; config-location.txt and the settings folder stay (this RMDir is not recursive).
  RMDir "$INSTDIR"
SectionEnd
