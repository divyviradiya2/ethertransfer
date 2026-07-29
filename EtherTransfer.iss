[Setup]
; App Information
AppName=EtherTransfer
AppVersion=1.0.0
AppVerName=EtherTransfer
AppPublisher=DS Labs
UninstallDisplayIcon={app}\EtherTransfer.exe

; Installation Directory
DefaultDirName={autopf}\EtherTransfer
DefaultGroupName=EtherTransfer
DisableProgramGroupPage=yes

; Output Settings
OutputDir=.\publish\installer
OutputBaseFilename=EtherTransfer_Setup_x64
SetupIconFile=.\EtherTransfer.UI\Assets\logo.ico

; HIGHEST COMPRESSION RATIO SETTINGS
Compression=lzma2/ultra64
SolidCompression=yes
LZMAAlgorithm=1

; Architecture (64-bit only)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Require admin rights to install to Program Files
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main executable
Source: "EtherTransfer.UI\publish\win-framework-dependent\EtherTransfer.exe"; DestDir: "{app}"; Flags: ignoreversion
; All other DLLs and files
Source: "EtherTransfer.UI\publish\win-framework-dependent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu icon
Name: "{group}\EtherTransfer"; Filename: "{app}\EtherTransfer.exe"
; Desktop icon
Name: "{autodesktop}\EtherTransfer"; Filename: "{app}\EtherTransfer.exe"; Tasks: desktopicon

[Run]
; Launch after install
Filename: "{app}\EtherTransfer.exe"; Description: "{cm:LaunchProgram,EtherTransfer}"; Flags: nowait postinstall skipifsilent


[Code]
var
  RequiresDotNet: Boolean;
  DownloadPage: TDownloadWizardPage;

function InitializeSetup(): Boolean;
begin
  Result := True;
  RequiresDotNet := False;
  
  // Check if .NET Desktop Runtime x64 is installed via registry. 
  // If this key doesn't exist, they definitely don't have it.
  if not RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    RequiresDotNet := True;
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // Trigger download right before the actual installation starts
  if (CurPageID = wpReady) and RequiresDotNet then
  begin
    DownloadPage.Clear;
    // URL to the .NET 10 Desktop Runtime x64 installer
    DownloadPage.Add('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', 'dotnet_installer.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        Result := True;
      except
        if DownloadPage.AbortedByUser then
          Log('Aborted by user.')
        else
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // Execute the downloaded .NET installer BEFORE we extract our app files
  if (CurStep = ssInstall) and RequiresDotNet then
  begin
    WizardForm.StatusLabel.Caption := 'Installing .NET 10 Desktop Runtime... This may take a minute.';
    // Set progress bar to infinite scrolling mode
    WizardForm.ProgressGauge.Style := npbstMarquee;
    try
      // Run the installer silently
      Exec(ExpandConstant('{tmp}\dotnet_installer.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
    finally
      // Reset progress bar back to normal
      WizardForm.ProgressGauge.Style := npbstNormal;
    end;
  end;
end;
