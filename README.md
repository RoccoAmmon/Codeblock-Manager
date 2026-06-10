<h1 align="center">📋 CodeBlock-Manager</h1>

<p align="center">
  <em>Tausche PowerShell-Funktionen live aus der Zwischenablage aus – mit Syntax-Highlighting, AST-Parser und einem Klick.</em>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET">
  <img src="https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white" alt="C#">
  <img src="https://img.shields.io/badge/editor-AvalonEdit-FF6F00" alt="AvalonEdit">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
</p>

---

## 📖 Über das Projekt

Der **CodeBlock-Manager** löst ein alltägliches Problem: Wer Code aus einem Chat oder einer anderen Quelle in ein bestehendes Skript übernimmt, muss sonst mühsam die richtige Stelle suchen und manuell ersetzen.

Dieses Tool überwacht die **Zwischenablage live**: Sobald Sie eine PowerShell-Funktion kopieren, erkennt es diese automatisch und **tauscht sie im geladenen Skript aus** – oder hängt sie an, falls sie noch nicht existiert. Dank des **offiziellen PowerShell-Parsers** geschieht das zu 100 % zuverlässig.

---

## ✨ Funktionen

- 🔄 **Live-Überwachung der Zwischenablage** – kopierte `function`-Blöcke werden automatisch erkannt
- 🎯 **Echter PowerShell-Parser (AST)** – findet Funktionen zuverlässig, auch bei Here-Strings & verschachtelten Konstrukten
- 📚 **Mehrfach-Modus** – mehrere Funktionen in einem Durchlauf
- ➕ **Automatisches Anhängen** neuer Funktionen (optional)
- 🌈 **Syntax-Highlighting** für PowerShell via [AvalonEdit](https://github.com/icsharpcode/AvalonEdit)
- 🟡🟢 **Farbliche Markierung**: Gelb = ersetzt, Grün = angehängt
- ↩️ **Undo / Redo** mit stabiler Scroll-Position
- 💾 **Automatisches Backup** beim Speichern (mit Zeitstempel)
- ⚠️ **Speichern-Nachfrage** beim Schließen + `*` im Titel bei ungespeicherten Änderungen
- 📝 **Logging** unter `C:\ScriptLog\`

---

## 🖼️ Vorschau

> _Tipp: Fügen Sie hier einen Screenshot ein, sobald Sie einen haben:_

```text
![Screenshot](docs/screenshot.png)
```

---

## 🚀 Erste Schritte

### Voraussetzungen

| Werkzeug   | Version | Zweck                  |
|------------|---------|------------------------|
| .NET SDK   | 10.0    | Build & Ausführung     |
| Windows    | 10 / 11 | WPF-Oberfläche         |
| VS Code    | aktuell | (optional) Entwicklung |

```powershell
dotnet --version   # sollte 10.0.x anzeigen
```

### Installation & Start

```powershell
# Repository klonen
git clone <repo-url>
cd Codeblock-Manager
