#define MyAppName "TDN Control"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "TDN Media"
#define MyAppExeName "TDN_Control.exe"
#define MyAppBuildDir "Release_v1.0.5"

[Setup]
AppId={{93EBF5C6-BE9E-4E94-9CF2-CE153480FF05}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=© 2026 {#MyAppPublisher}
VersionInfoCompany={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}.0
; Cài theo người dùng, đúng thư mục mà TDN Control coi là "đã cài" (để tự cập nhật hoạt động).
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\TDN Control
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
OutputDir={#MyAppBuildDir}
OutputBaseFilename=TDN_Control_Setup_{#MyAppVersion}
SetupIconFile=Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt trên Desktop"; GroupDescription: "Lối tắt:"
Name: "startup"; Description: "Chạy cùng Windows (ẩn dưới khay)"; GroupDescription: "Khởi động:"; Flags: unchecked

[Files]
Source: "{#MyAppBuildDir}\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TDN Control"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Mở TDN Control"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillTDNControl"
