; مثبّت كاشف (Inno Setup 6) — يُبنى تلقائيًا في GitHub Actions
; iscc /DAppVersion=1.0.0 /DExeDir=..\publish installer\Kashif.iss
; التثبيت للمستخدم الحالي بلا صلاحيات مدير: البرنامج في %LOCALAPPDATA%\Programs\Kashif
; وبياناته (قاعدة الفحوصات والإعدادات) في %LOCALAPPDATA%\Kashif — لا يحذفها إلغاء التثبيت.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef ExeDir
  #define ExeDir "..\publish"
#endif

[Setup]
AppId={{6B1F3C2E-8E4A-4C1B-9D7E-4B5A2F0C8D11}
AppName=كاشف
AppVersion={#AppVersion}
AppVerName=كاشف {#AppVersion}
AppPublisher=يوسف أحمد
AppPublisherURL=https://www.instagram.com/Gxp6
AppSupportURL=https://t.me/YsYsD
AppContact=07764455011
AppCopyright=© يوسف أحمد — Telegram YsYsD · Instagram Gxp6 · 07764455011
DefaultDirName={autopf}\Kashif
DefaultGroupName=كاشف
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=Kashif-Setup-{#AppVersion}
OutputDir=..\dist
SetupIconFile=..\Kashif\Assets\kashif.ico
UninstallDisplayIcon={app}\Kashif.exe
UninstallDisplayName=كاشف
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Languages]
#if FileExists(CompilerPath + "Languages\Arabic.isl")
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "associps"; Description: "فتح ملفات سجلات البانك (.ips) بكاشف"; GroupDescription: "ربط الملفات:"

[Files]
Source: "{#ExeDir}\Kashif.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ExeDir}\*.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\كاشف"; Filename: "{app}\Kashif.exe"
Name: "{autodesktop}\كاشف"; Filename: "{app}\Kashif.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.ips\OpenWithProgids"; ValueType: string; ValueName: "Kashif.ips"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associps
Root: HKA; Subkey: "Software\Classes\Kashif.ips"; ValueType: string; ValueName: ""; ValueData: "سجل بانك الآيفون"; Flags: uninsdeletekey; Tasks: associps
Root: HKA; Subkey: "Software\Classes\Kashif.ips\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Kashif.exe,0"; Tasks: associps
Root: HKA; Subkey: "Software\Classes\Kashif.ips\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Kashif.exe"" ""%1"""; Tasks: associps

[Run]
Filename: "{app}\Kashif.exe"; Description: "{cm:LaunchProgram,كاشف}"; Flags: nowait postinstall skipifsilent
