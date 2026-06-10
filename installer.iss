; === CodeBlock-Manager Installer-Skript =====================================
[Setup]
AppName=CodeBlock-Manager
AppVersion=1.3.0
AppPublisher=Rocco Ammon
DefaultDirName={autopf}\CodeBlock-Manager
DefaultGroupName=CodeBlock-Manager
OutputDir=installer
OutputBaseFilename=CodeBlock-Manager-Setup-v1.3.0
Compression=lzma2
SolidCompression=yes
; Eigenes Icon fuer den Installer (Sie haben ja schon eins)
SetupIconFile=CodeBlock-Manager.ico
; Mindestens Windows 10
MinVersion=10.0
; Moderne Optik
WizardStyle=modern

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Files]
; Die fertige EXE einpacken
Source: "publish\CodeBlockManager.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Startmenue-Eintrag
Name: "{group}\CodeBlock-Manager"; Filename: "{app}\CodeBlockManager.exe"
; Deinstallations-Eintrag
Name: "{group}\CodeBlock-Manager deinstallieren"; Filename: "{uninstallexe}"
; Optionale Desktop-Verknuepfung
Name: "{autodesktop}\CodeBlock-Manager"; Filename: "{app}\CodeBlockManager.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknuepfung erstellen"; GroupDescription: "Zusaetzliche Symbole:"

[Run]
; Option, das Programm nach der Installation gleich zu starten
Filename: "{app}\CodeBlockManager.exe"; Description: "CodeBlock-Manager jetzt starten"; Flags: nowait postinstall skipifsilent
