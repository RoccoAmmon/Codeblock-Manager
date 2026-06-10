# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.0.0/),
und dieses Projekt folgt der [Semantischen Versionierung](https://semver.org/lang/de/).

---

## [Unveröffentlicht]

### Geplant
- Optionaler Cursor-Sprung zur geänderten Funktion nach einem Import
- Eigenes Icon für die EXE
- Tastenkürzel-Übersicht im Hilfe-Dialog

---

##.3.0] - 2026-06-10

### Hinzugefügt
- **Redo-Button** ("Wiederholen") als Gegenstück zum Undo
- **Stern (`*`) im Fenstertitel** bei ungespeicherten Änderungen
- Anzeige des **Dateinamens im Fenstertitel**

### Geändert
- Undo/Redo **springt nicht mehr an den Dokumentanfang** – die Scroll-Position
  bleibt nach der Aktion erhalten (`ScrollToVerticalOffset`/`ScrollToHorizontalOffset`)

### Behoben
- **Undo funktionierte nach einem Clipboard-Import nicht**: Ursache war
  `Editor.Text = ...`, das die Undo-Historie löscht. Ersetzt durch
  `Document.Replace`, wodurch Änderungen undo-fähig bleiben

---

##1.2.0] - 2026-06-10

### Hinzugefügt
- **Echter PowerShell-Parser (AST)** über `System.Management.Automation` –
  ersetzt die bisherige Regex/Brace-Erkennung und findet Funktionen zu 100 %
  zuverlässig (auch Here-Strings & verschachtelte Konstrukte)
- **Speichern-Nachfrage beim Schließen** (Ja/Nein/Abbrechen) bei ungespeicherten
  Änderungen
- **Dirty-Flag** zur Erkennung ungespeicherter Änderungen

### Geändert
- Ziel-Framework von `net8.0-windows` auf **`net10.0-windows`** angehoben
  (LTS-Support bis November 2028), wodurch `System.Management.Automation 7.6.2`
  ohne Workaround nutzbar ist

---

##1.1.0] - 2026-06-10

### Hinzugefügt
- **Komplette Neuimplementierung in C# / WPF** mit dem Code-Editor
  [AvalonEdit](https://github.com/icsharpcode/AvalonEdit)
- Echtes **Syntax-Highlighting** für PowerShell (Schlüsselwörter, Strings,
  Kommentare, Variablen, Cmdlets) über `PowerShell.xshd`
- **Zeilennummern** im Editor
- **Farbige Block-Markierung** über einen `IBackgroundRenderer`
  (Gelb = ersetzt, Grün = angehängt)
- Build als eigenständige **Single-File-EXE** möglich

### Behoben
- `Side-by-Side-Konfiguration ungültig` – fehlerhaftes `app.manifest` entfernt
- `NU1100 / NU1202` – NuGet-Quelle und kompatible Paketversionen geklärt

---

##.0.0] - 2026-06-10

### Hinzugefügt
- Erste lauffähige Version als **PowerShell-Skript** mit Windows-Forms-GUI
- **Live-Überwachung der Zwischenablage** per Timer (1-Sekunden-Intervall)
- **Funktions-Erkennung** anhand von `function <Name>`
- **String-sichere Klammer-Zählung** (ignoriert `{ }` in Strings/Kommentaren)
- **Mehrfach-Modus** – mehrere Funktionen aus einem Clipboard auf einmal
- **Automatisches Anhängen** neuer Funktionen (optional per Checkbox)
- **Zweifarbige Hervorhebung** (Gelb = ersetzt, Grün = angehängt) via RichTextBox
- **Status-Farben** für Erfolg, Fehler, Warnung und Info
- **Automatisches Backup** mit Zeitstempel beim Speichern
- **Logging** unter `C:\ScriptLog\`
- Modernisierte Oberfläche mit Flat-Design, Dark-Theme und Hover-Effekten

### Hinweise
- Diese Version war der funktionale Prototyp; die Weiterentwicklung erfolgte
  in C# (siehe ab Version 1.1.0).

---

[Unveröffentlicht]: https://github.com/RoccoAmmon/Codeblock-Manager/compare/v1.3.0...HEAD: https://github.com/RoccoAmmon/Codeblock-Manager/compare/v1.2.0...v1.30]: https://github.com/RoccoAmmon/Codeblock-Manager/compare/v1.1.0...v11.0]: https://github.com/RoccoAmmon/Codeblock-Manager/compare/v1.0.0...1.0.0]: https://github.com/RoccoAmmon/Codeblock-Manager/releases/tag/v1.0.0
