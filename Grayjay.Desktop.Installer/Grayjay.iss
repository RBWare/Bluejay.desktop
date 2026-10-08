[Setup]
AppId=Bluejay
AppName=Bluejay
AppVersion=1.0
AppVerName=Bluejay
AppPublisher=RBWare
VersionInfoCompany=RBWare
VersionInfoDescription=Bluejay Desktop Installer
VersionInfoVersion=1.0
AppCopyright=© 2025 FUTO

AppPublisherURL=https://futo.org
AppSupportURL=https://github.com/RBWare/Bluejay.desktop
AppUpdatesURL=https://github.com/RBWare/Bluejay.desktop
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=win64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
AppMutex=BluejayInstallerMutex
CloseApplications=yes

DefaultDirName={code:GetInstallDir}
DefaultGroupName=Bluejay
DisableProgramGroupPage=yes

LicenseFile=Metadata\LICENSE.rtf
WizardImageFile=Metadata\grayjay_background.bmp
WizardSmallImageFile=Metadata\grayjay.bmp
SetupIconFile=Metadata\grayjay.ico

Compression=lzma2
SolidCompression=yes
OutputBaseFilename=Bluejay-1.0.0-Setup-x64
OutputDir=Output
UninstallDisplayIcon={app}\Bluejay.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
Name: "{app}\cef"; Flags: uninsalwaysuninstall

[Files]
Source: "Files\FUTO.Updater.Client.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\UpdaterConfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\UpdaterOSConfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\launch"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\vc_redist.x64.exe"; Flags: dontcopy
Source: "Metadata\grayjay.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Bluejay"; Filename: "{app}\Bluejay.exe"; IconFilename: "{app}\grayjay.ico"; WorkingDir: "{app}"; Comment: "Bluejay Desktop"
Name: "{autodesktop}\Bluejay"; Filename: "{app}\Bluejay.exe"; IconFilename: "{app}\grayjay.ico"; WorkingDir: "{app}"; Comment: "Bluejay Desktop"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\RBWare\Bluejay"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue; Check: IsAdminInstallMode
Root: HKCU; Subkey: "Software\RBWare\Bluejay"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue; Check: not IsAdminInstallMode

[Run]
Filename: "{app}\FUTO.Updater.Client.exe"; Parameters: "install"; Flags: waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function GetInstallDir(Param: string): string;
var
  PrevDir: string;
  ProgFiles: string;
  LocalProg: string;
begin
  ProgFiles := ExpandConstant('{pf64}\Bluejay');
  LocalProg := ExpandConstant('{localappdata}\Programs\Bluejay');

  { Existing machine‑wide install? }
  if RegQueryStringValue(HKLM, 'Software\RBWare\Bluejay', 'InstallLocation', PrevDir) and DirExists(PrevDir) then
  begin
    Result := PrevDir;
    exit;
  end;

  { Existing per‑user install? }
  if RegQueryStringValue(HKCU, 'Software\RBWare\Bluejay', 'InstallLocation', PrevDir) and DirExists(PrevDir) then
  begin
    Result := PrevDir;
    exit;
  end;

  { Fresh install }
  if IsAdminInstallMode then
    Result := ProgFiles
  else
    Result := LocalProg;
end;

const VC14_X64_KEY = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
function VcRedist14X64Installed: Boolean;
var
  Installed: Cardinal;
  Version: string;
begin
  if RegQueryDWordValue(HKLM64, VC14_X64_KEY, 'Installed', Installed) then
    Result := (Installed = 1)
  else if RegQueryStringValue(HKLM64, VC14_X64_KEY, 'Version', Version) then
    Result := (Version <> '')
  else
    Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ResultCode: Integer;
  ExePath: string;
begin
  Result := '';

  if VcRedist14X64Installed then
    Exit;

  ExtractTemporaryFile('vc_redist.x64.exe');
  ExePath := ExpandConstant('{tmp}\vc_redist.x64.exe');

  if not Exec(ExePath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Failed to launch Microsoft Visual C++ Redistributable (x64) installer.';
    Exit;
  end;

  if (ResultCode = 0) or (ResultCode = 1638) then
  begin
    if not VcRedist14X64Installed then
    begin
      Result := 'Microsoft Visual C++ Redistributable (x64) did not appear to install correctly.';
      Exit;
    end;
    Exit;
  end
  else if ResultCode = 3010 then
  begin
    NeedsRestart := True;
    if not VcRedist14X64Installed then
    begin
      Result := 'Microsoft Visual C++ Redistributable (x64) requires a restart to complete installation.';
      Exit;
    end;
    Exit;
  end
  else
    Result := Format('Microsoft Visual C++ Redistributable (x64) installation failed (exit code %d).', [ResultCode]);
end;
