Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\artifacts\Keeply-win-x64"
!endif
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "..\artifacts\release"
!endif

!include "MUI2.nsh"

Name "Keeply ${APP_VERSION}"
OutFile "${OUTPUT_DIR}\Keeply-Setup-${APP_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\Keeply"
InstallDirRegKey HKCU "Software\Keeply" "InstallDir"
ShowInstDetails show
ShowUnInstDetails show

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Keeply"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey "FileDescription" "Keeply photo sorting app installer"
VIAddVersionKey "CompanyName" "Keeply"
VIAddVersionKey "LegalCopyright" "Copyright (c) Keeply"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\Keeply.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Keeply"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\Keeply"
  CreateShortcut "$SMPROGRAMS\Keeply\Keeply.lnk" "$INSTDIR\Keeply.exe" "" "$INSTDIR\Keeply.exe" 0
  CreateShortcut "$SMPROGRAMS\Keeply\Uninstall Keeply.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "Software\Keeply" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "DisplayName" "Keeply"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "Publisher" "Keeply"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "DisplayIcon" "$INSTDIR\Keeply.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\Keeply\Keeply.lnk"
  Delete "$SMPROGRAMS\Keeply\Uninstall Keeply.lnk"
  RMDir "$SMPROGRAMS\Keeply"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Keeply"
  DeleteRegKey HKCU "Software\Keeply"
  RMDir /r "$INSTDIR"
SectionEnd
