using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
        private readonly DispatcherTimer _markerTimer = new();   // blendet Markierungen aus
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

                // Einmal-Timer: blendet die Block-Markierungen nach Ablauf aus
                _markerTimer.Tick += MarkerTimer_Tick;

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

        // === Block-Erkennung (Funktion/Filter/Workflow, Klasse/Enum, Configuration) ==
        private static bool IstBlockAst(Ast n) =>
            n is FunctionDefinitionAst ||
            n is TypeDefinitionAst ||
            n is ConfigurationDefinitionAst;

        // Name + Art eines Block-Knotens ermitteln
        private static (string Name, string Art) BlockInfo(Ast n) => n switch
        {
            FunctionDefinitionAst f => (f.Name, f.IsFilter ? "Filter" : f.IsWorkflow ? "Workflow" : "Funktion"),
            TypeDefinitionAst t     => (t.Name, t.IsEnum ? "Enum" : "Klasse"),
            ConfigurationDefinitionAst c => ((c.InstanceName as StringConstantExpressionAst)?.Value ?? "Configuration", "Configuration"),
            _ => ("", "")
        };

        // Zeilenanfang (Offset nach dem letzten Zeilenumbruch) zu einer Position
        private static int ZeilenAnfang(string text, int pos)
        {
            int i = text.LastIndexOf('\n', Math.Max(0, Math.Min(pos, text.Length) - 1));
            return i < 0 ? 0 : i + 1;
        }

        // Start eines Blocks rueckwaerts um direkt darueberstehende Kommentare erweitern
        private static int ErweitereUmKommentar(string text, Token[] tokens, int blockStart)
        {
            int start = blockStart;
            var kommentare = tokens
                .Where(t => t.Kind == TokenKind.Comment && t.Extent.EndOffset <= blockStart)
                .OrderByDescending(t => t.Extent.EndOffset);

            foreach (var t in kommentare)
            {
                int e = t.Extent.EndOffset;
                if (e > start) continue;
                string zwischen = text.Substring(e, start - e);
                if (zwischen.Trim().Length != 0) break;                 // anderer Code -> Ende
                if (zwischen.Count(c => c == '\n') > 1) break;          // Leerzeile -> getrennt
                start = t.Extent.StartOffset;                           // Kommentar einbeziehen
            }
            return start;
        }

        // Eingerueckten Block-Code auf eine Ziel-Einrueckung normalisieren
        private static string PasseEinrueckungAn(string code, string zielEinrueckung)
        {
            var zeilen = code.Replace("\r\n", "\n").Split('\n');

            int min = int.MaxValue;
            foreach (var z in zeilen)
            {
                if (z.Trim().Length == 0) continue;
                int i = 0;
                while (i < z.Length && (z[i] == ' ' || z[i] == '\t')) i++;
                min = Math.Min(min, i);
            }
            if (min == int.MaxValue) min = 0;

            var sb = new StringBuilder();
            for (int k = 0; k < zeilen.Length; k++)
            {
                string z = zeilen[k];
                if (z.Trim().Length == 0) sb.Append("");
                else sb.Append(zielEinrueckung + z.Substring(min));
                if (k < zeilen.Length - 1) sb.Append("\r\n");
            }
            return sb.ToString();
        }

        // Bereich eines benannten Blocks im Text finden (inkl. Kommentar, ab Zeilenanfang)
        private (int Start, int Ende, string Indent)? FindeBlockRegion(string text, string name)
        {
            try
            {
                Token[] tokens;
                ScriptBlockAst ast = Parser.ParseInput(text, out tokens, out _);

                var node = ast.FindAll(
                    n => IstBlockAst(n) &&
                         string.Equals(BlockInfo(n).Name, name, StringComparison.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: true)
                    .FirstOrDefault();

                if (node == null) return null;

                int bStart = node.Extent.StartOffset;
                int bEnd   = node.Extent.EndOffset;
                int kStart = ErweitereUmKommentar(text, tokens, bStart);
                int zStart = ZeilenAnfang(text, kStart);
                string indent = text.Substring(zStart, kStart - zStart);
                return (zStart, bEnd, indent);
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER im AST-Parser: " + ex.Message, "ERROR");
                return null;
            }
        }

        // === Alle Bloecke aus Clipboard per AST ermitteln (inkl. Kommentar) ==
        private List<(string Name, string Code, string Art)> GetAlleBloeckeAst(string codeText)
        {
            var ergebnis = new List<(string, string, string)>();
            try
            {
                Token[] tokens;
                ScriptBlockAst ast = Parser.ParseInput(codeText, out tokens, out _);

                var knoten = ast.FindAll(IstBlockAst, searchNestedScriptBlocks: false);

                foreach (var n in knoten)
                {
                    var (name, art) = BlockInfo(n);
                    int bStart = n.Extent.StartOffset;
                    int bEnd   = n.Extent.EndOffset;
                    int kStart = ErweitereUmKommentar(codeText, tokens, bStart);
                    int zStart = ZeilenAnfang(codeText, kStart);
                    string code = codeText.Substring(zStart, bEnd - zStart);
                    ergebnis.Add((name, code, art));
                }
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER beim AST-Zerlegen: " + ex.Message, "ERROR");
            }
            return ergebnis;
        }

        // === Datenklasse fuer eine geplante Aenderung (Vorschau) =============
        private class Aenderung
        {
            public string Name = "";
            public string Art = "";
            public string Aktion = "";   // "Ersetzt" oder "Angehaengt"
            public string Alt = "";
            public string Neu = "";
        }

        // === Bloecke ersetzen / anhaengen (per AST, mit Vorschau) ============
        private void InvokeFunktionsErsetzung(string clipText)
        {
            try
            {
                var bloecke = GetAlleBloeckeAst(clipText);
                if (bloecke.Count == 0) return;

                // 1) Aenderungen auf einer Arbeitskopie berechnen (noch nicht anwenden)
                string arbeit = Editor.Text;
                var aenderungen = new List<Aenderung>();
                var uebersprungen = new List<string>();

                foreach (var b in bloecke)
                {
                    var region = FindeBlockRegion(arbeit, b.Name);
                    if (region != null)
                    {
                        string alt = arbeit.Substring(region.Value.Start,
                                                      region.Value.Ende - region.Value.Start);
                        string neu = PasseEinrueckungAn(b.Code, region.Value.Indent);
                        arbeit = arbeit.Substring(0, region.Value.Start)
                               + neu
                               + arbeit.Substring(region.Value.Ende);
                        aenderungen.Add(new Aenderung { Name = b.Name, Art = b.Art, Aktion = "Ersetzt", Alt = alt, Neu = neu });
                        WriteLog($"{b.Art} '{b.Name}' ersetzt (AST).", "SUCCESS");
                    }
                    else if (ChkAnhaengen.IsChecked == true)
                    {
                        string neu = PasseEinrueckungAn(b.Code, "");
                        arbeit = arbeit.TrimEnd() + "\r\n\r\n" + neu + "\r\n";
                        aenderungen.Add(new Aenderung { Name = b.Name, Art = b.Art, Aktion = "Angehaengt", Alt = "", Neu = neu });
                        WriteLog($"{b.Art} '{b.Name}' angehaengt (AST).", "INFO");
                    }
                    else
                    {
                        uebersprungen.Add(b.Name);
                        WriteLog($"{b.Art} '{b.Name}' uebersprungen.", "WARN");
                    }
                }

                if (aenderungen.Count == 0)
                {
                    SetStatus("Keine passenden Bloecke - nichts geaendert.", "Warnung");
                    return;
                }

                // 2) Optionale Vorschau anzeigen
                if (ChkVorschau.IsChecked == true && !ZeigeVorschau(aenderungen))
                {
                    SetStatus("Ersetzung abgebrochen (Vorschau verworfen).", "Info");
                    WriteLog("Ersetzung per Vorschau verworfen.");
                    return;
                }

                // 3) Anwenden (eine Aktion fuer Undo)
                Editor.Document.Replace(0, Editor.Document.TextLength, arbeit);

                // 4) Markierungen setzen (gelb = ersetzt, gruen = angehaengt)
                _marker.Bereiche.Clear();
                int? erstesOffset = null;
                foreach (var a in aenderungen)
                {
                    var r = FindeBlockRegion(arbeit, a.Name);
                    if (r != null)
                    {
                        bool ersetzt = a.Aktion == "Ersetzt";
                        _marker.Bereiche.Add((r.Value.Start, r.Value.Ende - r.Value.Start, ersetzt));
                        erstesOffset ??= r.Value.Start;
                    }
                }
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
                StarteMarkerTimer();

                // 5) Zur ersten geaenderten Stelle springen
                if (erstesOffset.HasValue)
                {
                    int offset = Math.Min(erstesOffset.Value, Editor.Document.TextLength);
                    var pos = Editor.Document.GetLocation(offset);
                    Editor.CaretOffset = offset;
                    Editor.ScrollToLine(pos.Line);
                    Editor.TextArea.Caret.BringCaretToView();
                }

                // 6) Statusmeldung
                var ersetztNamen   = aenderungen.Where(a => a.Aktion == "Ersetzt").Select(a => a.Name).ToList();
                var angehaengtNamen = aenderungen.Where(a => a.Aktion == "Angehaengt").Select(a => a.Name).ToList();
                var teile = new List<string>();
                if (ersetztNamen.Count > 0)    teile.Add("Ersetzt: " + string.Join(", ", ersetztNamen));
                if (angehaengtNamen.Count > 0)  teile.Add("Angehaengt: " + string.Join(", ", angehaengtNamen));
                if (uebersprungen.Count > 0)    teile.Add("Uebersprungen: " + string.Join(", ", uebersprungen));

                SetStatus(string.Join("   |   ", teile), uebersprungen.Count > 0 ? "Warnung" : "Erfolg");
            }
            catch (Exception ex)
            {
                WriteLog("FEHLER bei Ersetzung: " + ex.Message, "ERROR");
                SetStatus("Fehler bei Ersetzung - siehe Log.", "Fehler");
            }
        }

        // === Vorschau-Fenster: Diff anzeigen, true = uebernehmen =============
        private bool ZeigeVorschau(List<Aenderung> aenderungen)
        {
            var sb = new StringBuilder();
            foreach (var a in aenderungen)
            {
                sb.AppendLine($"=== {a.Art}: {a.Name}   [{a.Aktion}] ===");
                if (a.Aktion == "Ersetzt")
                {
                    sb.AppendLine("--- ALT ---");
                    sb.AppendLine(a.Alt);
                }
                sb.AppendLine("+++ NEU +++");
                sb.AppendLine(a.Neu);
                sb.AppendLine();
            }

            var dunkel = (Brush)new BrushConverter().ConvertFrom("#1E1E1E")!;
            var hell   = (Brush)new BrushConverter().ConvertFrom("#DCDCDC")!;

            var win = new Window
            {
                Title = "Vorschau der Aenderungen",
                Width = 900,
                Height = 650,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = dunkel
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tb = new TextBox
            {
                Text = sb.ToString(),
                IsReadOnly = true,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                Background = dunkel,
                Foreground = hell,
                BorderThickness = new Thickness(0),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(tb, 0);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            var btnOk = new Button { Content = "Uebernehmen", Width = 130, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var btnAbbruch = new Button { Content = "Verwerfen", Width = 130, Height = 32, IsCancel = true };

            bool ergebnis = false;
            btnOk.Click += (_, __) => { ergebnis = true; win.Close(); };
            btnAbbruch.Click += (_, __) => { ergebnis = false; win.Close(); };
            panel.Children.Add(btnOk);
            panel.Children.Add(btnAbbruch);
            Grid.SetRow(panel, 1);

            grid.Children.Add(tb);
            grid.Children.Add(panel);
            win.Content = grid;
            win.ShowDialog();
            return ergebnis;
        }

        // === Marker-Timer: blendet Markierungen nach Ablauf aus ==============
        private void StarteMarkerTimer()
        {
            _markerTimer.Stop();
            int sekunden = GetMarkerSekunden();
            if (sekunden <= 0) return;   // "Aus"
            _markerTimer.Interval = TimeSpan.FromSeconds(sekunden);
            _markerTimer.Start();
        }

        private int GetMarkerSekunden()
        {
            if (CmbMarkerTimeout.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out int v))
                return v;
            return 0;
        }

        private void MarkerTimer_Tick(object? sender, EventArgs e)
        {
            _markerTimer.Stop();
            _marker.Bereiche.Clear();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
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
                        if (Regex.IsMatch(clip, @"(?im)^\s*(function|filter|workflow|class|enum|configuration)\s+"))
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
                    _markerTimer.Stop();
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
