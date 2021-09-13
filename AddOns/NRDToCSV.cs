#region Using declarations
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.Gui.NinjaScript
{
    public class NRDToCSV : AddOnBase
    {
        private NTMenuItem menuItem;
        private NTMenuItem existingMenuItemInControlCenter;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NRDToCSV";
                Description = "*.nrd to *.csv market replay files convertion";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null) return;

            existingMenuItemInControlCenter = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem;
            if (existingMenuItemInControlCenter == null) return;

            menuItem = new NTMenuItem { Header = "NRD to CSV", Style = Application.Current.TryFindResource("MainMenuItem") as Style };
            existingMenuItemInControlCenter.Items.Add(menuItem);
            menuItem.Click += OnMenuItemClick;
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (menuItem != null && window is ControlCenter)
            {
                if (existingMenuItemInControlCenter != null && existingMenuItemInControlCenter.Items.Contains(menuItem))
                    existingMenuItemInControlCenter.Items.Remove(menuItem);
                menuItem.Click -= OnMenuItemClick;
                menuItem = null;
            }
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => new NRDToCSVWindow().Show()));
        }
    }

    public class NRDToCSVWindow : NTWindow, IWorkspacePersistence
    {
        private static readonly int PARALLEL_THREADS_COUNT = 4;

        private TextBox tbCsvRootDir;
        private TextBox tbSelectedInstruments;
        private Button bConvert;
        private TextBox tbOutput;
        private Label lProgress;
        private ProgressBar pbProgress;
        private int taskCount;
        private DateTime startTimestamp;
        private long completeFilesLength;
        private long totalFilesLength;
        private bool running;
        private bool canceling;

        public NRDToCSVWindow()
        {
            Caption = "NRD to CSV";
            Width = 512;
            Height = 512;
            Content = BuildContent();
            Loaded += (o, e) =>
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions("NRDToCSV-" + Guid.NewGuid().ToString("N"), this);
            };
            Closing += (o, e) =>
            {
                if (bConvert != null)
                    bConvert.Click -= OnConvertButtonClick;
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            if (running)
                canceling = true;
            base.OnClosed(e);
        }


        private DependencyObject BuildContent()
        {
            double margin = (double)FindResource("MarginBase");
            tbCsvRootDir = new TextBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                Text = Path.Combine(Globals.UserDataDir, "db", "replay.csv"),
            };
            Label lCsvRootDir = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Root directory of converted CSV files:",
            };
            tbSelectedInstruments = new TextBox() { Margin = new Thickness(margin, 0, margin, margin) };
            Label lSelectedInstruments = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, margin, margin, 0),
                Content = "Semicolon separated RegEx'es to filter *.nrd file names (keep empty to proceed all):",
            };
            bConvert = new Button() { Margin = new Thickness(margin), IsDefault = true, Content = "_Convert" };
            bConvert.Click += OnConvertButtonClick;
            tbOutput = new TextBox()
            {
                IsReadOnly = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(margin),
            };
            pbProgress = new ProgressBar()
            {
                Height = 0,
            };
            lProgress = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Height = 0,
            };

            Grid grid = new Grid() { Background = new SolidColorBrush(Colors.Transparent) };
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            Grid.SetRow(lCsvRootDir, 0);
            Grid.SetRow(tbCsvRootDir, 1);
            Grid.SetRow(lSelectedInstruments, 2);
            Grid.SetRow(tbSelectedInstruments, 3);
            Grid.SetRow(bConvert, 4);
            Grid.SetRow(tbOutput, 5);
            Grid.SetRow(lProgress, 6);
            Grid.SetRow(pbProgress, 7);
            grid.Children.Add(lCsvRootDir);
            grid.Children.Add(tbCsvRootDir);
            grid.Children.Add(lSelectedInstruments);
            grid.Children.Add(tbSelectedInstruments);
            grid.Children.Add(bConvert);
            grid.Children.Add(tbOutput);
            grid.Children.Add(lProgress);
            grid.Children.Add(pbProgress);
            return grid;
        }

        private void OnConvertButtonClick(object sender, RoutedEventArgs e)
        {
            if (tbOutput == null) return;

            if (running)
            {
                if (!canceling)
                {
                    canceling = true;
                    logout("Canceling convertion...");
                    bConvert.IsEnabled = false;
                    bConvert.Content = "Canceling...";
                }
                return;
            }

            tbOutput.Clear();

            string nrdDir = Path.Combine(Globals.UserDataDir, "db", "replay");
            string csvDir = tbCsvRootDir.Text;
            List<Regex> selectedInstruments = tbSelectedInstruments.Text.IsNullOrEmpty() ? null :
                tbSelectedInstruments.Text.Split(';').Select(p => new Regex(p.Trim())).ToList();

            if (!Directory.Exists(nrdDir))
            {
                logout(string.Format("ERROR: The NRD root directory \"{0}\" not found", nrdDir));
                return;
            }

            string[] nrdSubDirs = Directory.GetDirectories(nrdDir);
            if (nrdSubDirs.Length == 0)
            {
                logout(string.Format("WARNING: The NRD root directory \"{0}\" is empty", nrdDir));
                return;
            }

            if (!Directory.Exists(csvDir))
            {
                try
                {
                    Directory.CreateDirectory(csvDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the CSV root directory \"{0}\": {1}", csvDir, error.ToString()));
                }
                return;
            }

            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                completeFilesLength = 0;
                totalFilesLength = 0;
                List<DumpEntry> entries = new List<DumpEntry>();
                foreach (string subDir in nrdSubDirs)
                    ProceedDirectory(entries, nrdDir, subDir, csvDir, selectedInstruments);
                if (entries.Count == 0)
                {
                    logout("No *.nrd files found to convert");
                }
                else
                {
                    Globals.RandomDispatcher.InvokeAsync(new Action(() =>
                    {
                        logout(string.Format("Convert {0} files...", entries.Count));
                        run(entries.Count);
                        taskCount = PARALLEL_THREADS_COUNT;
                        for (int i = 0; i < taskCount; i++)
                            RunConversion(entries, i, taskCount);
                    }));
                }
            }));
        }

        private void ProceedDirectory(List<DumpEntry> entries, string nrdRoot, string nrdDir, string csvDir, List<Regex> selectedInstruments)
        {
            string[] fileEntries = Directory.GetFiles(nrdDir, "*.nrd");
            if (fileEntries.Length == 0)
            {
                logout(string.Format("WARNING: No *.nrd files found in \"{0}\" directory. Skipped", nrdDir));
                return;
            }

            foreach (string fileName in fileEntries)
            {
                string fullName = Path.GetFileName(Path.GetDirectoryName(fileName));
                string relativeName = fileName.Substring(nrdRoot.Length);

                if (selectedInstruments != null && selectedInstruments.Where(r => r.Match(relativeName).Success).Count() == 0)
                    continue;

                Collection<Instrument> instruments = InstrumentList.GetInstruments(fullName);
                if (instruments.Count == 0)
                {
                    logout(string.Format("Unable to find an instrument named \"{0}\". Skipped", fullName));
                    continue;
                }
                else if (instruments.Count > 1)
                {
                    logout(string.Format("More than one instrument identified for name \"{0}\". Skipped", fullName));
                    continue;
                }
                Cbi.Instrument instrument = instruments[0];
                string name = Path.GetFileNameWithoutExtension(fileName);
                string csvFileName = string.Format("{0}.csv", Path.Combine(csvDir, instrument.FullName, name));
                if (File.Exists(csvFileName))
                {
                    logout(string.Format("Conversion \"{0}\" to \"{1}\" is done already. Skipped",
                        relativeName.Substring(1), csvFileName.Substring(csvDir.Length + 1)));
                    continue;
                }
                long nrdFileLength = new FileInfo(fileName).Length;
                totalFilesLength += nrdFileLength;
                entries.Add(new DumpEntry()
                {
                    NrdLength = nrdFileLength,
                    Instrument = instrument,
                    Date = new DateTime(
                        Convert.ToInt16(name.Substring(0, 4)),
                        Convert.ToInt16(name.Substring(4, 2)),
                        Convert.ToInt16(name.Substring(6, 2))),
                    CsvFileName = csvFileName,
                    FromName = relativeName.Substring(1),
                    ToName = csvFileName.Substring(csvDir.Length + 1),
                });
            }
        }

        private void RunConversion(List<DumpEntry> entries, int offset, int increment)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                for (int i = offset; i < entries.Count; i += increment)
                {
                    ConvertNrd(entries[i]);
                    Dispatcher.InvokeAsync(() =>
                    {
                        pbProgress.Value++;
                        completeFilesLength += entries[i].NrdLength;
                        string eta = "";
                        if (completeFilesLength > 0)
                        {
                            DateTime etaValue = new DateTime(
                                (long)((DateTime.Now.Ticks - startTimestamp.Ticks) * (totalFilesLength / completeFilesLength - 1)));
                            eta = string.Format(" ETA: {0}:{1}", etaValue.Day - 1, etaValue.ToString("HH:mm:ss"));
                        }
                        lProgress.Content = string.Format("{0} of {1} files converted ({2} of {3}){4}",
                            pbProgress.Value, entries.Count, ToBytes(completeFilesLength), ToBytes(totalFilesLength), eta);
                    });
                    if (canceling) break;
                }
                if (--taskCount == 0)
                {
                    complete();
                    if (canceling)
                    {
                        logout("Conversion canceled");
                    }
                    else
                    {
                        logout("Conversion complete");
                    }
                }
            }));
        }

        private void ConvertNrd(DumpEntry entry)
        {
            logout(string.Format("Conversion \"{0}\" to \"{1}\"...", entry.FromName, entry.ToName));

            string csvFileDir = Path.GetDirectoryName(entry.CsvFileName);
            if (!Directory.Exists(csvFileDir))
            {
                try
                {
                    Directory.CreateDirectory(csvFileDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the CSV file directory \"{0}\": {1}",
                        csvFileDir, error.ToString()));
                }
            }

            MarketReplay.DumpMarketDepth(entry.Instrument, entry.Date.AddDays(1), entry.Date.AddDays(1), entry.CsvFileName);

            logout(string.Format("Conversion \"{0}\" to \"{1}\" complete", entry.FromName, entry.ToName));
        }

        public void Restore(XDocument document, XElement element)
        {
            foreach (XElement elRoot in element.Elements())
            {
                if (elRoot.Name.LocalName.Contains("NRDToCSV"))
                {
                    XElement elCsvRootDir = elRoot.Element("CsvRootDir");
                    if (elCsvRootDir != null)
                        tbCsvRootDir.Text = elCsvRootDir.Value;

                    XElement elSelectedInstruments = elRoot.Element("SelectedInstruments");
                    if (elSelectedInstruments != null)
                        tbSelectedInstruments.Text = elSelectedInstruments.Value;
                }
            }
        }

        public void Save(XDocument document, XElement element)
        {
            element.Elements().Where(el => el.Name.LocalName.Equals("NRDToCSV")).Remove();
            XElement elRoot = new XElement("NRDToCSV");
            XElement elCsvRootDir = new XElement("CsvRootDir", tbCsvRootDir.Text);
            XElement elSelectedInstruments = new XElement("SelectedInstruments", tbSelectedInstruments.Text);
            elRoot.Add(elCsvRootDir);
            elRoot.Add(elSelectedInstruments);
            element.Add(elRoot);
        }

        public WorkspaceOptions WorkspaceOptions { get; set; }

        private void logout(string text)
        {
            Dispatcher.InvokeAsync(() =>
            {
                tbOutput.AppendText(text + Environment.NewLine);
                tbOutput.ScrollToEnd();
            });
        }

        private void run(int filesCount)
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = true;
                canceling = false;
                bConvert.IsEnabled = true;
                bConvert.Content = "_Cancel";
                tbCsvRootDir.IsReadOnly = true;
                tbSelectedInstruments.IsReadOnly = true;
                double margin = (double)FindResource("MarginBase");
                lProgress.Margin = new Thickness(0, 0, 0, 0);
                lProgress.Height = 24;
                pbProgress.Margin = new Thickness((double)FindResource("MarginBase"));
                pbProgress.Height = 16;
                pbProgress.Minimum = 0;
                pbProgress.Maximum = filesCount;
                pbProgress.Value = 0;
                startTimestamp = DateTime.Now;
            });
        }

        private void complete()
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = false;
                lProgress.Margin = new Thickness(0);
                lProgress.Height = 0;
                pbProgress.Margin = new Thickness(0);
                pbProgress.Height = 0;
                tbCsvRootDir.IsReadOnly = false;
                tbSelectedInstruments.IsReadOnly = false;
                bConvert.IsEnabled = true;
                bConvert.Content = "_Convert";
            });
        }

        public static string ToBytes(long bytes)
        {
            if (bytes < 1024) return string.Format("{0} B", bytes);
            double exp = (int)(Math.Log(bytes) / Math.Log(1024));
            return string.Format("{0:F1} {1}iB", bytes / Math.Pow(1024, exp), "KMGTPE"[(int)exp - 1]);
        }
    }

    public class DumpEntry
    {
        public long NrdLength { get; set; }
        public Cbi.Instrument Instrument { get; set; }
        public DateTime Date { get; set; }
        public string CsvFileName { get; set; }
        public string FromName { get; set; }
        public string ToName { get; set; }
    }
}