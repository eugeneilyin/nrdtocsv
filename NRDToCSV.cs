#region Using declarations
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private TextBox tbCsvRootDir;
        private TextBox tbSelectedInstruments;
        private Button bConvert;
        private TextBox tbOutput;
        private int filesCount;

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

        private DependencyObject BuildContent()
        {
            double margin = (double)FindResource("MarginBase");
            tbCsvRootDir = new TextBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                Text = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "db", "replay.csv"),
            };
            Label lCsvRootDir = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Converted _CSV root directory:",
            };
            tbSelectedInstruments = new TextBox() { Margin = new Thickness(margin, 0, margin, margin) };
            Label lSelectedInstruments = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, margin, margin, 0),
                Content = "Semicolon separated regexes to filer *.nrd file names (keep empty to proceed all):",
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
            Grid grid = new Grid() { Background = new SolidColorBrush(Colors.Transparent) };
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(lCsvRootDir, 0);
            Grid.SetRow(tbCsvRootDir, 1);
            Grid.SetRow(lSelectedInstruments, 2);
            Grid.SetRow(tbSelectedInstruments, 3);
            Grid.SetRow(bConvert, 4);
            Grid.SetRow(tbOutput, 5);
            grid.Children.Add(lCsvRootDir);
            grid.Children.Add(tbCsvRootDir);
            grid.Children.Add(lSelectedInstruments);
            grid.Children.Add(tbSelectedInstruments);
            grid.Children.Add(bConvert);
            grid.Children.Add(tbOutput);
            return grid;
        }

        private void OnConvertButtonClick(object sender, RoutedEventArgs e)
        {
            if (tbOutput == null) return;
            tbOutput.Clear();

            string nrdDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "db", "replay");
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

			Globals.RandomDispatcher.InvokeAsync(new Action(() =>  {
				run();
    	        foreach (string subDir in nrdSubDirs)
            	    ProcessDirectory(nrdDir, subDir, csvDir, selectedInstruments);
			}));
        }

        private void ProcessDirectory(string nrdRoot, string nrdDir, string csvDir, List<Regex> selectedInstruments)
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

                Collection<Instrument> instruments = NinjaTrader.Cbi.InstrumentList.GetInstruments(fullName);
                if (instruments.Count == 0)
                {
                    logout(string.Format("Unable to find an instrument name \"{0}\". Skipped", fullName));
                    continue;
                }
                else if (instruments.Count > 1)
                {
                    logout(string.Format("More than one instrument identified for name \"{0}\". Skipped", fullName));
                    continue;
                }
                Cbi.Instrument instrument = instruments[0];

                string name = Path.GetFileNameWithoutExtension(fileName);
                DateTime date = new DateTime(
                    Convert.ToInt16(name.Substring(0, 4)),
                    Convert.ToInt16(name.Substring(4, 2)),
                    Convert.ToInt16(name.Substring(6, 2)));

                string csvFileName = string.Format("{0}.csv", Path.Combine(csvDir, instrument.FullName, name));

                string csvFileDir = Path.GetDirectoryName(csvFileName);
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

                filesCount++;
				Globals.RandomDispatcher.InvokeAsync(new Action(() => 
                {
                    logout(string.Format("Convert \"{0}\" to \"{1}\"...", relativeName, csvFileName.Substring(csvDir.Length)));
                    MarketReplay.DumpMarketDepth(instrument, date.AddDays(1), date.AddDays(1), csvFileName);
                    logout(string.Format("Convert \"{0}\" to \"{1}\" complete", relativeName, csvFileName.Substring(csvDir.Length)));
					complete();
                }));
            }
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
            Dispatcher.InvokeAsync(() => {
				tbOutput.AppendText(text + Environment.NewLine);
				tbOutput.ScrollToEnd();
			});
        }
		
		private void run() {
			Dispatcher.InvokeAsync(() => {
				logout("Convers files...");
    	       	filesCount = 0;
				tbCsvRootDir.IsReadOnly = true;
				tbSelectedInstruments.IsReadOnly = true;
				bConvert.IsEnabled = false;
			});
		}

		private void complete() {
			Dispatcher.InvokeAsync(() => {
				if (--filesCount == 0) {
					logout("Conversion complete");
					tbCsvRootDir.IsEnabled = true;
					tbSelectedInstruments.IsEnabled = true;
					bConvert.IsEnabled = true;
				}
			});
		}
    }
}
