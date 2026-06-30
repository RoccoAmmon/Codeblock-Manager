using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;

namespace CodeBlockManager
{
    public partial class MainWindow : Window
    {
        // === Variablen-Definition ============================================
        private readonly string _logVerzeichnis = @"C:\ScriptLog";
        private readonly string _logDatei = @"C:\ScriptLog\CodeBlockManager-CS.log";
        private string _aktuellerPfad = "";
        private string _letzteClipboard = "";
        private bool _ungespeicherteAenderungen = false;   // Dirty-Flag
        private readonly DispatcherTimer _timer = new();
        private readonly BlockMarker _marker = new();

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                LadeSyntaxHighlighting();

                // AvalonEdit: Hintergrund-Renderer fuer Block-Markierungen registrieren
                Editor.TextArea.TextView.BackgroundRenderers.Add(_marker);

                // Aenderungen im Editor erfassen (fuer Dirty-Flag + Titel-Stern)
                Editor.TextChanged += Editor_TextChanged;

                // Beim Schliessen nachfragen
                this.Closing += MainWindow_Closing;

                // Timer fuer Zwischenablage-Ueberwachung (1 Sekunde)
                _timer.Interval = TimeSpan.FromMilliseconds(1000);
                _timer.Tick += Timer_Tick;
                _timer.Start();

                AktualisiereTitel();
                WriteLog("Anwendung gestartet.");
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim Start: " + ex.Message, "ERROR");
                SetStatus("Fehler beim Start - siehe Log.", "Fehler");
            }
        }

        // === Fenstertitel je nach Speicherstatus aktualisieren ===============
        private void AktualisiereTitel()
        {
            string basis = "CodeBlock-Manager (C#)";
            string datei = string.IsNullOrWhiteSpace(_aktuellerPfad)
                ? ""
                : " - " + Path.GetFileName(_aktuellerPfad);
            string stern = _ungespeicherteAenderungen ? " *" : "";
            this.Title = basis + datei + stern;
        }

        // === Reaktion auf Textaenderungen ====================================
        private void Editor_TextChanged(object? sender, EventArgs e)
        {
            _ungespeicherteAenderungen = true;
            AktualisiereTitel();
        }

        // === Syntax-Highlighting aus eingebetteter Ressource laden ===========
        private void LadeSyntaxHighlighting()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                // Name = <Namespace>.<Dateiname>
                using var stream = asm.GetManifestResourceStream("CodeBlockManager.PowerShell.xshd");
                if (stream != null)
                {
                    using var reader = new XmlTextReader(stream);
                    var def = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    Editor.SyntaxHighlighting = def;
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim Laden des Highlightings: " + ex.Message, "ERROR");
            }
        }

        // === Logging =========================================================
        private void WriteLog(string nachricht, string level = "INFO")
        {
            try
            {
                if (!Directory.Exists(_logVerzeichnis))
                    Directory.CreateDirectory(_logVerzeichnis);

                string eintrag = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {nachricht}";
                File.AppendAllText(_logDatei, eintrag + Environment.NewLine);
            }
            catch { /* Logging darf nie abstuerzen */ }
        }

        // === Statusmeldung mit Farbe =========================================
        private void SetStatus(string text, string typ = "Info")
        {
            LblStatus.Text = text;
            LblStatus.Foreground = typ switch
            {
                "Erfolg"  => new SolidColorBrush(Color.FromRgb(82, 196, 105)),
                "Fehler"  => new SolidColorBrush(Color.FromRgb(240, 90, 90)),
                "Warnung" => new SolidColorBrush(Color.FromRgb(240, 170, 60)),
                _         => new SolidColorBrush(Color.FromRgb(120, 180, 235)),
            };
        }

        // === Funktionsbereich per echtem PowerShell-Parser (AST) =============
        private (int Start, int Laenge)? GetFunktionsBereichAst(string inhalt, string name)
        {
            try
            {
                Token[] tokens;
                ParseError[] errors;
                ScriptBlockAst ast = Parser.ParseInput(inhalt, out tokens, out errors);

                var funktion = ast.FindAll(
                    n => n is FunctionDefinitionAst fd &&
                         string.Equals(fd.Name, name, StringComparison.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: true)
                    .Cast<FunctionDefinitionAst>()
                    .FirstOrDefault();

                if (funktion == null) return null;

                int start  = funktion.Extent.StartOffset;
                int laenge = funktion.Extent.EndOffset - funktion.Extent.StartOffset;
                return (start, laenge);
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER im AST-Parser: " + ex.Message, "ERROR");
                return null;
            }
        }

        // === Alle Funktionen aus Clipboard per AST ermitteln =================
        private List<(string Name, string Code)> GetAlleFunktionenAst(string codeText)
        {
            var ergebnis = new List<(string, string)>();
            try
            {
                Token[] tokens;
                ParseError[] errors;
                ScriptBlockAst ast = Parser.ParseInput(codeText, out tokens, out errors);

                var funktionen = ast.FindAll(
                    n => n is FunctionDefinitionAst,
                    searchNestedScriptBlocks: false)
                    .Cast<FunctionDefinitionAst>();

                foreach (var fn in funktionen)
                {
                    string code = fn.Extent.Text;
                    ergebnis.Add((fn.Name, code));
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim AST-Zerlegen: " + ex.Message, "ERROR");
            }
            return ergebnis;
        }

        // === Funktionen ersetzen / anhaengen (per AST) =======================
        private void InvokeFunktionsErsetzung(string clipText)
        {
            try
            {
                var funktionen = GetAlleFunktionenAst(clipText);
                if (funktionen.Count == 0) return;

                string inhalt = Editor.Text;
                var ersetzt = new List<string>();
                var angehaengt = new List<string>();
                var uebersprungen = new List<string>();

                foreach (var fn in funktionen)
                {
                    var bereich = GetFunktionsBereichAst(inhalt, fn.Name);
                    if (bereich != null)
                    {
                        inhalt = inhalt.Substring(0, bereich.Value.Start)
                               + fn.Code
                               + inhalt.Substring(bereich.Value.Start + bereich.Value.Laenge);
                        ersetzt.Add(fn.Name);
                        WriteLog($"Funktion '{fn.Name}' ersetzt (AST).", "SUCCESS");
                    }
                    else if (ChkAnhaengen.IsChecked == true)
                    {
                        inhalt = inhalt.TrimEnd() + "\r\n\r\n" + fn.Code + "\r\n";
                        angehaengt.Add(fn.Name);
                        WriteLog($"Funktion '{fn.Name}' angehaengt (AST).", "INFO");
                    }
                    else
                    {
                        uebersprungen.Add(fn.Name);
                        WriteLog($"Funktion '{fn.Name}' uebersprungen.", "WARN");
                    }
                }

                Editor.Document.Replace(0, Editor.Document.TextLength, inhalt);   // setzt Text, behält Undo

                // Markierungen setzen (gelb = ersetzt, gruen = angehaengt)
                _marker.Bereiche.Clear();
                int? erstesOffset = null;   // erste geaenderte Stelle merken (zum Hinscrollen)
                foreach (var name in ersetzt)
                {
                    var b = GetFunktionsBereichAst(inhalt, name);
                    if (b != null)
                    {
                        _marker.Bereiche.Add((b.Value.Start, b.Value.Laenge, true));
                        erstesOffset ??= b.Value.Start;
                    }
                }
                foreach (var name in angehaengt)
                {
                    var b = GetFunktionsBereichAst(inhalt, name);
                    if (b != null)
                    {
                        _marker.Bereiche.Add((b.Value.Start, b.Value.Laenge, false));
                        erstesOffset ??= b.Value.Start;
                    }
                }
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

                // Zur ersten geaenderten Funktion springen und sichtbar machen
                if (erstesOffset.HasValue)
                {
                    int offset = Math.Min(erstesOffset.Value, Editor.Document.TextLength);
                    var pos = Editor.Document.GetLocation(offset);
                    Editor.CaretOffset = offset;
                    Editor.ScrollToLine(pos.Line);
                    Editor.TextArea.Caret.BringCaretToView();
                }

                // Statusmeldung
                var teile = new List<string>();
                if (ersetzt.Count > 0)        teile.Add("Ersetzt: " + string.Join(", ", ersetzt));
                if (angehaengt.Count > 0)      teile.Add("Angehaengt: " + string.Join(", ", angehaengt));
                if (uebersprungen.Count > 0)   teile.Add("Uebersprungen: " + string.Join(", ", uebersprungen));
                string meldung = string.Join("   |   ", teile);

                SetStatus(meldung, uebersprungen.Count > 0 ? "Warnung" : "Erfolg");
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER bei Ersetzung: " + ex.Message, "ERROR");
                SetStatus("Fehler bei Ersetzung - siehe Log.", "Fehler");
            }
        }

        // === Event: Skript laden =============================================
        private void BtnLaden_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "PowerShell-Skripte (*.ps1)|*.ps1|Alle Dateien (*.*)|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    _aktuellerPfad = dlg.FileName;
                    Editor.Text = File.ReadAllText(_aktuellerPfad);
                    _marker.Bereiche.Clear();
                    Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

                    // AvalonEdit-Undo-Historie zuruecksetzen (frisch geladen)
                    Editor.Document.UndoStack.ClearAll();

                    _ungespeicherteAenderungen = false;   // frisch geladen = sauber
                    AktualisiereTitel();

                    SetStatus("Geladen: " + _aktuellerPfad, "Info");
                    WriteLog("Skript geladen: " + _aktuellerPfad);
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim Laden: " + ex.Message, "ERROR");
                SetStatus("Laden fehlgeschlagen - siehe Log.", "Fehler");
            }
        }

        // === Event: Speichern ================================================
        private void BtnSpeichern_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_aktuellerPfad))
                {
                    SetStatus("Bitte zuerst ein Skript laden.", "Warnung");
                    return;
                }
                // Backup mit Zeitstempel
                string zeitstempel = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupPfad = $"{_aktuellerPfad}.{zeitstempel}.bak";
                File.Copy(_aktuellerPfad, backupPfad, true);
                WriteLog("Backup erstellt: " + backupPfad);

                File.WriteAllText(_aktuellerPfad, Editor.Text, new UTF8Encoding(true));

                _ungespeicherteAenderungen = false;   // gespeichert = sauber
                AktualisiereTitel();

                SetStatus($"Gespeichert (Backup: {backupPfad})", "Erfolg");
                WriteLog("Skript gespeichert: " + _aktuellerPfad);
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim Speichern: " + ex.Message, "ERROR");
                SetStatus("Speichern fehlgeschlagen - siehe Log.", "Fehler");
            }
        }

        // === Event: Rueckgaengig (Undo) ======================================
                // === Event: Rueckgaengig (Undo) ======================================
        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Editor.CanUndo)
                {
                    // Scroll-Position vor der Aktion merken
                    double vOffset = Editor.VerticalOffset;
                    double hOffset = Editor.HorizontalOffset;

                    Editor.Undo();

                    // Ansicht wieder an die alte Stelle scrollen,
                    // damit der Editor nicht an den Anfang springt
                    Editor.ScrollToVerticalOffset(vOffset);
                    Editor.ScrollToHorizontalOffset(hOffset);

                    SetStatus("Letzte Aenderung rueckgaengig gemacht.", "Info");
                    WriteLog("Undo ausgefuehrt.");
                }
                else
                {
                    SetStatus("Nichts zum Rueckgaengigmachen vorhanden.", "Warnung");
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER bei Undo: " + ex.Message, "ERROR");
                SetStatus("Fehler bei Undo - siehe Log.", "Fehler");
            }
        }

        // === Event: Wiederholen (Redo) =======================================
        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Editor.CanRedo)
                {
                    // Scroll-Position vor der Aktion merken
                    double vOffset = Editor.VerticalOffset;
                    double hOffset = Editor.HorizontalOffset;

                    Editor.Redo();

                    // Ansicht wieder an die alte Stelle scrollen
                    Editor.ScrollToVerticalOffset(vOffset);
                    Editor.ScrollToHorizontalOffset(hOffset);

                    SetStatus("Aenderung wiederhergestellt.", "Info");
                    WriteLog("Redo ausgefuehrt.");
                }
                else
                {
                    SetStatus("Nichts zum Wiederholen vorhanden.", "Warnung");
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER bei Redo: " + ex.Message, "ERROR");
                SetStatus("Fehler bei Redo - siehe Log.", "Fehler");
            }
        }


        // === Event: Markierungen loeschen ====================================
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _marker.Bereiche.Clear();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            SetStatus("Markierungen entfernt.", "Info");
        }

        // === Timer: Zwischenablage ueberwachen ===============================
        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (ChkLive.IsChecked != true) return;
                if (string.IsNullOrWhiteSpace(_aktuellerPfad)) return;

                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(clip) && clip != _letzteClipboard)
                    {
                        _letzteClipboard = clip;
                        if (Regex.IsMatch(clip, @"(?im)^\s*function\s+"))
                            InvokeFunktionsErsetzung(clip);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER im Timer: " + ex.Message, "ERROR");
            }
        }

        // === Event: Fenster wird geschlossen =================================
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Nur fragen, wenn es ungespeicherte Aenderungen gibt
                if (_ungespeicherteAenderungen)
                {
                    var antwort = MessageBox.Show(
                        "Es gibt ungespeicherte Aenderungen.\n\n" +
                        "Moechten Sie vor dem Schliessen speichern?",
                        "Aenderungen speichern?",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    switch (antwort)
                    {
                        case MessageBoxResult.Yes:
                            BtnSpeichern_Click(this, new RoutedEventArgs());
                            // Falls Speichern fehlschlug (Flag noch true), Schliessen abbrechen
                            if (_ungespeicherteAenderungen)
                            {
                                e.Cancel = true;
                                SetStatus("Schliessen abgebrochen - bitte Speicherproblem pruefen.", "Fehler");
                            }
                            break;

                        case MessageBoxResult.No:
                            WriteLog("Geschlossen ohne Speichern - Aenderungen verworfen.", "WARN");
                            break;

                        case MessageBoxResult.Cancel:
                            e.Cancel = true;
                            break;
                    }
                }

                // Timer stoppen, wenn wir wirklich schliessen
                if (!e.Cancel)
                {
                    _timer.Stop();
                    WriteLog("Anwendung geschlossen.");
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim Schliessen: " + ex.Message, "ERROR");
            }
        }
    }

    // === Hintergrund-Renderer fuer farbige Block-Markierungen ================
    public class BlockMarker : IBackgroundRenderer
    {
        // (Start, Laenge, IstErsetzt)  -> true = gelb, false = gruen
        public List<(int Start, int Laenge, bool IstErsetzt)> Bereiche { get; } = new();

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Bereiche.Count == 0) return;
            textView.EnsureVisualLines();

            var brushGelb  = new SolidColorBrush(Color.FromArgb(70, 220, 220, 60));
            var brushGruen = new SolidColorBrush(Color.FromArgb(70, 80, 220, 90));
            brushGelb.Freeze();
            brushGruen.Freeze();

            foreach (var b in Bereiche)
            {
                var segment = new ICSharpCode.AvalonEdit.Document.TextSegment
                {
                    StartOffset = b.Start,
                    Length = b.Laenge
                };
                foreach (var rect in ICSharpCode.AvalonEdit.Rendering.BackgroundGeometryBuilder
                         .GetRectsForSegment(textView, segment))
                {
                    drawingContext.DrawRectangle(
                        b.IstErsetzt ? brushGelb : brushGruen,
                        null, rect);
                }
            }
        }
    }
}
