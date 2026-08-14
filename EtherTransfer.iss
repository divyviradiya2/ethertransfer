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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Main executable
Source: "publish\EtherTransfer.exe"; DestDir: "{app}"; Flags: ignoreversion
; All other DLLs and files
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu icon
Name: "{group}\EtherTransfer"; Filename: "{app}\EtherTransfer.exe"
; Desktop icon
Name: "{autodesktop}\EtherTransfer"; Filename: "{app}\EtherTransfer.exe"; Tasks: desktopicon

[Run]
; Add Firewall Rule for Private and Public networks (Silent)
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""EtherTransfer"" dir=in action=allow program=""{app}\EtherTransfer.exe"" enable=yes profile=private,public"; Flags: runhidden
; Launch after install
Filename: "{app}\EtherTransfer.exe"; Description: "{cm:LaunchProgram,EtherTransfer}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove Firewall Rule on uninstall (Silent)
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""EtherTransfer"" program=""{app}\EtherTransfer.exe"""; Flags: runhidden
