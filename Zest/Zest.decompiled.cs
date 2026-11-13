using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ImageMagick;
using Microsoft.Win32;
using Windows.System;
using Zest.App.Native;
using Zest.App.Services;
using Zest.App.UI;
using Zest.Core;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETCoreApp,Version=v8.0", FrameworkDisplayName = ".NET 8.0")]
[assembly: AssemblyCompany("Zest")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: AssemblyProduct("Zest")]
[assembly: AssemblyTitle("Zest")]
[assembly: TargetPlatform("Windows10.0.19041.0")]
[assembly: SupportedOSPlatform("Windows10.0.17763.0")]
[assembly: AssemblyVersion("1.0.0.0")]
[module: RefSafetyRules(11)]
namespace Zest.App
{
	public class App : Application
	{
		private Mutex? _singleInstance;

		private InstanceChannel? _channel;

		private TrayService? _tray;

		private DragWatcher? _watcher;

		private GlobalHotkey? _hotkey;

		private WheelWindow? _wheel;

		private bool _contentLoaded;

		internal Settings Settings { get; private set; } = new Settings();

		internal bool ShellMenuOn => ShellMenu.IsRegistered;

		internal bool DragDetectionOn => _watcher?.Enabled ?? false;

		internal bool DragFromAnyApp => Settings.DragFromAnyApp;

		protected override void OnStartup(StartupEventArgs e)
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0019: Expected Obj, but got Unknown
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			((Application)this).OnStartup(e);
			((Application)this).DispatcherUnhandledException += OnUnhandled;
			ThemeManager.Initialize();
			if (Environment.GetEnvironmentVariable("ZEST_SOFTWARE_RENDER") == "1")
			{
				RenderOptions.ProcessRenderMode = (RenderMode)1;
			}
			Settings = Zest.App.Services.Settings.Load();
			if (e.Args.Length >= 1 && e.Args[0].StartsWith("--"))
			{
				string text = e.Args[0];
				RunCommandLine(text.Substring(2, text.Length - 2), e.Args.Skip(1).ToArray());
				return;
			}
			_singleInstance = new Mutex(initiallyOwned: false, InstanceChannel.OwnerMutexName);
			if (!TryOwn(_singleInstance, 3000))
			{
				MessageBox.Show("Zest is already running. Look for the orange icon in the system tray.", "Zest");
				((Application)this).Shutdown();
				return;
			}
			_channel = new InstanceChannel(((DispatcherObject)this).Dispatcher, ExecuteCommand);
			_channel.StartListening();
			if (Settings.ContextMenu)
			{
				TryRegisterShellMenu(showError: false);
			}
			_watcher = new DragWatcher
			{
				HoldMilliseconds = Settings.HoldMilliseconds,
				AnyApp = Settings.DragFromAnyApp
			};
			_watcher.HoldDetected += OnHoldDetected;
			_watcher.Released += () =>
			{
				_wheel?.OnMouseReleased();
			};
			if (Settings.DragDetection)
			{
				TryStartWatcher();
			}
			_hotkey = new GlobalHotkey();
			_hotkey.Pressed += OnHotkey;
			bool flag = _hotkey.Register(3u, 90u);
			_tray = new TrayService(this);
			ToastWindow.Show("Zest is running", "Drag files and hold still for half a second" + (flag ? ", or select files in Explorer and press Ctrl+Alt+Z." : ". (Ctrl+Alt+Z is used by another app.)"));
		}

		private void RunCommandLine(string command, string[] args)
		{
			//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_010e: Unknown result type (might be due to invalid IL or missing references)
			((Application)this).ShutdownMode = (ShutdownMode)0;
			string[] array = args.Where(File.Exists).Select(Path.GetFullPath).ToArray();
			if (array.Length == 0)
			{
				((Application)this).Shutdown();
				return;
			}
			if (InstanceChannel.TrySend(command, array, 250))
			{
				((Application)this).Shutdown();
				return;
			}
			_singleInstance = new Mutex(initiallyOwned: false, InstanceChannel.OwnerMutexName);
			if (TryOwn(_singleInstance, 0))
			{
				_channel = new InstanceChannel(((DispatcherObject)this).Dispatcher, ExecuteCommand);
				_channel.StartListening();
				_channel.Add(command, array);
			}
			else
			{
				for (int i = 0; i < 20; i++)
				{
					Thread.Sleep(100);
					if (InstanceChannel.TrySend(command, array, 200))
					{
						((Application)this).Shutdown();
						return;
					}
				}
				ExecuteCommand(command, array);
			}
			DispatcherTimer val = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(4.0)
			};
			val.Tick += (object? _, EventArgs _) =>
			{
				if (((Application)this).Windows.Count == 0)
				{
					((Application)this).Shutdown();
				}
			};
			val.Start();
		}

		private static bool TryOwn(Mutex m, int timeoutMs)
		{
			try
			{
				return m.WaitOne(timeoutMs);
			}
			catch (AbandonedMutexException)
			{
				return true;
			}
		}

		private void ExecuteCommand(string command, IReadOnlyList<string> files)
		{
			if (command == "wheel")
			{
				NativeMethods.GetCursorPos(out var p);
				ShowWheelForFiles(files, p.X, p.Y);
			}
			else
			{
				ActionRunner.Execute(command, files);
			}
		}

		internal void SetShellMenu(bool on)
		{
			Settings.ContextMenu = on;
			Settings.Save();
			if (on)
			{
				TryRegisterShellMenu(showError: true);
				return;
			}
			try
			{
				ShellMenu.Unregister();
			}
			catch (Exception ex)
			{
				Log.Error("Remove right-click menu failed", ex);
			}
		}

		private void TryRegisterShellMenu(bool showError)
		{
			try
			{
				ShellMenu.Register(Environment.ProcessPath);
			}
			catch (Exception ex)
			{
				Log.Error("Right-click menu registration failed", ex);
				if (showError)
				{
					ToastWindow.Show("Right-click menu", "Could not add Zest to the right-click menu: " + ex.Message, null, isError: true);
				}
			}
		}

		private void OnHoldDetected(int x, int y)
		{
			WheelWindow wheel = _wheel;
			if (wheel == null || !((UIElement)wheel).IsVisible)
			{
				_wheel = CreateWheel();
				_wheel.ShowProbe(x, y);
			}
		}

		private void OnHotkey()
		{
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			List<string> selectedFiles = ExplorerSelection.GetSelectedFiles(NativeMethods.GetForegroundWindow(), out string problem);
			if (selectedFiles.Count == 0)
			{
				ToastWindow.Show("Nothing selected", problem ?? "Select files in File Explorer first.");
				return;
			}
			if ((int)FileKinds.DetectGroup((IReadOnlyList<string>)selectedFiles) == 0)
			{
				ToastWindow.Show("Not supported", "Select only one kind: images, PDFs, or videos.");
				return;
			}
			NativeMethods.GetCursorPos(out var p);
			ShowWheelForFiles(selectedFiles, p.X, p.Y);
		}

		internal void ShowWheelForFiles(IReadOnlyList<string> files, int x, int y)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			if ((int)FileKinds.DetectGroup(files) == 0)
			{
				ToastWindow.Show("Not supported", "Zest works with images (JPG, PNG, HEIC, WEBP…), PDF files and videos (MP4, MOV…). Select only one kind at a time.");
				return;
			}
			_wheel?.CloseWheel();
			_wheel = CreateWheel();
			_wheel.ShowForFiles(files, x, y);
		}

		private WheelWindow CreateWheel()
		{
			WheelWindow w = new WheelWindow();
			w.ActionChosen += (SliceDef slice, IReadOnlyList<string> files) =>
			{
				ActionRunner.Run(slice, files);
			};
			((Window)w).Closed += (object? _, EventArgs _) =>
			{
				if (_wheel == w)
				{
					_wheel = null;
				}
			};
			return w;
		}

		internal void SetDragFromAnyApp(bool on)
		{
			Settings.DragFromAnyApp = on;
			Settings.Save();
			if (_watcher != null)
			{
				_watcher.AnyApp = on;
			}
		}

		internal void SetDragDetection(bool on)
		{
			Settings.DragDetection = on;
			Settings.Save();
			if (on)
			{
				TryStartWatcher();
			}
			else
			{
				_watcher?.Stop();
			}
		}

		private void TryStartWatcher()
		{
			try
			{
				_watcher?.Start();
			}
			catch (Exception ex)
			{
				Log.Error("Mouse hook failed", ex);
				ToastWindow.Show("Drag detection is off", "Windows did not allow the mouse hook. Use Ctrl+Alt+Z instead.", null, isError: true);
			}
		}

		private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			Log.Error("Unhandled error", e.Exception);
			ToastWindow.Show("Something went wrong", e.Exception.Message, null, isError: true);
			e.Handled = true;
		}

		protected override void OnExit(ExitEventArgs e)
		{
			_channel?.Dispose();
			_watcher?.Dispose();
			_hotkey?.Dispose();
			_tray?.Dispose();
			_singleInstance?.Dispose();
			((Application)this).OnExit(e);
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/app.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[STAThread]
		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public static void Main()
		{
			App app = new App();
			app.InitializeComponent();
			((Application)app).Run();
		}
	}
}
namespace Zest.App.UI
{
	public class AddBgDialog : Window, IComponentConnector
	{
		private static readonly string[] Palette = new string[12]
		{
			"#FFFFFF", "#000000", "#F3F0EA", "#FF8A1F", "#FFB347", "#E8445A", "#7C4DFF", "#2F80ED", "#27AE60", "#1ABC9C",
			"#F2C94C", "#4F4F4F"
		};

		private readonly string _path;

		private readonly LivePreview _preview;

		private bool _loaded;

		internal Border Root;

		internal TextBlock FileName;

		internal TextBlock Loading;

		internal Image PreviewImage;

		internal RadioButton SolidChip;

		internal RadioButton GradientChip;

		internal RadioButton BlurChip;

		internal StackPanel Color1Panel;

		internal TextBlock Color1Label;

		internal WrapPanel Swatches1;

		internal TextBox Color1Box;

		internal StackPanel Color2Panel;

		internal WrapPanel Swatches2;

		internal TextBox Color2Box;

		internal RadioButton DirV;

		internal RadioButton DirH;

		internal RadioButton DirD;

		internal StackPanel BlurPanel;

		internal TextBlock BlurLabel;

		internal Slider BlurSlider;

		internal TextBlock Note;

		internal Button SaveButton;

		private bool _contentLoaded;

		public AddBgDialog(string path)
		{
			//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ba: Expected Obj, but got Unknown
			//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00db: Expected Obj, but got Unknown
			//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Expected Obj, but got Unknown
			//IL_0116: Unknown result type (might be due to invalid IL or missing references)
			//IL_0120: Expected Obj, but got Unknown
			//IL_013a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0144: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			_preview = new LivePreview(PreviewImage);
			AddSwatches(Swatches1, Color1Box);
			AddSwatches(Swatches2, Color2Box);
			RadioButton[] array = new RadioButton[6] { SolidChip, GradientChip, BlurChip, DirV, DirH, DirD };
			for (int i = 0; i < array.Length; i++)
			{
				((ToggleButton)array[i]).Checked += (object _, RoutedEventArgs _) =>
				{
					Changed();
				};
			}
			((TextBoxBase)Color1Box).TextChanged += (object _, TextChangedEventArgs _) =>
			{
				Changed();
			};
			((TextBoxBase)Color2Box).TextChanged += (object _, TextChangedEventArgs _) =>
			{
				Changed();
			};
			((RangeBase)BlurSlider).ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> _) =>
			{
				Changed();
			};
			((ButtonBase)SaveButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await SaveAsync();
			};
			((Window)this).Closed += (object? _, EventArgs _) =>
			{
				_preview.Dispose();
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
		}

		private void AddSwatches(WrapPanel panel, TextBox box)
		{
			//IL_0030: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0093: Unknown result type (might be due to invalid IL or missing references)
			//IL_009d: Expected Obj, but got Unknown
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00de: Expected Obj, but got Unknown
			//IL_00e0: Expected Obj, but got Unknown
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f3: Expected Obj, but got Unknown
			string[] palette = Palette;
			foreach (string hex in palette)
			{
				Button val = new Button
				{
					Width = 24.0,
					Height = 24.0,
					Margin = new Thickness(3.0, 3.0, 8.0, 8.0),
					Background = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
					BorderBrush = (Brush)(object)Brushes.Transparent,
					BorderThickness = new Thickness(0.0),
					ToolTip = hex,
					Style = (Style)((FrameworkElement)this).FindResource((object)"Swatch")
				};
				((ButtonBase)val).Click += (object _, RoutedEventArgs _) =>
				{
					box.Text = hex;
				};
				((Panel)panel).Children.Add((UIElement)(object)val);
			}
		}

		private async Task LoadAsync()
		{
			try
			{
				await _preview.LoadAsync(_path);
				((UIElement)Loading).Visibility = (Visibility)2;
				_loaded = true;
				if (!BackgroundFiller.HasTransparency(_preview.Base))
				{
					Note.Text = "This image has no transparent parts, so a background will not be visible. It works best with PNG logos, stickers and cut-out photos.";
				}
				Changed();
			}
			catch (Exception ex)
			{
				Log.Error("Add BG preview failed: " + _path, ex);
				Loading.Text = "Could not open this image: " + ex.Message;
			}
		}

		private static bool IsHex(string s)
		{
			return Regex.IsMatch(s.Trim(), "^#?([0-9a-fA-F]{6})$");
		}

		private static string Hex(string s)
		{
			s = s.Trim();
			if (!s.StartsWith('#'))
			{
				return "#" + s;
			}
			return s;
		}

		private BgSettings? Current()
		{
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Invalid comparison between Unknown and I4
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Invalid comparison between Unknown and I4
			//IL_0093: Unknown result type (might be due to invalid IL or missing references)
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Expected Obj, but got Unknown
			BgMode val = (BgMode)((((ToggleButton)GradientChip).IsChecked == true) ? 1 : ((((ToggleButton)BlurChip).IsChecked == true) ? 2 : 0));
			if ((int)val != 2 && !IsHex(Color1Box.Text))
			{
				return null;
			}
			if ((int)val == 1 && !IsHex(Color2Box.Text))
			{
				return null;
			}
			GradientDirection val2 = (GradientDirection)((((ToggleButton)DirH).IsChecked == true) ? 1 : ((((ToggleButton)DirD).IsChecked == true) ? 2 : 0));
			return new BgSettings(val, IsHex(Color1Box.Text) ? Hex(Color1Box.Text) : "#FFFFFF", IsHex(Color2Box.Text) ? Hex(Color2Box.Text) : "#FFB347", val2, (int)((RangeBase)BlurSlider).Value);
		}

		private void Changed()
		{
			if (Color1Panel == null)
			{
				return;
			}
			bool valueOrDefault = ((ToggleButton)GradientChip).IsChecked == true;
			bool valueOrDefault2 = ((ToggleButton)BlurChip).IsChecked == true;
			((UIElement)Color1Panel).Visibility = (Visibility)(valueOrDefault2 ? 2 : 0);
			Color1Label.Text = (valueOrDefault ? "From color" : "Color");
			((UIElement)Color2Panel).Visibility = (Visibility)((!valueOrDefault) ? 2 : 0);
			((UIElement)BlurPanel).Visibility = (Visibility)((!valueOrDefault2) ? 2 : 0);
			BlurLabel.Text = $"Blur strength: {((RangeBase)BlurSlider).Value:0}";
			BgSettings s = Current();
			((UIElement)SaveButton).IsEnabled = _loaded && s != (BgSettings)null;
			if (_loaded && !(s == (BgSettings)null))
			{
				_preview.Update((MagickImage img) => BackgroundFiller.Compose(img, s));
			}
		}

		private async Task SaveAsync()
		{
			BgSettings s = Current();
			if (s == (BgSettings)null)
			{
				return;
			}
			((UIElement)SaveButton).IsEnabled = false;
			try
			{
				string text = await Task.Run(() => BackgroundFiller.Save(_path, s));
				ToastWindow.Show("Background added", Path.GetFileName(text), new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Add BG failed: " + _path, ex);
				ToastWindow.Show("Add background failed", ex.Message, null, isError: true);
				((UIElement)SaveButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/addbgdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Expected Obj, but got Unknown
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007e: Expected Obj, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Expected Obj, but got Unknown
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0098: Expected Obj, but got Unknown
			//IL_009b: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a5: Expected Obj, but got Unknown
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b2: Expected Obj, but got Unknown
			//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bf: Expected Obj, but got Unknown
			//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cc: Expected Obj, but got Unknown
			//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d9: Expected Obj, but got Unknown
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e6: Expected Obj, but got Unknown
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f3: Expected Obj, but got Unknown
			//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0100: Expected Obj, but got Unknown
			//IL_0103: Unknown result type (might be due to invalid IL or missing references)
			//IL_010d: Expected Obj, but got Unknown
			//IL_0110: Unknown result type (might be due to invalid IL or missing references)
			//IL_011a: Expected Obj, but got Unknown
			//IL_011d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0127: Expected Obj, but got Unknown
			//IL_012a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0134: Expected Obj, but got Unknown
			//IL_0137: Unknown result type (might be due to invalid IL or missing references)
			//IL_0141: Expected Obj, but got Unknown
			//IL_0144: Unknown result type (might be due to invalid IL or missing references)
			//IL_014e: Expected Obj, but got Unknown
			//IL_0151: Unknown result type (might be due to invalid IL or missing references)
			//IL_015b: Expected Obj, but got Unknown
			//IL_015e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0168: Expected Obj, but got Unknown
			//IL_016b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0175: Expected Obj, but got Unknown
			//IL_0178: Unknown result type (might be due to invalid IL or missing references)
			//IL_0182: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				Loading = (TextBlock)target;
				break;
			case 4:
				PreviewImage = (Image)target;
				break;
			case 5:
				SolidChip = (RadioButton)target;
				break;
			case 6:
				GradientChip = (RadioButton)target;
				break;
			case 7:
				BlurChip = (RadioButton)target;
				break;
			case 8:
				Color1Panel = (StackPanel)target;
				break;
			case 9:
				Color1Label = (TextBlock)target;
				break;
			case 10:
				Swatches1 = (WrapPanel)target;
				break;
			case 11:
				Color1Box = (TextBox)target;
				break;
			case 12:
				Color2Panel = (StackPanel)target;
				break;
			case 13:
				Swatches2 = (WrapPanel)target;
				break;
			case 14:
				Color2Box = (TextBox)target;
				break;
			case 15:
				DirV = (RadioButton)target;
				break;
			case 16:
				DirH = (RadioButton)target;
				break;
			case 17:
				DirD = (RadioButton)target;
				break;
			case 18:
				BlurPanel = (StackPanel)target;
				break;
			case 19:
				BlurLabel = (TextBlock)target;
				break;
			case 20:
				BlurSlider = (Slider)target;
				break;
			case 21:
				Note = (TextBlock)target;
				break;
			case 22:
				SaveButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	public class AnnotateDialog : Window, IComponentConnector
	{
		private static readonly string[] Colors = new string[7] { "#E53935", "#FF8A1F", "#FFD600", "#43A047", "#1E88E5", "#000000", "#FFFFFF" };

		private readonly string _path;

		private readonly List<Annotation> _items = new List<Annotation>();

		private double _imgW;

		private double _imgH;

		private double _baseWidth = 4.0;

		private string _color = Colors[0];

		private Annotation? _draft;

		private List<Pt>? _penPoints;

		private Pt _start;

		private TextBox? _textEditor;

		private Pt _textAt;

		internal Border Root;

		internal TextBlock FileName;

		internal RadioButton PenTool;

		internal RadioButton ArrowTool;

		internal RadioButton HighlightTool;

		internal RadioButton TextTool;

		internal StackPanel Swatches;

		internal Slider SizeSlider;

		internal TextBlock Loading;

		internal Viewbox View;

		internal Canvas Stage;

		internal Image Img;

		internal Canvas Layer;

		internal Canvas EditorLayer;

		internal Button UndoButton;

		internal Button ClearButton;

		internal Button SaveButton;

		internal TextBlock Hint;

		private bool _contentLoaded;

		private double StrokeWidth => Math.Round(_baseWidth * ((RangeBase)SizeSlider).Value, 1);

		private double TextSize => Math.Round(_baseWidth * ((RangeBase)SizeSlider).Value * 7.0);

		private double Scale
		{
			get
			{
				if (!(_imgW <= 0.0))
				{
					return Math.Max(0.0001, Math.Min(((FrameworkElement)View).ActualWidth / _imgW, ((FrameworkElement)View).ActualHeight / _imgH));
				}
				return 1.0;
			}
		}

		public AnnotateDialog(string path)
		{
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Expected Obj, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Expected Obj, but got Unknown
			//IL_0098: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a2: Expected Obj, but got Unknown
			//IL_00db: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e5: Expected Obj, but got Unknown
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0106: Expected Obj, but got Unknown
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			//IL_011d: Expected Obj, but got Unknown
			//IL_012a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0134: Expected Obj, but got Unknown
			//IL_013c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0146: Expected Obj, but got Unknown
			//IL_014e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0158: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			BuildSwatches();
			((UIElement)Stage).MouseLeftButtonDown += OnDown;
			((UIElement)Stage).MouseMove += OnMove;
			((UIElement)Stage).MouseLeftButtonUp += OnUp;
			RadioButton[] array = new RadioButton[4] { PenTool, ArrowTool, HighlightTool, TextTool };
			for (int i = 0; i < array.Length; i++)
			{
				((ToggleButton)array[i]).Checked += (object _, RoutedEventArgs _) =>
				{
					ToolChanged();
				};
			}
			((ButtonBase)UndoButton).Click += (object _, RoutedEventArgs _) =>
			{
				Undo();
			};
			((ButtonBase)ClearButton).Click += (object _, RoutedEventArgs _) =>
			{
				CommitText();
				_items.Clear();
				Redraw();
			};
			((ButtonBase)SaveButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await SaveAsync();
			};
			((UIElement)this).PreviewKeyDown += OnKey;
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
			ToolChanged();
		}

		private async Task LoadAsync()
		{
			try
			{
				(BitmapSource, int, int) tuple = await Task.Run(() => PreviewLoader.Load(_path));
				BitmapSource item = tuple.Item1;
				int item2 = tuple.Item2;
				int item3 = tuple.Item3;
				_imgW = item2;
				_imgH = item3;
				_baseWidth = Annotator.DefaultWidth((double)item2, (double)item3);
				Img.Source = (ImageSource)(object)item;
				Image img = Img;
				Canvas stage = Stage;
				Canvas layer = Layer;
				double num = (((FrameworkElement)EditorLayer).Width = item2);
				double num3 = (((FrameworkElement)layer).Width = num);
				double width = (((FrameworkElement)stage).Width = num3);
				((FrameworkElement)img).Width = width;
				Image img2 = Img;
				Canvas stage2 = Stage;
				Canvas layer2 = Layer;
				num = (((FrameworkElement)EditorLayer).Height = item3);
				num3 = (((FrameworkElement)layer2).Height = num);
				width = (((FrameworkElement)stage2).Height = num3);
				((FrameworkElement)img2).Height = width;
				((UIElement)Loading).Visibility = (Visibility)2;
				((UIElement)Stage).Visibility = (Visibility)0;
			}
			catch (Exception ex)
			{
				Log.Error("Annotate preview failed: " + _path, ex);
				Loading.Text = "Could not open this image: " + ex.Message;
			}
		}

		private void BuildSwatches()
		{
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			//IL_0090: Expected Obj, but got Unknown
			//IL_0090: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Expected Obj, but got Unknown
			//IL_00bf: Expected Obj, but got Unknown
			//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d1: Expected Obj, but got Unknown
			string[] colors = Colors;
			foreach (string hex in colors)
			{
				Button val = new Button
				{
					Width = 22.0,
					Height = 22.0,
					Margin = new Thickness(3.0, 0.0, 9.0, 0.0),
					Background = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
					ToolTip = hex,
					Tag = hex,
					Style = (Style)((FrameworkElement)this).FindResource((object)"Swatch")
				};
				((ButtonBase)val).Click += (object _, RoutedEventArgs _) =>
				{
					_color = hex;
					HighlightSwatch();
				};
				((Panel)Swatches).Children.Add((UIElement)(object)val);
			}
			HighlightSwatch();
		}

		private void HighlightSwatch()
		{
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_004b: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			foreach (Button child in ((Panel)Swatches).Children)
			{
				bool flag = (string)((FrameworkElement)child).Tag == _color;
				((Control)child).BorderBrush = (Brush)(object)(flag ? ((SolidColorBrush)(Brush)((FrameworkElement)this).FindResource((object)"Accent")) : Brushes.Transparent);
				((Control)child).BorderThickness = new Thickness((double)(flag ? 2 : 0));
			}
			if (_textEditor != null)
			{
				((Control)_textEditor).Foreground = (Brush)(object)Brush(_color);
			}
		}

		private void ToolChanged()
		{
			if (Hint != null)
			{
				CommitText();
				((FrameworkElement)Stage).Cursor = ((((ToggleButton)TextTool).IsChecked == true) ? Cursors.IBeam : Cursors.Cross);
				? val = Hint;
				string text;
				if (((ToggleButton)TextTool).IsChecked == true)
				{
					text = "Click where the text should start, type, then press Enter.";
				}
				else if (((ToggleButton)HighlightTool).IsChecked == true)
				{
					text = "Drag over the area you want to highlight.";
				}
				else
				{
					text = ((((ToggleButton)ArrowTool).IsChecked == true) ? "Drag from the start to where the arrow should point." : "Hold the mouse button and draw.");
				}
				((TextBlock)val).Text = text;
				bool flag = ((ToggleButton)HighlightTool).IsChecked == true;
				if (flag)
				{
					string color = _color;
					bool flag2 = ((color == "#E53935" || color == "#000000") ? true : false);
					flag = flag2;
				}
				if (flag)
				{
					_color = "#FFD600";
					HighlightSwatch();
				}
			}
		}

		private Pt Pos(MouseEventArgs e)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			Point position = e.GetPosition((IInputElement)(object)Stage);
			return new Pt(Math.Clamp(position.X, 0.0, _imgW), Math.Clamp(position.Y, 0.0, _imgH));
		}

		private void OnDown(object sender, MouseButtonEventArgs e)
		{
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_0093: Unknown result type (might be due to invalid IL or missing references)
			//IL_009d: Expected Obj, but got Unknown
			if (_imgW <= 0.0 || ((RoutedEventArgs)e).OriginalSource is TextBox)
			{
				return;
			}
			Pt val = Pos((MouseEventArgs)(object)e);
			if (((ToggleButton)TextTool).IsChecked == true)
			{
				CommitText();
				StartText(val);
				((RoutedEventArgs)e).Handled = true;
				return;
			}
			_start = val;
			if (((ToggleButton)PenTool).IsChecked == true)
			{
				_penPoints = new List<Pt> { val };
				_draft = (Annotation?)new PenStroke((IReadOnlyList<Pt>)_penPoints, _color, StrokeWidth);
			}
			((UIElement)Stage).CaptureMouse();
			Redraw();
		}

		private void OnMove(object sender, MouseEventArgs e)
		{
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ef: Expected Obj, but got Unknown
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0168: Unknown result type (might be due to invalid IL or missing references)
			//IL_0173: Unknown result type (might be due to invalid IL or missing references)
			//IL_017d: Expected Obj, but got Unknown
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b7: Expected Obj, but got Unknown
			if (!((UIElement)Stage).IsMouseCaptured)
			{
				return;
			}
			Pt val = Pos(e);
			if (((ToggleButton)PenTool).IsChecked == true && _penPoints != null)
			{
				List<Pt>? penPoints = _penPoints;
				Pt val2 = penPoints[penPoints.Count - 1];
				if (Math.Abs(val.X - val2.X) + Math.Abs(val.Y - val2.Y) < 1.5 / Scale)
				{
					return;
				}
				_penPoints.Add(val);
				_draft = (Annotation?)new PenStroke((IReadOnlyList<Pt>)_penPoints.ToList(), _color, StrokeWidth);
			}
			else if (((ToggleButton)ArrowTool).IsChecked == true)
			{
				_draft = (Annotation?)new ArrowShape(_start, val, _color, StrokeWidth);
			}
			else if (((ToggleButton)HighlightTool).IsChecked == true)
			{
				_draft = (Annotation?)new HighlightBox(new CropBox(Math.Min(_start.X, val.X), Math.Min(_start.Y, val.Y), Math.Abs(val.X - _start.X), Math.Abs(val.Y - _start.Y)), _color);
			}
			Redraw();
		}

		private void OnUp(object sender, MouseButtonEventArgs e)
		{
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0092: Unknown result type (might be due to invalid IL or missing references)
			if (!((UIElement)Stage).IsMouseCaptured)
			{
				return;
			}
			((UIElement)Stage).ReleaseMouseCapture();
			Annotation draft = _draft;
			bool flag;
			if (!(draft is PenStroke))
			{
				ArrowShape val = (ArrowShape)(object)((draft is ArrowShape) ? draft : null);
				if (val == null)
				{
					HighlightBox val2 = (HighlightBox)(object)((draft is HighlightBox) ? draft : null);
					if (val2 != null)
					{
						CropBox box = val2.Box;
						int num;
						if (box.W * Scale >= 4.0)
						{
							box = val2.Box;
							num = ((box.H * Scale >= 4.0) ? 1 : 0);
						}
						else
						{
							num = 0;
						}
						flag = (byte)num != 0;
					}
					else
					{
						flag = false;
					}
				}
				else
				{
					flag = Dist(val.From, val.To) * Scale >= 8.0;
				}
			}
			else
			{
				flag = true;
			}
			if (flag)
			{
				_items.Add(_draft);
			}
			_draft = null;
			_penPoints = null;
			Redraw();
		}

		private static double Dist(Pt a, Pt b)
		{
			return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
		}

		private void StartText(Pt p)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Expected Obj, but got Unknown
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Expected Obj, but got Unknown
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Expected Obj, but got Unknown
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c2: Expected Obj, but got Unknown
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0114: Expected Obj, but got Unknown
			_textAt = p;
			_textEditor = new TextBox
			{
				FontFamily = new FontFamily("Segoe UI"),
				FontSize = TextSize,
				Foreground = (Brush)(object)Brush(_color),
				Background = (Brush)new SolidColorBrush(Color.FromArgb((byte)48, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				BorderBrush = (Brush)((FrameworkElement)this).FindResource((object)"Brand"),
				BorderThickness = new Thickness(1.5 / Scale),
				Padding = new Thickness(0.0),
				MinWidth = TextSize * 2.0,
				Style = null
			};
			Canvas.SetLeft((UIElement)(object)_textEditor, p.X);
			Canvas.SetTop((UIElement)(object)_textEditor, p.Y);
			((Panel)EditorLayer).Children.Add((UIElement)(object)_textEditor);
			((FrameworkElement)_textEditor).Loaded += (object _, RoutedEventArgs _) =>
			{
				TextBox? textEditor = _textEditor;
				if (textEditor != null)
				{
					((UIElement)textEditor).Focus();
				}
			};
		}

		private void CommitText()
		{
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Expected Obj, but got Unknown
			if (_textEditor != null)
			{
				string text = _textEditor.Text.Trim();
				double fontSize = ((Control)_textEditor).FontSize;
				((Panel)EditorLayer).Children.Clear();
				_textEditor = null;
				if (text.Length > 0)
				{
					_items.Add((Annotation)new TextNote(_textAt, text, _color, fontSize));
				}
				Redraw();
			}
		}

		private void OnKey(object sender, KeyEventArgs e)
		{
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_0050: Invalid comparison between Unknown and I4
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Invalid comparison between Unknown and I4
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0061: Invalid comparison between Unknown and I4
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Invalid comparison between Unknown and I4
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			//IL_0069: Invalid comparison between Unknown and I4
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Invalid comparison between Unknown and I4
			//IL_0095: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Invalid comparison between Unknown and I4
			//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b7: Invalid comparison between Unknown and I4
			//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Invalid comparison between Unknown and I4
			if (_textEditor != null)
			{
				if ((int)e.Key == 6)
				{
					CommitText();
					((RoutedEventArgs)e).Handled = true;
				}
				else if ((int)e.Key == 13)
				{
					((Panel)EditorLayer).Children.Clear();
					_textEditor = null;
					((RoutedEventArgs)e).Handled = true;
				}
			}
			else if ((int)e.Key == 13)
			{
				((Window)this).Close();
			}
			else if ((int)e.Key == 69 && (int)Keyboard.Modifiers == 2)
			{
				Undo();
			}
			else if ((int)Keyboard.Modifiers == 0)
			{
				if ((int)e.Key == 59)
				{
					((ToggleButton)PenTool).IsChecked = true;
				}
				if ((int)e.Key == 44)
				{
					((ToggleButton)ArrowTool).IsChecked = true;
				}
				if ((int)e.Key == 51)
				{
					((ToggleButton)HighlightTool).IsChecked = true;
				}
				if ((int)e.Key == 63)
				{
					((ToggleButton)TextTool).IsChecked = true;
				}
			}
		}

		private void Undo()
		{
			if (_items.Count != 0)
			{
				_items.RemoveAt(_items.Count - 1);
				Redraw();
			}
		}

		private static SolidColorBrush Brush(string hex)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Expected Obj, but got Unknown
			return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		}

		private void Redraw()
		{
			((Panel)Layer).Children.Clear();
			IEnumerable<Annotation> enumerable;
			if (!(_draft == (Annotation)null))
			{
				enumerable = _items.Append(_draft);
			}
			else
			{
				IEnumerable<Annotation> items = _items;
				enumerable = items;
			}
			foreach (Annotation item in enumerable)
			{
				((Panel)Layer).Children.Add(Visual(item));
			}
			((UIElement)SaveButton).IsEnabled = _items.Count > 0;
			((UIElement)UndoButton).IsEnabled = _items.Count > 0;
		}

		private static UIElement Visual(Annotation a)
		{
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a1: Expected Obj, but got Unknown
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cd: Expected Obj, but got Unknown
			//IL_00ce: Expected Obj, but got Unknown
			//IL_013c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0142: Unknown result type (might be due to invalid IL or missing references)
			//IL_0153: Unknown result type (might be due to invalid IL or missing references)
			//IL_0158: Unknown result type (might be due to invalid IL or missing references)
			//IL_015b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0160: Unknown result type (might be due to invalid IL or missing references)
			//IL_0163: Unknown result type (might be due to invalid IL or missing references)
			//IL_0168: Unknown result type (might be due to invalid IL or missing references)
			//IL_016a: Unknown result type (might be due to invalid IL or missing references)
			//IL_016f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0171: Unknown result type (might be due to invalid IL or missing references)
			//IL_0176: Unknown result type (might be due to invalid IL or missing references)
			//IL_017c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0181: Unknown result type (might be due to invalid IL or missing references)
			//IL_0183: Unknown result type (might be due to invalid IL or missing references)
			//IL_0188: Unknown result type (might be due to invalid IL or missing references)
			//IL_0196: Unknown result type (might be due to invalid IL or missing references)
			//IL_0198: Unknown result type (might be due to invalid IL or missing references)
			//IL_019d: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f5: Expected Obj, but got Unknown
			//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0201: Unknown result type (might be due to invalid IL or missing references)
			//IL_0202: Unknown result type (might be due to invalid IL or missing references)
			//IL_0207: Unknown result type (might be due to invalid IL or missing references)
			//IL_0216: Unknown result type (might be due to invalid IL or missing references)
			//IL_0220: Unknown result type (might be due to invalid IL or missing references)
			//IL_022f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0239: Unknown result type (might be due to invalid IL or missing references)
			//IL_0248: Unknown result type (might be due to invalid IL or missing references)
			//IL_0257: Expected Obj, but got Unknown
			//IL_0257: Unknown result type (might be due to invalid IL or missing references)
			//IL_026d: Expected Obj, but got Unknown
			//IL_026f: Expected Obj, but got Unknown
			//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
			//IL_0108: Expected Obj, but got Unknown
			//IL_0108: Unknown result type (might be due to invalid IL or missing references)
			//IL_0119: Unknown result type (might be due to invalid IL or missing references)
			//IL_0125: Unknown result type (might be due to invalid IL or missing references)
			//IL_012c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0133: Unknown result type (might be due to invalid IL or missing references)
			//IL_013b: Expected Obj, but got Unknown
			//IL_026f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0274: Unknown result type (might be due to invalid IL or missing references)
			//IL_0276: Unknown result type (might be due to invalid IL or missing references)
			//IL_027b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0289: Unknown result type (might be due to invalid IL or missing references)
			//IL_028b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0290: Unknown result type (might be due to invalid IL or missing references)
			//IL_029e: Unknown result type (might be due to invalid IL or missing references)
			//IL_02af: Unknown result type (might be due to invalid IL or missing references)
			//IL_02be: Unknown result type (might be due to invalid IL or missing references)
			//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d3: Expected Obj, but got Unknown
			//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_02da: Unknown result type (might be due to invalid IL or missing references)
			//IL_02e8: Expected Obj, but got Unknown
			//IL_02e9: Expected Obj, but got Unknown
			//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
			//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0300: Unknown result type (might be due to invalid IL or missing references)
			//IL_030a: Expected Obj, but got Unknown
			//IL_030a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0316: Unknown result type (might be due to invalid IL or missing references)
			//IL_0327: Unknown result type (might be due to invalid IL or missing references)
			//IL_0329: Unknown result type (might be due to invalid IL or missing references)
			//IL_032e: Unknown result type (might be due to invalid IL or missing references)
			//IL_033c: Expected Obj, but got Unknown
			//IL_033c: Unknown result type (might be due to invalid IL or missing references)
			//IL_033e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0343: Unknown result type (might be due to invalid IL or missing references)
			//IL_0351: Expected Obj, but got Unknown
			//IL_0352: Expected Obj, but got Unknown
			//IL_0352: Unknown result type (might be due to invalid IL or missing references)
			//IL_0358: Expected Obj, but got Unknown
			PenStroke val = (PenStroke)(object)((a is PenStroke) ? a : null);
			Pt val6;
			if (val == null)
			{
				ArrowShape val2 = (ArrowShape)(object)((a is ArrowShape) ? a : null);
				if (val2 == null)
				{
					HighlightBox val3 = (HighlightBox)(object)((a is HighlightBox) ? a : null);
					if (val3 == null)
					{
						TextNote val4 = (TextNote)(object)((a is TextNote) ? a : null);
						if (val4 != null)
						{
							TextBlock val5 = new TextBlock
							{
								Text = val4.Text,
								FontFamily = new FontFamily("Segoe UI"),
								FontSize = val4.Size,
								Foreground = (Brush)(object)Brush(((Annotation)val4).Color)
							};
							val6 = val4.At;
							Canvas.SetLeft((UIElement)val5, val6.X);
							val6 = val4.At;
							Canvas.SetTop((UIElement)val5, val6.Y);
							return (UIElement)val5;
						}
						return (UIElement)new Canvas();
					}
					Rectangle val7 = new Rectangle();
					CropBox box = val3.Box;
					((FrameworkElement)val7).Width = box.W;
					box = val3.Box;
					((FrameworkElement)val7).Height = box.H;
					((Shape)val7).Fill = (Brush)(object)Brush(((Annotation)val3).Color);
					((UIElement)val7).Opacity = 0.4;
					box = val3.Box;
					Canvas.SetLeft((UIElement)val7, box.X);
					box = val3.Box;
					Canvas.SetTop((UIElement)val7, box.Y);
					return (UIElement)val7;
				}
				(Pt, Pt, Pt, Pt) tuple = ArrowGeometry.Head(val2.From, val2.To, val2.Width);
				Pt item = tuple.Item1;
				Pt item2 = tuple.Item2;
				Pt item3 = tuple.Item3;
				Pt item4 = tuple.Item4;
				Canvas val8 = new Canvas();
				UIElementCollection children = ((Panel)val8).Children;
				Line val9 = new Line();
				val6 = val2.From;
				val9.X1 = val6.X;
				val6 = val2.From;
				val9.Y1 = val6.Y;
				val9.X2 = item4.X;
				val9.Y2 = item4.Y;
				((Shape)val9).Stroke = (Brush)(object)Brush(((Annotation)val2).Color);
				((Shape)val9).StrokeThickness = val2.Width;
				((Shape)val9).StrokeStartLineCap = (PenLineCap)2;
				((Shape)val9).StrokeEndLineCap = (PenLineCap)2;
				children.Add((UIElement)val9);
				UIElementCollection children2 = ((Panel)val8).Children;
				Polygon val10 = new Polygon();
				PointCollection val11 = new PointCollection();
				val11.Add(new Point(item.X, item.Y));
				val11.Add(new Point(item2.X, item2.Y));
				val11.Add(new Point(item3.X, item3.Y));
				val10.Points = val11;
				((Shape)val10).Fill = (Brush)(object)Brush(((Annotation)val2).Color);
				children2.Add((UIElement)val10);
				return (UIElement)val8;
			}
			if (val.Points.Count == 1)
			{
				Ellipse val12 = new Ellipse
				{
					Width = val.Width,
					Height = val.Width,
					Fill = (Brush)(object)Brush(((Annotation)val).Color)
				};
				val6 = val.Points[0];
				Canvas.SetLeft((UIElement)val12, val6.X - val.Width / 2.0);
				val6 = val.Points[0];
				Canvas.SetTop((UIElement)val12, val6.Y - val.Width / 2.0);
				return (UIElement)val12;
			}
			return (UIElement)new Polyline
			{
				Points = new PointCollection(((IEnumerable<Pt>)val.Points).Select((Func<Pt, Point>)((Pt q) =>
				{
					//IL_000e: Unknown result type (might be due to invalid IL or missing references)
					return new Point(q.X, q.Y);
				}))),
				Stroke = (Brush)(object)Brush(((Annotation)val).Color),
				StrokeThickness = val.Width,
				StrokeLineJoin = (PenLineJoin)2,
				StrokeStartLineCap = (PenLineCap)2,
				StrokeEndLineCap = (PenLineCap)2
			};
		}

		private async Task SaveAsync()
		{
			CommitText();
			((UIElement)SaveButton).IsEnabled = false;
			List<Annotation> items = _items.ToList();
			try
			{
				string text = await Task.Run(() => Annotator.Save(_path, (IReadOnlyList<Annotation>)items));
				ToastWindow.Show("Annotated", Path.GetFileName(text), new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Annotate failed: " + _path, ex);
				ToastWindow.Show("Annotate failed", ex.Message, null, isError: true);
				((UIElement)SaveButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/annotatedialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_0057: Unknown result type (might be due to invalid IL or missing references)
			//IL_0061: Expected Obj, but got Unknown
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_006e: Expected Obj, but got Unknown
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_007b: Expected Obj, but got Unknown
			//IL_007e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0088: Expected Obj, but got Unknown
			//IL_008b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0095: Expected Obj, but got Unknown
			//IL_0098: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a2: Expected Obj, but got Unknown
			//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00af: Expected Obj, but got Unknown
			//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bc: Expected Obj, but got Unknown
			//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c9: Expected Obj, but got Unknown
			//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d6: Expected Obj, but got Unknown
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Expected Obj, but got Unknown
			//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f0: Expected Obj, but got Unknown
			//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fd: Expected Obj, but got Unknown
			//IL_0100: Unknown result type (might be due to invalid IL or missing references)
			//IL_010a: Expected Obj, but got Unknown
			//IL_010d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0117: Expected Obj, but got Unknown
			//IL_011a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0124: Expected Obj, but got Unknown
			//IL_0127: Unknown result type (might be due to invalid IL or missing references)
			//IL_0131: Expected Obj, but got Unknown
			//IL_0134: Unknown result type (might be due to invalid IL or missing references)
			//IL_013e: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				PenTool = (RadioButton)target;
				break;
			case 4:
				ArrowTool = (RadioButton)target;
				break;
			case 5:
				HighlightTool = (RadioButton)target;
				break;
			case 6:
				TextTool = (RadioButton)target;
				break;
			case 7:
				Swatches = (StackPanel)target;
				break;
			case 8:
				SizeSlider = (Slider)target;
				break;
			case 9:
				Loading = (TextBlock)target;
				break;
			case 10:
				View = (Viewbox)target;
				break;
			case 11:
				Stage = (Canvas)target;
				break;
			case 12:
				Img = (Image)target;
				break;
			case 13:
				Layer = (Canvas)target;
				break;
			case 14:
				EditorLayer = (Canvas)target;
				break;
			case 15:
				UndoButton = (Button)target;
				break;
			case 16:
				ClearButton = (Button)target;
				break;
			case 17:
				SaveButton = (Button)target;
				break;
			case 18:
				Hint = (TextBlock)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	public class CompressDialog : Window, IComponentConnector
	{
		private readonly IReadOnlyList<string> _files;

		private readonly long[] _sizes;

		private CancellationTokenSource? _cts;

		private VideoInfo? _video;

		internal Border Root;

		internal TextBlock FileName;

		internal TextBlock OriginalSize;

		internal WrapPanel ModeBox;

		internal RadioButton PercentChip;

		internal RadioButton TargetChip;

		internal StackPanel PercentPanel;

		internal TextBlock PercentLabel;

		internal Slider PercentSlider;

		internal StackPanel TargetPanel;

		internal TextBox TargetBox;

		internal TextBlock OutputLine;

		internal TextBlock Hint;

		internal ProgressBar Busy;

		internal TextBlock ResultText;

		internal Button CancelButton;

		internal Button CompressButton;

		private bool _contentLoaded;

		private CompressMode Mode
		{
			get
			{
				if (((ToggleButton)TargetChip).IsChecked == true)
				{
					return (CompressMode)1;
				}
				return (CompressMode)0;
			}
		}

		private double TargetKb
		{
			get
			{
				if (!double.TryParse(TargetBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result))
				{
					return 0.0;
				}
				return result;
			}
		}

		public CompressDialog(IReadOnlyList<string> files)
		{
			//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b8: Expected Obj, but got Unknown
			//IL_01c5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01cf: Expected Obj, but got Unknown
			//IL_01f3: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fd: Expected Obj, but got Unknown
			//IL_020a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0214: Expected Obj, but got Unknown
			//IL_0221: Unknown result type (might be due to invalid IL or missing references)
			//IL_022b: Expected Obj, but got Unknown
			//IL_0245: Unknown result type (might be due to invalid IL or missing references)
			//IL_024f: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_files = files;
			_sizes = files.Select((string f) => new FileInfo(f).Length).ToArray();
			FileName.Text = ((files.Count == 1) ? Path.GetFileName(files[0]) : $"{files.Count} files");
			((FrameworkElement)FileName).ToolTip = string.Join("\n", files);
			OriginalSize.Text = "Original size: " + SizeFormat.Format(_sizes.Sum());
			TargetBox.Text = Math.Max(1L, _sizes.Max() / 2 / 1024).ToString(CultureInfo.CurrentCulture);
			List<CompressSupport> source = ((IEnumerable<string>)files).Select((Func<string, CompressSupport>)CompressPlan.SupportFor).Distinct().ToList();
			Hint.Text = string.Join("\n", ((IEnumerable<string>)files).Select((Func<string, string>)CompressPlan.HintFor).Distinct());
			if (source.All((CompressSupport s) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				//IL_0002: Invalid comparison between Unknown and I4
				return (int)s == 5;
			}))
			{
				((UIElement)CompressButton).IsEnabled = false;
			}
			((ToggleButton)PercentChip).Checked += (object _, RoutedEventArgs _) =>
			{
				Update();
			};
			((ToggleButton)TargetChip).Checked += (object _, RoutedEventArgs _) =>
			{
				Update();
			};
			((RangeBase)PercentSlider).ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> _) =>
			{
				Update();
			};
			((TextBoxBase)TargetBox).TextChanged += (object _, TextChangedEventArgs _) =>
			{
				Update();
			};
			((ButtonBase)CompressButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await RunAsync();
			};
			((ButtonBase)CancelButton).Click += (object _, RoutedEventArgs _) =>
			{
				_cts?.Cancel();
				((Window)this).Close();
			};
			((Window)this).Closed += (object? _, EventArgs _) =>
			{
				_cts?.Cancel();
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadVideoInfoAsync();
			};
			Update();
		}

		private async Task LoadVideoInfoAsync()
		{
			if (_files.Count != 1 || (int)CompressPlan.SupportFor(_files[0]) != 4)
			{
				return;
			}
			try
			{
				_video = await VideoCompressor.GetInfoAsync(_files[0]);
				TextBlock originalSize = OriginalSize;
				originalSize.Text += $"  ·  {_video.Width} × {_video.Height}  ·  {_video.Duration:h\\:mm\\:ss}";
				Update();
			}
			catch (Exception ex)
			{
				Log.Error("Video info failed", ex);
			}
		}

		private void Update()
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Invalid comparison between Unknown and I4
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0092: Invalid comparison between Unknown and I4
			//IL_0125: Unknown result type (might be due to invalid IL or missing references)
			//IL_012b: Invalid comparison between Unknown and I4
			//IL_013b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0141: Invalid comparison between Unknown and I4
			if (!((FrameworkElement)this).IsInitialized || PercentPanel == null)
			{
				return;
			}
			((UIElement)PercentPanel).Visibility = (Visibility)(((int)Mode != 0) ? 2 : 0);
			((UIElement)TargetPanel).Visibility = (Visibility)(((int)Mode != 1) ? 2 : 0);
			PercentLabel.Text = $"Reduce by {((RangeBase)PercentSlider).Value:0}%";
			if ((int)Mode == 1 && TargetKb < 1.0)
			{
				OutputLine.Text = "Type a size in KB (for example 500).";
				((UIElement)CompressButton).IsEnabled = false;
				return;
			}
			((UIElement)CompressButton).IsEnabled = _files.Any((string f) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Invalid comparison between Unknown and I4
				return (int)CompressPlan.SupportFor(f) != 5;
			});
			long num = _sizes.Sum((long s) => Target(s));
			string text;
			if (_files.Count <= 1)
			{
				text = "";
			}
			else
			{
				text = (((int)Mode == 1) ? " each" : " in total");
			}
			long bytes = (((int)Mode == 1) ? Target(_sizes[0]) : num);
			OutputLine.Text = "Output will be at most " + KB(bytes) + text;
			VideoInfo video = _video;
			if (video != null && video.Duration.TotalSeconds > 0.0)
			{
				uint item = VideoCompressor.PlanBitrates(Target(_sizes[0]), video.Duration, true).Item1;
				TextBlock outputLine = OutputLine;
				outputLine.Text += $"\nVideo bitrate about {item / 1000:N0} kbps";
				if (VideoCompressor.LooksBlurry(item, video.Width, video.Height))
				{
					TextBlock outputLine2 = OutputLine;
					outputLine2.Text += " — low for this resolution, it may look blurry.";
				}
			}
		}

		private long Target(long original)
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return CompressPlan.TargetBytes(original, Mode, ((RangeBase)PercentSlider).Value, TargetKb);
		}

		private static string KB(long bytes)
		{
			return Math.Max(1L, bytes / 1024).ToString("N0", CultureInfo.CurrentCulture) + " KB" + ((bytes >= 10485760) ? (" (" + SizeFormat.Format(bytes) + ")") : "");
		}

		private async Task RunAsync()
		{
			_cts = new CancellationTokenSource();
			CancellationToken ct = _cts.Token;
			((UIElement)CompressButton).IsEnabled = false;
			WrapPanel modeBox = ModeBox;
			Slider percentSlider = PercentSlider;
			bool flag = (((UIElement)TargetBox).IsEnabled = false);
			bool isEnabled = (((UIElement)percentSlider).IsEnabled = flag);
			((UIElement)modeBox).IsEnabled = isEnabled;
			((UIElement)Busy).Visibility = (Visibility)0;
			ResultText.Text = "Working…";
			List<CompressResult> results = new List<CompressResult>();
			List<string> errors = new List<string>();
			for (int i = 0; i < _files.Count; i++)
			{
				string file = _files[i];
				long target = Target(_sizes[i]);
				if (_files.Count > 1)
				{
					ResultText.Text = $"Working… {i + 1} of {_files.Count}";
				}
				try
				{
					CompressResult item;
					if ((int)CompressPlan.SupportFor(file) == 4)
					{
						Busy.IsIndeterminate = false;
						((RangeBase)Busy).Value = 0.0;
						string prefix = ((_files.Count > 1) ? $"File {i + 1} of {_files.Count}: encoding video" : "Encoding video");
						ResultText.Text = prefix + "… 0%";
						Progress<double> progress = new Progress<double>((double p) =>
						{
							((RangeBase)Busy).Value = p * 100.0;
							ResultText.Text = $"{prefix}… {p:P0}";
						});
						item = await VideoCompressor.CompressAsync(file, target, (IProgress<double>)progress, ct);
						Busy.IsIndeterminate = true;
					}
					else
					{
						item = await Task.Run(() =>
						{
							//IL_0006: Unknown result type (might be due to invalid IL or missing references)
							//IL_000c: Invalid comparison between Unknown and I4
							return ((int)CompressPlan.SupportFor(file) != 3) ? ImageCompressor.Compress(file, target, ct) : PdfCompressor.Compress(file, target, ct);
						}, ct);
					}
					results.Add(item);
				}
				catch (OperationCanceledException)
				{
					return;
				}
				catch (Exception ex2)
				{
					Log.Error("Compress failed: " + file, ex2);
					errors.Add(Path.GetFileName(file) + ": " + ex2.Message);
				}
			}
			((UIElement)Busy).Visibility = (Visibility)2;
			List<string> list = (from r in results
				where r.OutputPath != null
				select r.OutputPath).ToList();
			if (list.Count == 0)
			{
				ResultText.Text = string.Join("\n", results.Select((CompressResult r) => r.Message).Concat(errors));
				WrapPanel modeBox2 = ModeBox;
				Slider percentSlider2 = PercentSlider;
				flag = (((UIElement)TargetBox).IsEnabled = true);
				isEnabled = (((UIElement)percentSlider2).IsEnabled = flag);
				((UIElement)modeBox2).IsEnabled = isEnabled;
				Update();
				return;
			}
			string text = ((_files.Count == 1) ? results[0].Message : $"{list.Count} of {_files.Count} files compressed. {SizeFormat.Format(results.Where((CompressResult r) => r.OutputPath != null).Sum((CompressResult r) => r.OriginalBytes))} → {SizeFormat.Format(results.Where((CompressResult r) => r.OutputPath != null).Sum((CompressResult r) => r.OutputBytes))}");
			ToastWindow.Show((results.All((CompressResult r) => r.ReachedTarget) && errors.Count == 0) ? "Compressed" : "Compressed (with notes)", text + ((errors.Count > 0) ? ("\n" + string.Join("\n", errors)) : ""), list);
			ShellReveal.Select(list);
			((Window)this).Close();
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/compressdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Expected Obj, but got Unknown
			//IL_0060: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Expected Obj, but got Unknown
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Expected Obj, but got Unknown
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Expected Obj, but got Unknown
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_0091: Expected Obj, but got Unknown
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_009e: Expected Obj, but got Unknown
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ab: Expected Obj, but got Unknown
			//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Expected Obj, but got Unknown
			//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c5: Expected Obj, but got Unknown
			//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Expected Obj, but got Unknown
			//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Expected Obj, but got Unknown
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ec: Expected Obj, but got Unknown
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f9: Expected Obj, but got Unknown
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0106: Expected Obj, but got Unknown
			//IL_0109: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Expected Obj, but got Unknown
			//IL_0116: Unknown result type (might be due to invalid IL or missing references)
			//IL_0120: Expected Obj, but got Unknown
			//IL_0123: Unknown result type (might be due to invalid IL or missing references)
			//IL_012d: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				OriginalSize = (TextBlock)target;
				break;
			case 4:
				ModeBox = (WrapPanel)target;
				break;
			case 5:
				PercentChip = (RadioButton)target;
				break;
			case 6:
				TargetChip = (RadioButton)target;
				break;
			case 7:
				PercentPanel = (StackPanel)target;
				break;
			case 8:
				PercentLabel = (TextBlock)target;
				break;
			case 9:
				PercentSlider = (Slider)target;
				break;
			case 10:
				TargetPanel = (StackPanel)target;
				break;
			case 11:
				TargetBox = (TextBox)target;
				break;
			case 12:
				OutputLine = (TextBlock)target;
				break;
			case 13:
				Hint = (TextBlock)target;
				break;
			case 14:
				Busy = (ProgressBar)target;
				break;
			case 15:
				ResultText = (TextBlock)target;
				break;
			case 16:
				CancelButton = (Button)target;
				break;
			case 17:
				CompressButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	public class CropDialog : Window, IComponentConnector
	{
		private const double HandleScreenPx = 12.0;

		private readonly string _path;

		private double _imgW;

		private double _imgH;

		private CropBox _box;

		private double? _ratio;

		private CropHandle _drag;

		private Point _dragStart;

		private CropBox _boxAtStart;

		private bool _updatingFields;

		private readonly Dictionary<CropHandle, Rectangle> _handles = new Dictionary<CropHandle, Rectangle>();

		internal Border Root;

		internal TextBlock FileName;

		internal TextBlock Loading;

		internal Viewbox View;

		internal Canvas Stage;

		internal Image Img;

		internal Path Shade;

		internal Path Thirds;

		internal Rectangle Frame;

		internal Canvas Handles;

		internal WrapPanel Chips;

		internal TextBox WBox;

		internal TextBox HBox;

		internal Button ResetButton;

		internal Button ApplyButton;

		private bool _contentLoaded;

		private double Scale
		{
			get
			{
				if (!(_imgW <= 0.0))
				{
					return Math.Max(0.0001, Math.Min(((FrameworkElement)View).ActualWidth / _imgW, ((FrameworkElement)View).ActualHeight / _imgH));
				}
				return 1.0;
			}
		}

		public CropDialog(string path)
		{
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_0055: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0065: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Expected Obj, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_0090: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Expected Obj, but got Unknown
			//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ff: Expected Obj, but got Unknown
			//IL_010e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0118: Expected Obj, but got Unknown
			//IL_0145: Unknown result type (might be due to invalid IL or missing references)
			//IL_014f: Expected Obj, but got Unknown
			//IL_015c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0166: Expected Obj, but got Unknown
			//IL_0173: Unknown result type (might be due to invalid IL or missing references)
			//IL_017d: Expected Obj, but got Unknown
			//IL_018a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0194: Expected Obj, but got Unknown
			//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ab: Expected Obj, but got Unknown
			//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c2: Expected Obj, but got Unknown
			//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d9: Expected Obj, but got Unknown
			//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f0: Expected Obj, but got Unknown
			//IL_01fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_0207: Expected Obj, but got Unknown
			//IL_0214: Unknown result type (might be due to invalid IL or missing references)
			//IL_021e: Expected Obj, but got Unknown
			//IL_0226: Unknown result type (might be due to invalid IL or missing references)
			//IL_0230: Expected Obj, but got Unknown
			//IL_0238: Unknown result type (might be due to invalid IL or missing references)
			//IL_0242: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			CropHandle[] array = new CropHandle[8];
			RuntimeHelpers.InitializeArray(array, __ldtoken(<PrivateImplementationDetails>.03661698491031DCF9F48393488A194EABE5C310C422A7AF81159213156AFC05));
			CropHandle[] array2 = array;
			foreach (CropHandle key in array2)
			{
				Rectangle val = new Rectangle
				{
					Fill = (Brush)(object)Brushes.White,
					Stroke = (Brush)new SolidColorBrush(Color.FromRgb(byte.MaxValue, (byte)138, (byte)31)),
					RadiusX = 2.0,
					RadiusY = 2.0
				};
				_handles[key] = val;
				((Panel)Handles).Children.Add((UIElement)(object)val);
			}
			foreach (RadioButton child in ((Panel)Chips).Children)
			{
				RadioButton chip = child;
				((ToggleButton)chip).Checked += (object _, RoutedEventArgs _) =>
				{
					SetRatio(((FrameworkElement)chip).Tag as string);
				};
			}
			((UIElement)Stage).MouseLeftButtonDown += OnDown;
			((UIElement)Stage).MouseMove += OnMove;
			((UIElement)Stage).MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				//IL_0002: Unknown result type (might be due to invalid IL or missing references)
				_drag = (CropHandle)0;
				((UIElement)Stage).ReleaseMouseCapture();
			};
			((FrameworkElement)View).SizeChanged += (object _, SizeChangedEventArgs _) =>
			{
				Redraw();
			};
			((ButtonBase)ResetButton).Click += (object _, RoutedEventArgs _) =>
			{
				Reset();
			};
			((ButtonBase)ApplyButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await ApplyAsync();
			};
			((UIElement)WBox).LostFocus += (object _, RoutedEventArgs _) =>
			{
				FieldsChanged(widthChanged: true);
			};
			((UIElement)HBox).LostFocus += (object _, RoutedEventArgs _) =>
			{
				FieldsChanged(widthChanged: false);
			};
			((UIElement)WBox).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Invalid comparison between Unknown and I4
				if ((int)e.Key == 6)
				{
					FieldsChanged(widthChanged: true);
				}
			};
			((UIElement)HBox).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Invalid comparison between Unknown and I4
				if ((int)e.Key == 6)
				{
					FieldsChanged(widthChanged: false);
				}
			};
			((UIElement)this).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				if ((int)e.Key == 13)
				{
					((Window)this).Close();
				}
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
		}

		private async Task LoadAsync()
		{
			try
			{
				(BitmapSource, int, int) tuple = await Task.Run(() => PreviewLoader.Load(_path));
				BitmapSource item = tuple.Item1;
				int item2 = tuple.Item2;
				int item3 = tuple.Item3;
				_imgW = item2;
				_imgH = item3;
				Img.Source = (ImageSource)(object)item;
				Image img = Img;
				double width = (((FrameworkElement)Stage).Width = item2);
				((FrameworkElement)img).Width = width;
				Image img2 = Img;
				width = (((FrameworkElement)Stage).Height = item3);
				((FrameworkElement)img2).Height = width;
				((UIElement)Loading).Visibility = (Visibility)2;
				((UIElement)Stage).Visibility = (Visibility)0;
				((UIElement)ApplyButton).IsEnabled = true;
				Reset();
			}
			catch (Exception ex)
			{
				Log.Error("Crop preview failed: " + _path, ex);
				Loading.Text = "Could not open this image: " + ex.Message;
			}
		}

		private void SetRatio(string? tag)
		{
			//IL_007c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			_ratio = null;
			if (!string.IsNullOrEmpty(tag))
			{
				string[] array = tag.Split(':');
				_ratio = double.Parse(array[0], CultureInfo.InvariantCulture) / double.Parse(array[1], CultureInfo.InvariantCulture);
			}
			if (!(_imgW <= 0.0))
			{
				double? ratio = _ratio;
				if (ratio.HasValue)
				{
					double valueOrDefault = ratio.GetValueOrDefault();
					_box = CropMath.FitRatio(_imgW, _imgH, valueOrDefault);
				}
				Redraw();
			}
		}

		private void Reset()
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			((ToggleButton)(RadioButton)((Panel)Chips).Children[0]).IsChecked = true;
			_ratio = null;
			_box = CropMath.Full(_imgW, _imgH);
			Redraw();
		}

		private void FieldsChanged(bool widthChanged)
		{
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			if (_updatingFields || _imgW <= 0.0)
			{
				return;
			}
			if (!double.TryParse(WBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) || !double.TryParse(HBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result2))
			{
				Redraw();
				return;
			}
			double? ratio = _ratio;
			if (ratio.HasValue)
			{
				double valueOrDefault = ratio.GetValueOrDefault();
				if (widthChanged)
				{
					result2 = result / valueOrDefault;
				}
				else
				{
					result = result2 * valueOrDefault;
				}
				if (result > _imgW)
				{
					result = _imgW;
					result2 = result / valueOrDefault;
				}
				if (result2 > _imgH)
				{
					result2 = _imgH;
					result = result2 * valueOrDefault;
				}
			}
			_box = CropMath.SetSize(_box, result, result2, _imgW, _imgH);
			Redraw();
		}

		private CropHandle HandleAt(Point p)
		{
			double num = 12.0 / Scale;
			bool flag = Math.Abs(p.X - _box.X) <= num;
			bool flag2 = Math.Abs(p.X - _box.Right) <= num;
			bool flag3 = Math.Abs(p.Y - _box.Y) <= num;
			bool flag4 = Math.Abs(p.Y - _box.Bottom) <= num;
			bool flag5 = p.X > _box.X - num && p.X < _box.Right + num;
			bool flag6 = p.Y > _box.Y - num && p.Y < _box.Bottom + num;
			if (!(flag3 & flag))
			{
				if (!(flag3 & flag2))
				{
					if (!(flag4 & flag))
					{
						if (!(flag4 & flag2))
						{
							if (!(flag3 & flag5))
							{
								if (!(flag4 & flag5))
								{
									if (!(flag & flag6))
									{
										if (!(flag2 & flag6))
										{
											if (p.X > _box.X && p.X < _box.Right && p.Y > _box.Y && p.Y < _box.Bottom)
											{
												return (CropHandle)1;
											}
											return (CropHandle)0;
										}
										return (CropHandle)4;
									}
									return (CropHandle)5;
								}
								return (CropHandle)3;
							}
							return (CropHandle)2;
						}
						return (CropHandle)8;
					}
					return (CropHandle)9;
				}
				return (CropHandle)6;
			}
			return (CropHandle)7;
		}

		private static Cursor CursorFor(CropHandle h)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Expected I4, but got Unknown
			switch (h - 1)
			{
			case 6:
			case 7:
				return Cursors.SizeNWSE;
			case 5:
			case 8:
				return Cursors.SizeNESW;
			case 1:
			case 2:
				return Cursors.SizeNS;
			case 3:
			case 4:
				return Cursors.SizeWE;
			case 0:
				return Cursors.SizeAll;
			default:
				return Cursors.Arrow;
			}
		}

		private void OnDown(object sender, MouseButtonEventArgs e)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			Point position = ((MouseEventArgs)e).GetPosition((IInputElement)(object)Stage);
			_drag = HandleAt(position);
			if ((int)_drag != 0)
			{
				_dragStart = position;
				_boxAtStart = _box;
				((UIElement)Stage).CaptureMouse();
			}
		}

		private void OnMove(object sender, MouseEventArgs e)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			Point position = e.GetPosition((IInputElement)(object)Stage);
			if ((int)_drag == 0)
			{
				((FrameworkElement)Stage).Cursor = CursorFor(HandleAt(position));
				return;
			}
			_box = CropMath.Drag(_boxAtStart, _drag, position.X - _dragStart.X, position.Y - _dragStart.Y, _ratio, _imgW, _imgH);
			Redraw();
		}

		private void Redraw()
		{
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0076: Unknown result type (might be due to invalid IL or missing references)
			//IL_007b: Unknown result type (might be due to invalid IL or missing references)
			//IL_007c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Expected Obj, but got Unknown
			//IL_0086: Expected Obj, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Expected Obj, but got Unknown
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ef: Expected Obj, but got Unknown
			//IL_0144: Unknown result type (might be due to invalid IL or missing references)
			//IL_0152: Unknown result type (might be due to invalid IL or missing references)
			//IL_0157: Unknown result type (might be due to invalid IL or missing references)
			//IL_0161: Expected Obj, but got Unknown
			//IL_0170: Unknown result type (might be due to invalid IL or missing references)
			//IL_017e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0183: Unknown result type (might be due to invalid IL or missing references)
			//IL_018d: Expected Obj, but got Unknown
			//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0214: Expected I4, but got Unknown
			//IL_033a: Unknown result type (might be due to invalid IL or missing references)
			//IL_022d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0230: Unknown result type (might be due to invalid IL or missing references)
			//IL_0232: Invalid comparison between Unknown and I4
			//IL_0268: Unknown result type (might be due to invalid IL or missing references)
			//IL_026b: Invalid comparison between Unknown and I4
			//IL_026d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0270: Unknown result type (might be due to invalid IL or missing references)
			//IL_0272: Invalid comparison between Unknown and I4
			//IL_028b: Unknown result type (might be due to invalid IL or missing references)
			//IL_028e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0290: Invalid comparison between Unknown and I4
			if (_imgW <= 0.0)
			{
				return;
			}
			double scale = Scale;
			Rect val = new Rect(_box.X, _box.Y, _box.W, _box.H);
			Shade.Data = (Geometry)new CombinedGeometry((GeometryCombineMode)3, (Geometry)new RectangleGeometry(new Rect(0.0, 0.0, _imgW, _imgH)), (Geometry)new RectangleGeometry(val));
			Canvas.SetLeft((UIElement)(object)Frame, val.X);
			Canvas.SetTop((UIElement)(object)Frame, val.Y);
			((FrameworkElement)Frame).Width = val.Width;
			((FrameworkElement)Frame).Height = val.Height;
			((Shape)Frame).StrokeThickness = 1.5 / scale;
			GeometryGroup val2 = new GeometryGroup();
			for (int i = 1; i <= 2; i++)
			{
				double num = val.X + val.Width * (double)i / 3.0;
				double num2 = val.Y + val.Height * (double)i / 3.0;
				val2.Children.Add((Geometry)new LineGeometry(new Point(num, val.Y), new Point(num, val.Bottom)));
				val2.Children.Add((Geometry)new LineGeometry(new Point(val.X, num2), new Point(val.Right, num2)));
			}
			Thirds.Data = (Geometry)(object)val2;
			((Shape)Thirds).StrokeThickness = 1.0 / scale;
			double num3 = 12.0 / scale;
			foreach (var (val5, val6) in _handles)
			{
				bool flag;
				switch (val5 - 5)
				{
				case 0:
				case 2:
				case 4:
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				double num4;
				if (flag)
				{
					num4 = val.X;
				}
				else
				{
					bool flag2 = val5 - 2 <= 1;
					num4 = (flag2 ? (val.X + val.Width / 2.0) : val.Right);
				}
				double num5 = num4;
				if (((int)val5 == 2 || val5 - 6 <= 1) ? true : false)
				{
					num4 = val.Y;
				}
				else
				{
					bool flag2 = val5 - 4 <= 1;
					num4 = (flag2 ? (val.Y + val.Height / 2.0) : val.Bottom);
				}
				double num6 = num4;
				num4 = (((FrameworkElement)val6).Height = num3);
				((FrameworkElement)val6).Width = num4;
				((Shape)val6).StrokeThickness = 1.5 / scale;
				Canvas.SetLeft((UIElement)(object)val6, num5 - num3 / 2.0);
				Canvas.SetTop((UIElement)(object)val6, num6 - num3 / 2.0);
			}
			_updatingFields = true;
			(int, int, int, int) tuple = CropMath.ToPixels(_box, (int)_imgW, (int)_imgH);
			int item = tuple.Item3;
			int item2 = tuple.Item4;
			if (!((UIElement)WBox).IsKeyboardFocused)
			{
				WBox.Text = item.ToString(CultureInfo.CurrentCulture);
			}
			if (!((UIElement)HBox).IsKeyboardFocused)
			{
				HBox.Text = item2.ToString(CultureInfo.CurrentCulture);
			}
			_updatingFields = false;
		}

		private async Task ApplyAsync()
		{
			((UIElement)ApplyButton).IsEnabled = false;
			CropBox box = _box;
			try
			{
				string text = await Task.Run(() =>
				{
					//IL_000c: Unknown result type (might be due to invalid IL or missing references)
					return ImageCropper.Crop(_path, box);
				});
				(int, int, int, int) tuple = CropMath.ToPixels(box, (int)_imgW, (int)_imgH);
				int item = tuple.Item3;
				int item2 = tuple.Item4;
				ToastWindow.Show("Cropped", $"{Path.GetFileName(text)} ({item} × {item2} px)", new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Crop failed: " + _path, ex);
				ToastWindow.Show("Crop failed", ex.Message, null, isError: true);
				((UIElement)ApplyButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/cropdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_004b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0055: Expected Obj, but got Unknown
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Expected Obj, but got Unknown
			//IL_0065: Unknown result type (might be due to invalid IL or missing references)
			//IL_006f: Expected Obj, but got Unknown
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_007c: Expected Obj, but got Unknown
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Expected Obj, but got Unknown
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0096: Expected Obj, but got Unknown
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a3: Expected Obj, but got Unknown
			//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b0: Expected Obj, but got Unknown
			//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Expected Obj, but got Unknown
			//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ca: Expected Obj, but got Unknown
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d7: Expected Obj, but got Unknown
			//IL_00da: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e4: Expected Obj, but got Unknown
			//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f1: Expected Obj, but got Unknown
			//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fe: Expected Obj, but got Unknown
			//IL_0101: Unknown result type (might be due to invalid IL or missing references)
			//IL_010b: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				Loading = (TextBlock)target;
				break;
			case 4:
				View = (Viewbox)target;
				break;
			case 5:
				Stage = (Canvas)target;
				break;
			case 6:
				Img = (Image)target;
				break;
			case 7:
				Shade = (Path)target;
				break;
			case 8:
				Thirds = (Path)target;
				break;
			case 9:
				Frame = (Rectangle)target;
				break;
			case 10:
				Handles = (Canvas)target;
				break;
			case 11:
				Chips = (WrapPanel)target;
				break;
			case 12:
				WBox = (TextBox)target;
				break;
			case 13:
				HBox = (TextBox)target;
				break;
			case 14:
				ResetButton = (Button)target;
				break;
			case 15:
				ApplyButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	internal static class DialogChrome
	{
		public static void Apply(Window window, Border root)
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Expected Obj, but got Unknown
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Expected Obj, but got Unknown
			window.Icon = (ImageSource)new BitmapImage(new Uri("pack://application:,,,/Assets/zest.ico"));
			((Control)window).FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
			((FrameworkElement)window).SetResourceReference(Control.ForegroundProperty, (object)"Ink");
			((FrameworkElement)window).SetResourceReference(Control.BackgroundProperty, (object)"WindowBg");
			root.Background = (Brush)(object)Brushes.Transparent;
			bool mica = false;
			window.SourceInitialized += (object? _, EventArgs _) =>
			{
				mica = WindowEffects.TryApplyMica(window, ThemeManager.IsDark);
				if (mica)
				{
					((Control)window).Background = (Brush)(object)Brushes.Transparent;
				}
			};
			ThemeManager.Changed += OnThemeChanged;
			window.Closed += (object? _, EventArgs _) =>
			{
				ThemeManager.Changed -= OnThemeChanged;
			};
			void OnThemeChanged()
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				nint handle = new WindowInteropHelper(window).Handle;
				if (handle != IntPtr.Zero)
				{
					WindowEffects.SetDarkTitleBar(handle, ThemeManager.IsDark);
					if (!mica)
					{
						((FrameworkElement)window).SetResourceReference(Control.BackgroundProperty, (object)"WindowBg");
					}
				}
			}
		}

		public static void ShowInFront(Window window)
		{
			window.Topmost = true;
			window.Show();
			window.Activate();
			window.ContentRendered += (object? _, EventArgs _) =>
			{
				((DispatcherObject)window).Dispatcher.BeginInvoke((Delegate)(Func<bool>)(() => window.Topmost = false), (DispatcherPriority)2, Array.Empty<object>());
			};
			DebugSnapshot(window);
		}

		private static void DebugSnapshot(Window window)
		{
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Expected Obj, but got Unknown
			string dir = Environment.GetEnvironmentVariable("ZEST_SNAPSHOT");
			if (string.IsNullOrEmpty(dir))
			{
				return;
			}
			DispatcherTimer timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(2.0)
			};
			timer.Tick += (object? _, EventArgs _) =>
			{
				//IL_0026: Unknown result type (might be due to invalid IL or missing references)
				//IL_002b: Unknown result type (might be due to invalid IL or missing references)
				//IL_005d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0062: Unknown result type (might be due to invalid IL or missing references)
				//IL_0068: Expected Obj, but got Unknown
				//IL_0068: Unknown result type (might be due to invalid IL or missing references)
				//IL_006e: Expected Obj, but got Unknown
				//IL_008c: Unknown result type (might be due to invalid IL or missing references)
				//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
				//IL_00ba: Expected Obj, but got Unknown
				//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
				//IL_00dd: Expected Obj, but got Unknown
				timer.Stop();
				object content = ((ContentControl)window).Content;
				FrameworkElement val = (FrameworkElement)((content is FrameworkElement) ? content : null);
				if (val == null)
				{
					return;
				}
				DpiScale dpi = VisualTreeHelper.GetDpi((Visual)(object)window);
				double dpiScaleX = dpi.DpiScaleX;
				RenderTargetBitmap val2 = new RenderTargetBitmap((int)(val.ActualWidth * dpiScaleX), (int)(val.ActualHeight * dpiScaleX), 96.0 * dpiScaleX, 96.0 * dpiScaleX, PixelFormats.Pbgra32);
				DrawingVisual val3 = new DrawingVisual();
				DrawingContext val4 = val3.RenderOpen();
				try
				{
					val4.DrawRectangle((Brush)Application.Current.Resources[(object)"WindowBg"], (Pen)null, new Rect(0.0, 0.0, val.ActualWidth, val.ActualHeight));
				}
				finally
				{
					((IDisposable)val4)?.Dispose();
				}
				val2.Render((Visual)(object)val3);
				val2.Render((Visual)(object)val);
				PngBitmapEncoder val5 = new PngBitmapEncoder();
				((BitmapEncoder)val5).Frames.Add(BitmapFrame.Create((BitmapSource)(object)val2));
				using FileStream fileStream = File.Create(Path.Combine(dir, ((object)window).GetType().Name + ".png"));
				((BitmapEncoder)val5).Save((Stream)fileStream);
			};
			timer.Start();
		}
	}
	public class EditDialog : Window, IComponentConnector
	{
		private readonly string _path;

		private readonly LivePreview _preview;

		private EditSettings _settings = new EditSettings(0, 0, 0, 0, false, false);

		private bool _loaded;

		internal Border Root;

		internal TextBlock FileName;

		internal TextBlock Loading;

		internal Image PreviewImage;

		internal TextBlock BrightnessValue;

		internal Slider BrightnessSlider;

		internal TextBlock ContrastValue;

		internal Slider ContrastSlider;

		internal TextBlock SaturationValue;

		internal Slider SaturationSlider;

		internal Button RotateLeftButton;

		internal Button RotateRightButton;

		internal Button FlipHButton;

		internal Button FlipVButton;

		internal TextBlock StateText;

		internal Button ResetButton;

		internal Button SaveButton;

		private bool _contentLoaded;

		public EditDialog(string path)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Expected Obj, but got Unknown
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Expected Obj, but got Unknown
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0104: Expected Obj, but got Unknown
			//IL_0111: Unknown result type (might be due to invalid IL or missing references)
			//IL_011b: Expected Obj, but got Unknown
			//IL_0128: Unknown result type (might be due to invalid IL or missing references)
			//IL_0132: Expected Obj, but got Unknown
			//IL_013f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0149: Expected Obj, but got Unknown
			//IL_0156: Unknown result type (might be due to invalid IL or missing references)
			//IL_0160: Expected Obj, but got Unknown
			//IL_016d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0177: Expected Obj, but got Unknown
			//IL_017f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0189: Expected Obj, but got Unknown
			//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ad: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			_preview = new LivePreview(PreviewImage);
			((RangeBase)BrightnessSlider).ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> _) =>
			{
				Changed();
			};
			((RangeBase)ContrastSlider).ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> _) =>
			{
				Changed();
			};
			((RangeBase)SaturationSlider).ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> _) =>
			{
				Changed();
			};
			Slider[] array = new Slider[3] { BrightnessSlider, ContrastSlider, SaturationSlider };
			foreach (Slider s in array)
			{
				((Control)s).MouseDoubleClick += (object _, MouseButtonEventArgs _) =>
				{
					((RangeBase)s).Value = 0.0;
				};
			}
			((ButtonBase)RotateLeftButton).Click += (object _, RoutedEventArgs _) =>
			{
				_settings = _settings.RotateLeft();
				Changed();
			};
			((ButtonBase)RotateRightButton).Click += (object _, RoutedEventArgs _) =>
			{
				_settings = _settings.RotateRight();
				Changed();
			};
			((ButtonBase)FlipHButton).Click += (object _, RoutedEventArgs _) =>
			{
				EditSettings val = _settings.<Clone>$();
				val.FlipHorizontal = !_settings.FlipHorizontal;
				_settings = val;
				Changed();
			};
			((ButtonBase)FlipVButton).Click += (object _, RoutedEventArgs _) =>
			{
				EditSettings val = _settings.<Clone>$();
				val.FlipVertical = !_settings.FlipVertical;
				_settings = val;
				Changed();
			};
			((ButtonBase)ResetButton).Click += (object _, RoutedEventArgs _) =>
			{
				Reset();
			};
			((ButtonBase)SaveButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await SaveAsync();
			};
			((UIElement)this).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				//IL_0010: Unknown result type (might be due to invalid IL or missing references)
				//IL_0016: Invalid comparison between Unknown and I4
				//IL_0037: Unknown result type (might be due to invalid IL or missing references)
				//IL_003d: Invalid comparison between Unknown and I4
				//IL_0019: Unknown result type (might be due to invalid IL or missing references)
				//IL_0020: Invalid comparison between Unknown and I4
				//IL_0040: Unknown result type (might be due to invalid IL or missing references)
				//IL_0047: Invalid comparison between Unknown and I4
				//IL_002d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0037: Expected Obj, but got Unknown
				//IL_0054: Unknown result type (might be due to invalid IL or missing references)
				//IL_005e: Expected Obj, but got Unknown
				if ((int)e.Key == 13)
				{
					((Window)this).Close();
				}
				if ((int)Keyboard.Modifiers == 2 && (int)e.Key == 55)
				{
					((UIElement)RotateLeftButton).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
				}
				if ((int)Keyboard.Modifiers == 2 && (int)e.Key == 61)
				{
					((UIElement)RotateRightButton).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
				}
			};
			((Window)this).Closed += (object? _, EventArgs _) =>
			{
				_preview.Dispose();
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
		}

		private async Task LoadAsync()
		{
			try
			{
				await _preview.LoadAsync(_path);
				((UIElement)Loading).Visibility = (Visibility)2;
				_loaded = true;
				Changed();
			}
			catch (Exception ex)
			{
				Log.Error("Edit preview failed: " + _path, ex);
				Loading.Text = "Could not open this image: " + ex.Message;
			}
		}

		private void Changed()
		{
			EditSettings val = _settings.<Clone>$();
			val.Brightness = (int)Math.Round(((RangeBase)BrightnessSlider).Value);
			val.Contrast = (int)Math.Round(((RangeBase)ContrastSlider).Value);
			val.Saturation = (int)Math.Round(((RangeBase)SaturationSlider).Value);
			_settings = val;
			BrightnessValue.Text = Signed(_settings.Brightness);
			ContrastValue.Text = Signed(_settings.Contrast);
			SaturationValue.Text = Signed(_settings.Saturation);
			List<string> list = new List<string>();
			if (_settings.Rotation != 0)
			{
				list.Add($"rotated {_settings.Rotation}°");
			}
			if (_settings.FlipHorizontal)
			{
				list.Add("flipped ↔");
			}
			if (_settings.FlipVertical)
			{
				list.Add("flipped ↕");
			}
			StateText.Text = ((list.Count > 0) ? ("Image is " + string.Join(", ", list) + ".") : "");
			if (_loaded)
			{
				((UIElement)SaveButton).IsEnabled = !_settings.IsIdentity;
				EditSettings s = _settings;
				_preview.Update((MagickImage img) =>
				{
					ImageEditor.Apply(img, s);
					return img;
				});
			}
		}

		private static string Signed(int v)
		{
			if (v <= 0)
			{
				return v.ToString();
			}
			return $"+{v}";
		}

		private void Reset()
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Expected Obj, but got Unknown
			_settings = new EditSettings(0, 0, 0, 0, false, false);
			Slider brightnessSlider = BrightnessSlider;
			Slider contrastSlider = ContrastSlider;
			double num = (((RangeBase)SaturationSlider).Value = 0.0);
			double value = (((RangeBase)contrastSlider).Value = num);
			((RangeBase)brightnessSlider).Value = value;
			Changed();
		}

		private async Task SaveAsync()
		{
			((UIElement)SaveButton).IsEnabled = false;
			EditSettings s = _settings;
			try
			{
				string text = await Task.Run(() => ImageEditor.Save(_path, s));
				ToastWindow.Show("Saved", Path.GetFileName(text), new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Edit failed: " + _path, ex);
				ToastWindow.Show("Edit failed", ex.Message, null, isError: true);
				((UIElement)SaveButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/editdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Expected Obj, but got Unknown
			//IL_0060: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Expected Obj, but got Unknown
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Expected Obj, but got Unknown
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Expected Obj, but got Unknown
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_0091: Expected Obj, but got Unknown
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_009e: Expected Obj, but got Unknown
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ab: Expected Obj, but got Unknown
			//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Expected Obj, but got Unknown
			//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c5: Expected Obj, but got Unknown
			//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Expected Obj, but got Unknown
			//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Expected Obj, but got Unknown
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ec: Expected Obj, but got Unknown
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f9: Expected Obj, but got Unknown
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0106: Expected Obj, but got Unknown
			//IL_0109: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Expected Obj, but got Unknown
			//IL_0116: Unknown result type (might be due to invalid IL or missing references)
			//IL_0120: Expected Obj, but got Unknown
			//IL_0123: Unknown result type (might be due to invalid IL or missing references)
			//IL_012d: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				Loading = (TextBlock)target;
				break;
			case 4:
				PreviewImage = (Image)target;
				break;
			case 5:
				BrightnessValue = (TextBlock)target;
				break;
			case 6:
				BrightnessSlider = (Slider)target;
				break;
			case 7:
				ContrastValue = (TextBlock)target;
				break;
			case 8:
				ContrastSlider = (Slider)target;
				break;
			case 9:
				SaturationValue = (TextBlock)target;
				break;
			case 10:
				SaturationSlider = (Slider)target;
				break;
			case 11:
				RotateLeftButton = (Button)target;
				break;
			case 12:
				RotateRightButton = (Button)target;
				break;
			case 13:
				FlipHButton = (Button)target;
				break;
			case 14:
				FlipVButton = (Button)target;
				break;
			case 15:
				StateText = (TextBlock)target;
				break;
			case 16:
				ResetButton = (Button)target;
				break;
			case 17:
				SaveButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	internal sealed class LivePreview : IDisposable
	{
		private readonly Image _target;

		private MagickImage? _base;

		private CancellationTokenSource? _pending;

		public uint Width
		{
			get
			{
				MagickImage? val = _base;
				if (val == null)
				{
					return 0u;
				}
				return val.Width;
			}
		}

		public uint Height
		{
			get
			{
				MagickImage? val = _base;
				if (val == null)
				{
					return 0u;
				}
				return val.Height;
			}
		}

		public MagickImage? Base => _base;

		public LivePreview(Image target)
		{
			_target = target;
		}

		public async Task LoadAsync(string path, uint maxSize = 1100u)
		{
			_base = await Task.Run(() =>
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				//IL_000c: Expected Obj, but got Unknown
				//IL_0038: Unknown result type (might be due to invalid IL or missing references)
				//IL_0042: Expected Obj, but got Unknown
				MagickImage val = new MagickImage(path);
				val.AutoOrient();
				if (Math.Max(val.Width, val.Height) > maxSize)
				{
					val.Resize((IMagickGeometry)new MagickGeometry(maxSize, maxSize));
				}
				return val;
			});
		}

		public void Update(Func<MagickImage, MagickImage> transform)
		{
			if (_base == null)
			{
				return;
			}
			_pending?.Cancel();
			CancellationTokenSource cts = (_pending = new CancellationTokenSource());
			MagickImage source = _base;
			Task.Run(async () =>
			{
				await Task.Delay(60, cts.Token);
				MagickImage val = (MagickImage)source.Clone();
				MagickImage val2 = transform(val);
				BitmapSource bmp;
				try
				{
					bmp = ToBitmap(val2);
				}
				finally
				{
					if (val2 != val)
					{
						val2.Dispose();
					}
					val.Dispose();
				}
				if (!cts.IsCancellationRequested)
				{
					((DispatcherObject)_target).Dispatcher.Invoke((Action)(() =>
					{
						if (!cts.IsCancellationRequested)
						{
							_target.Source = (ImageSource)(object)bmp;
						}
					}));
				}
			}, cts.Token).ContinueWith((Task t) =>
			{
				if (t.Exception != null)
				{
					Log.Error("Preview failed", t.Exception);
				}
			}, TaskContinuationOptions.OnlyOnFaulted);
		}

		public static BitmapSource ToBitmap(MagickImage img)
		{
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			int width = (int)img.Width;
			int height = (int)img.Height;
			byte[] array = ((IPixelCollection<byte>)(object)img.GetPixelsUnsafe()).ToByteArray((PixelMapping)5);
			BitmapSource val = BitmapSource.Create(width, height, 96.0, 96.0, PixelFormats.Bgra32, (BitmapPalette)null, (Array)array, width * 4);
			((Freezable)val).Freeze();
			return val;
		}

		public void Dispose()
		{
			_pending?.Cancel();
			MagickImage? val = _base;
			if (val != null)
			{
				val.Dispose();
			}
		}
	}
	public class MetadataDialog : Window, IComponentConnector
	{
		private readonly string _path;

		private readonly bool _isPdf;

		private string[] _originalValues = Array.Empty<string>();

		internal Border Root;

		internal TextBlock FileName;

		internal ListView EntriesList;

		internal StackPanel FieldsPanel;

		internal TextBlock Label1;

		internal TextBox Field1;

		internal TextBlock Label2;

		internal TextBox Field2;

		internal TextBlock Label3;

		internal TextBox Field3;

		internal TextBlock Label4;

		internal TextBox Field4;

		internal CheckBox RemoveGpsBox;

		internal TextBlock Note;

		internal Button RemoveAllButton;

		internal Button SaveButton;

		private bool _contentLoaded;

		public MetadataDialog(string path)
		{
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Invalid comparison between Unknown and I4
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Expected Obj, but got Unknown
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Expected Obj, but got Unknown
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b2: Expected Obj, but got Unknown
			//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c4: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			_isPdf = (int)FileKinds.Detect(path) == 2;
			FileName.Text = Path.GetFileName(path);
			if (_isPdf)
			{
				Label3.Text = "Subject";
				Label4.Text = "Keywords";
			}
			((ButtonBase)SaveButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await RunAsync(save: true);
			};
			((ButtonBase)RemoveAllButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await RunAsync(save: false);
			};
			((UIElement)this).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				if ((int)e.Key == 13)
				{
					((Window)this).Close();
				}
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
		}

		private async Task LoadAsync()
		{
			_ = 1;
			try
			{
				if (_isPdf)
				{
					var (itemsSource, val) = await Task.Run(() => (PdfMetadata.Read(_path), PdfMetadata.ReadFields(_path)));
					((ItemsControl)EntriesList).ItemsSource = itemsSource;
					_originalValues = new string[4] { val.Title, val.Author, val.Subject, val.Keywords };
					Note.Text = "\"Remove all\" clears the document info and the XMP metadata. Pages are not changed.";
				}
				else
				{
					var (itemsSource2, val2, flag) = await Task.Run(() => (ImageMetadata.Read(_path), ImageMetadata.ReadBasic(_path), ImageMetadata.HasGps(_path)));
					((ItemsControl)EntriesList).ItemsSource = itemsSource2;
					_originalValues = new string[4]
					{
						val2.Title ?? "",
						val2.Author ?? "",
						val2.Copyright ?? "",
						val2.DateTaken ?? ""
					};
					((UIElement)RemoveGpsBox).Visibility = (Visibility)((!flag) ? 2 : 0);
					((ToggleButton)RemoveGpsBox).IsChecked = flag;
					bool flag2;
					switch (Path.GetExtension(_path).ToLowerInvariant())
					{
					case ".jpg":
					case ".jpeg":
					case ".jfif":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					bool flag3 = flag2;
					if (!ImageMetadata.CanEdit(_path))
					{
						((UIElement)FieldsPanel).IsEnabled = false;
						((UIElement)SaveButton).IsEnabled = false;
						Note.Text = "For this format Zest can show and remove metadata, but not edit it.";
					}
					else
					{
						Note.Text = (flag3 ? "JPEG: only the metadata changes. The photo is not re-compressed (no quality loss)." : "The image is saved again with the new metadata.");
					}
					if (flag)
					{
						TextBlock note = Note;
						note.Text += "\nThis photo contains a GPS location (where it was taken).";
					}
				}
				Field1.Text = _originalValues[0];
				Field2.Text = _originalValues[1];
				Field3.Text = _originalValues[2];
				Field4.Text = _originalValues[3];
			}
			catch (Exception ex)
			{
				Log.Error("Metadata read failed: " + _path, ex);
				Note.Text = "Could not read this file: " + ex.Message;
				Button saveButton = SaveButton;
				bool flag2 = (((UIElement)RemoveAllButton).IsEnabled = false);
				((UIElement)saveButton).IsEnabled = flag2;
			}
		}

		private string? IfChanged(int i, string value)
		{
			if (!(value == _originalValues[i]))
			{
				return value;
			}
			return null;
		}

		private async Task RunAsync(bool save)
		{
			if (!save && (int)MessageBox.Show((Window)(object)this, "Save a copy of this file without any metadata?\nThe original file is not changed.", "Remove all metadata", (MessageBoxButton)1, (MessageBoxImage)32) != 1)
			{
				return;
			}
			Button saveButton = SaveButton;
			bool isEnabled = (((UIElement)RemoveAllButton).IsEnabled = false);
			((UIElement)saveButton).IsEnabled = isEnabled;
			string f1 = Field1.Text;
			string f2 = Field2.Text;
			string f3 = Field3.Text;
			string f4 = Field4.Text;
			bool removeGps = ((ToggleButton)RemoveGpsBox).IsChecked == true && (int)((UIElement)RemoveGpsBox).Visibility == 0;
			try
			{
				string text = default;
				if (save && !_isPdf && f4.Trim().Length > 0 && !ImageMetadata.TryNormalizeDate(f4, ref text))
				{
					throw new ArgumentException("Date taken must look like 2024-05-31 14:20.");
				}
				string text2 = await Task.Run(() =>
				{
					//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
					//IL_00d0: Expected Obj, but got Unknown
					//IL_0049: Unknown result type (might be due to invalid IL or missing references)
					//IL_0053: Expected Obj, but got Unknown
					if (_isPdf)
					{
						if (!save)
						{
							return PdfMetadata.RemoveAll(_path);
						}
						return PdfMetadata.Write(_path, new PdfInfoFields(f1, f2, f3, f4));
					}
					return (!save) ? ImageMetadata.RemoveAll(_path) : ImageMetadata.Write(_path, new BasicMetadata(IfChanged(0, f1), IfChanged(1, f2), IfChanged(2, f3), IfChanged(3, f4)), removeGps);
				});
				ToastWindow.Show(save ? "Metadata saved" : "Metadata removed", Path.GetFileName(text2), new string[1] { text2 });
				ShellReveal.Select(new string[1] { text2 });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Metadata write failed: " + _path, ex);
				Note.Text = ex.Message;
				((UIElement)SaveButton).IsEnabled = _isPdf || ImageMetadata.CanEdit(_path);
				((UIElement)RemoveAllButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/metadatadialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Expected Obj, but got Unknown
			//IL_005c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0066: Expected Obj, but got Unknown
			//IL_0069: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Expected Obj, but got Unknown
			//IL_0076: Unknown result type (might be due to invalid IL or missing references)
			//IL_0080: Expected Obj, but got Unknown
			//IL_0083: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Expected Obj, but got Unknown
			//IL_0090: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Expected Obj, but got Unknown
			//IL_009d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a7: Expected Obj, but got Unknown
			//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b4: Expected Obj, but got Unknown
			//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c1: Expected Obj, but got Unknown
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Expected Obj, but got Unknown
			//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00db: Expected Obj, but got Unknown
			//IL_00de: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e8: Expected Obj, but got Unknown
			//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f5: Expected Obj, but got Unknown
			//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
			//IL_0102: Expected Obj, but got Unknown
			//IL_0105: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Expected Obj, but got Unknown
			//IL_0112: Unknown result type (might be due to invalid IL or missing references)
			//IL_011c: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				EntriesList = (ListView)target;
				break;
			case 4:
				FieldsPanel = (StackPanel)target;
				break;
			case 5:
				Label1 = (TextBlock)target;
				break;
			case 6:
				Field1 = (TextBox)target;
				break;
			case 7:
				Label2 = (TextBlock)target;
				break;
			case 8:
				Field2 = (TextBox)target;
				break;
			case 9:
				Label3 = (TextBlock)target;
				break;
			case 10:
				Field3 = (TextBox)target;
				break;
			case 11:
				Label4 = (TextBlock)target;
				break;
			case 12:
				Field4 = (TextBox)target;
				break;
			case 13:
				RemoveGpsBox = (CheckBox)target;
				break;
			case 14:
				Note = (TextBlock)target;
				break;
			case 15:
				RemoveAllButton = (Button)target;
				break;
			case 16:
				SaveButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	public sealed class PageTile : INotifyPropertyChanged
	{
		private int _rotation;

		public required int SourceIndex { get; init; }

		public required BitmapSource Thumb { get; init; }

		public string Label => $"Page {SourceIndex + 1}";

		public int Rotation
		{
			get
			{
				return _rotation;
			}
			set
			{
				_rotation = (value % 360 + 360) % 360;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Rotation"));
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;
	}
	public class OrganizeDialog : Window, IComponentConnector
	{
		[Serializable]
		[CompilerGenerated]
		private sealed class <>c
		{
			public static readonly <>c <>9 = new <>c();

			public static DragEventHandler <>9__5_11;

			public static Func<PageTile, PageOp> <>9__19_0;

			internal void <.ctor>b__5_11(object _, DragEventArgs e)
			{
				e.Effects = (DragDropEffects)2;
				((RoutedEventArgs)e).Handled = true;
			}

			internal PageOp <SaveAsync>b__19_0(PageTile t)
			{
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0012: Expected Obj, but got Unknown
				return new PageOp(t.SourceIndex, t.Rotation);
			}
		}

		private readonly string _path;

		private readonly ObservableCollection<PageTile> _tiles = new ObservableCollection<PageTile>();

		private List<PageTile> _original = new List<PageTile>();

		private Point _dragStart;

		private PageTile? _dragTile;

		internal Border Root;

		internal TextBlock FileName;

		internal Button RotateLeftButton;

		internal Button RotateRightButton;

		internal Button MoveEarlierButton;

		internal Button MoveLaterButton;

		internal Button DeleteButton;

		internal Button ResetButton;

		internal TextBlock Loading;

		internal ListBox Pages;

		internal Button SaveButton;

		internal TextBlock Hint;

		private bool _contentLoaded;

		private List<PageTile> Selected => (from PageTile t in Pages.SelectedItems
			orderby _tiles.IndexOf(t)
			select t).ToList();

		private bool Changed
		{
			get
			{
				if (_tiles.Count == _original.Count)
				{
					return _tiles.Where((PageTile t, int i) => t != _original[i] || t.Rotation != 0).Any();
				}
				return true;
			}
		}

		public OrganizeDialog(string path)
		{
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_006e: Expected Obj, but got Unknown
			//IL_007b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0085: Expected Obj, but got Unknown
			//IL_0092: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Expected Obj, but got Unknown
			//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b3: Expected Obj, but got Unknown
			//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ca: Expected Obj, but got Unknown
			//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Expected Obj, but got Unknown
			//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f8: Expected Obj, but got Unknown
			//IL_0105: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Expected Obj, but got Unknown
			//IL_011c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0126: Expected Obj, but got Unknown
			//IL_012e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0138: Expected Obj, but got Unknown
			//IL_0145: Unknown result type (might be due to invalid IL or missing references)
			//IL_014f: Expected Obj, but got Unknown
			//IL_015c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0166: Expected Obj, but got Unknown
			//IL_019d: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a7: Expected Obj, but got Unknown
			//IL_01af: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b9: Expected Obj, but got Unknown
			//IL_0180: Unknown result type (might be due to invalid IL or missing references)
			//IL_0185: Unknown result type (might be due to invalid IL or missing references)
			//IL_018b: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			((ItemsControl)Pages).ItemsSource = _tiles;
			((ButtonBase)RotateLeftButton).Click += (object _, RoutedEventArgs _) =>
			{
				Rotate(-90);
			};
			((ButtonBase)RotateRightButton).Click += (object _, RoutedEventArgs _) =>
			{
				Rotate(90);
			};
			((ButtonBase)MoveEarlierButton).Click += (object _, RoutedEventArgs _) =>
			{
				Move(-1);
			};
			((ButtonBase)MoveLaterButton).Click += (object _, RoutedEventArgs _) =>
			{
				Move(1);
			};
			((ButtonBase)DeleteButton).Click += (object _, RoutedEventArgs _) =>
			{
				DeleteSelected();
			};
			((ButtonBase)ResetButton).Click += (object _, RoutedEventArgs _) =>
			{
				Reset();
			};
			((ButtonBase)SaveButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await SaveAsync();
			};
			((Selector)Pages).SelectionChanged += (object _, SelectionChangedEventArgs _) =>
			{
				UpdateButtons();
			};
			((UIElement)Pages).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				if ((int)e.Key == 32)
				{
					DeleteSelected();
				}
			};
			((UIElement)this).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				if ((int)e.Key == 13)
				{
					((Window)this).Close();
				}
			};
			((UIElement)Pages).PreviewMouseLeftButtonDown += (object _, MouseButtonEventArgs e) =>
			{
				//IL_0008: Unknown result type (might be due to invalid IL or missing references)
				//IL_000d: Unknown result type (might be due to invalid IL or missing references)
				_dragStart = ((MouseEventArgs)e).GetPosition((IInputElement)(object)Pages);
				_dragTile = TileAt(((RoutedEventArgs)e).OriginalSource);
			};
			((UIElement)Pages).PreviewMouseMove += OnDragMove;
			ListBox pages = Pages;
			DragEventHandler val = <>c.<>9__5_11;
			if (val == null)
			{
				DragEventHandler val2 = (object _, DragEventArgs e) =>
				{
					e.Effects = (DragDropEffects)2;
					((RoutedEventArgs)e).Handled = true;
				};
				<>c.<>9__5_11 = val2;
				val = val2;
			}
			((UIElement)pages).DragOver += val;
			((UIElement)Pages).Drop += OnDrop;
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
			UpdateButtons();
		}

		private async Task LoadAsync()
		{
			try
			{
				List<byte[]> list = await PdfConverter.RenderPagesAsync(_path, 45.0, default(CancellationToken));
				for (int i = 0; i < list.Count; i++)
				{
					BitmapImage val = new BitmapImage();
					val.BeginInit();
					val.CacheOption = (BitmapCacheOption)1;
					val.StreamSource = new MemoryStream(list[i]);
					val.EndInit();
					((Freezable)val).Freeze();
					_tiles.Add(new PageTile
					{
						SourceIndex = i,
						Thumb = (BitmapSource)(object)val
					});
				}
				_original = _tiles.ToList();
				((UIElement)Loading).Visibility = (Visibility)2;
				UpdateButtons();
			}
			catch (Exception ex)
			{
				Log.Error("Organize load failed: " + _path, ex);
				Loading.Text = "Could not open this PDF: " + ex.Message;
			}
		}

		private void Rotate(int degrees)
		{
			foreach (PageTile item in Selected)
			{
				item.Rotation += degrees;
			}
			UpdateButtons();
		}

		private void Move(int direction)
		{
			List<PageTile> selected = Selected;
			if (selected.Count == 0)
			{
				return;
			}
			foreach (PageTile item in (direction < 0) ? selected : Enumerable.Reverse(selected).ToList())
			{
				int num = _tiles.IndexOf(item);
				int num2 = num + direction;
				if (num2 >= 0 && num2 < _tiles.Count && !selected.Contains(_tiles[num2]))
				{
					_tiles.Move(num, num2);
				}
			}
			Pages.ScrollIntoView((object)selected[0]);
			UpdateButtons();
		}

		private void DeleteSelected()
		{
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			List<PageTile> selected = Selected;
			if (selected.Count == 0)
			{
				return;
			}
			if (selected.Count == _tiles.Count)
			{
				MessageBox.Show((Window)(object)this, "A PDF needs at least one page.", "Organize", (MessageBoxButton)0, (MessageBoxImage)64);
				return;
			}
			foreach (PageTile item in selected)
			{
				_tiles.Remove(item);
			}
			UpdateButtons();
		}

		private void Reset()
		{
			_tiles.Clear();
			foreach (PageTile item in _original)
			{
				item.Rotation = 0;
				_tiles.Add(item);
			}
			UpdateButtons();
		}

		private void UpdateButtons()
		{
			bool flag = Pages.SelectedItems.Count > 0;
			Button rotateLeftButton = RotateLeftButton;
			Button rotateRightButton = RotateRightButton;
			bool flag2 = (((UIElement)DeleteButton).IsEnabled = flag);
			bool isEnabled = (((UIElement)rotateRightButton).IsEnabled = flag2);
			((UIElement)rotateLeftButton).IsEnabled = isEnabled;
			Button moveEarlierButton = MoveEarlierButton;
			isEnabled = (((UIElement)MoveLaterButton).IsEnabled = flag);
			((UIElement)moveEarlierButton).IsEnabled = isEnabled;
			((UIElement)SaveButton).IsEnabled = _tiles.Count > 0 && Changed;
			int num = _original.Count - _tiles.Count;
			if (_original.Count > 0)
			{
				Hint.Text = $"{_tiles.Count} page{((_tiles.Count == 1) ? "" : "s")}" + ((num > 0) ? $" ({num} deleted)" : "") + ". Drag pages to change the order. Ctrl+click or Shift+click to select many.";
			}
		}

		private static PageTile? TileAt(object source)
		{
			DependencyObject val = (DependencyObject)((source is DependencyObject) ? source : null);
			while (val != null && !(val is ListBoxItem))
			{
				val = VisualTreeHelper.GetParent(val);
			}
			DependencyObject obj = ((val is ListBoxItem) ? val : null);
			return ((obj != null) ? ((FrameworkElement)obj).DataContext : null) as PageTile;
		}

		private void OnDragMove(object sender, MouseEventArgs e)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0088: Expected Obj, but got Unknown
			//IL_0083: Unknown result type (might be due to invalid IL or missing references)
			if ((int)e.LeftButton == 1 && _dragTile != null)
			{
				Point position = e.GetPosition((IInputElement)(object)Pages);
				if (!(Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) || !(Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance))
				{
					PageTile dragTile = _dragTile;
					_dragTile = null;
					DragDrop.DoDragDrop((DependencyObject)(object)Pages, (object)new DataObject(typeof(PageTile), (object)dragTile), (DragDropEffects)2);
				}
			}
		}

		private void OnDrop(object sender, DragEventArgs e)
		{
			if (e.Data.GetData(typeof(PageTile)) is PageTile pageTile)
			{
				PageTile pageTile2 = TileAt(((RoutedEventArgs)e).OriginalSource);
				int num = _tiles.IndexOf(pageTile);
				int num2 = ((pageTile2 == null) ? (_tiles.Count - 1) : _tiles.IndexOf(pageTile2));
				if (num >= 0 && num2 >= 0 && num != num2)
				{
					_tiles.Move(num, num2);
				}
				((Selector)Pages).SelectedItem = pageTile;
				UpdateButtons();
			}
		}

		private async Task SaveAsync()
		{
			((UIElement)SaveButton).IsEnabled = false;
			List<PageOp> ops = ((IEnumerable<PageTile>)_tiles).Select((Func<PageTile, PageOp>)((PageTile t) =>
			{
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0012: Expected Obj, but got Unknown
				return new PageOp(t.SourceIndex, t.Rotation);
			})).ToList();
			try
			{
				string text = await Task.Run(() => PdfPages.Organize(_path, (IReadOnlyList<PageOp>)ops));
				ToastWindow.Show("Pages organized", $"{Path.GetFileName(text)} ({ops.Count} pages)", new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Organize failed: " + _path, ex);
				ToastWindow.Show("Organize failed", ex.Message, null, isError: true);
				((UIElement)SaveButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/organizedialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Expected Obj, but got Unknown
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Expected Obj, but got Unknown
			//IL_0059: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Expected Obj, but got Unknown
			//IL_0066: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Expected Obj, but got Unknown
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Expected Obj, but got Unknown
			//IL_0080: Unknown result type (might be due to invalid IL or missing references)
			//IL_008a: Expected Obj, but got Unknown
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0097: Expected Obj, but got Unknown
			//IL_009a: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Expected Obj, but got Unknown
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b1: Expected Obj, but got Unknown
			//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Expected Obj, but got Unknown
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cb: Expected Obj, but got Unknown
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d8: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				RotateLeftButton = (Button)target;
				break;
			case 4:
				RotateRightButton = (Button)target;
				break;
			case 5:
				MoveEarlierButton = (Button)target;
				break;
			case 6:
				MoveLaterButton = (Button)target;
				break;
			case 7:
				DeleteButton = (Button)target;
				break;
			case 8:
				ResetButton = (Button)target;
				break;
			case 9:
				Loading = (TextBlock)target;
				break;
			case 10:
				Pages = (ListBox)target;
				break;
			case 11:
				SaveButton = (Button)target;
				break;
			case 12:
				Hint = (TextBlock)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	internal static class PreviewLoader
	{
		public static (BitmapSource Bitmap, int Width, int Height) Load(string path, uint maxSide = 2000u)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Expected Obj, but got Unknown
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0068: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Expected Obj, but got Unknown
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Expected Obj, but got Unknown
			MagickImage val = new MagickImage(path);
			try
			{
				val.AutoOrient();
				int width = (int)val.Width;
				int height = (int)val.Height;
				if (Math.Max(width, height) > maxSide)
				{
					val.Resize((IMagickGeometry)new MagickGeometry(maxSide, maxSide));
				}
				val.Format = (MagickFormat)194;
				BitmapImage val2 = new BitmapImage();
				val2.BeginInit();
				val2.CacheOption = (BitmapCacheOption)1;
				val2.StreamSource = new MemoryStream(val.ToByteArray());
				val2.EndInit();
				((Freezable)val2).Freeze();
				return (Bitmap: (BitmapSource)val2, Width: width, Height: height);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}
	public sealed record QrRow(string Where, string Text, Uri? Link)
	{
		public Visibility LinkVisibility
		{
			get
			{
				if (Link != null)
				{
					return (Visibility)0;
				}
				return (Visibility)2;
			}
		}
	}
	public class QrDialog : Window, IComponentConnector, IStyleConnector
	{
		private readonly IReadOnlyList<string> _files;

		private readonly List<QrRow> _rows = new List<QrRow>();

		internal Border Root;

		internal TextBlock Status;

		internal ProgressBar Busy;

		internal ItemsControl Results;

		internal Button CopyAllButton;

		private bool _contentLoaded;

		public QrDialog(IReadOnlyList<string> files)
		{
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Expected Obj, but got Unknown
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_files = files;
			((ButtonBase)CopyAllButton).Click += (object _, RoutedEventArgs _) =>
			{
				SetClipboard(string.Join(Environment.NewLine, _rows.Select((QrRow r) => r.Text)));
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await ScanAsync();
			};
		}

		private async Task ScanAsync()
		{
			List<string> errors = new List<string>();
			foreach (string f in _files)
			{
				Status.Text = "Searching " + Path.GetFileName(f) + "…";
				try
				{
					foreach (CodeHit item in await QrReader.ReadFileAsync(f, default(CancellationToken)))
					{
						string text = ((_files.Count > 1) ? (Path.GetFileName(f) + " · ") : "") + ((item.Page > 0) ? $"Page {item.Page} · " : "") + item.Format;
						_rows.Add(new QrRow(text, item.Text, item.Link));
					}
				}
				catch (Exception ex)
				{
					Log.Error("QR read failed: " + f, ex);
					errors.Add(Path.GetFileName(f) + ": " + ex.Message);
				}
			}
			((UIElement)Busy).Visibility = (Visibility)2;
			Results.ItemsSource = _rows;
			((UIElement)CopyAllButton).IsEnabled = _rows.Count > 0;
			TextBlock status = Status;
			status.Text = _rows.Count switch
			{
				0 => "No QR codes found. Tip: very small or blurry codes may not be readable.", 
				1 => "1 code found.", 
				_ => $"{_rows.Count} codes found.", 
			} + ((errors.Count > 0) ? ("\n" + string.Join("\n", errors)) : "");
		}

		private void Copy_Click(object sender, RoutedEventArgs e)
		{
			object obj = ((sender is Button) ? sender : null);
			if (((obj != null) ? ((FrameworkElement)obj).Tag : null) is QrRow qrRow)
			{
				SetClipboard(qrRow.Text);
			}
		}

		private async void Open_Click(object sender, RoutedEventArgs e)
		{
			object obj = ((sender is Button) ? sender : null);
			if (!(((obj != null) ? ((FrameworkElement)obj).Tag : null) is QrRow qrRow))
			{
				return;
			}
			Uri link = qrRow.Link;
			Uri uri = default;
			if ((object)link == null || !WebLinks.TryGetWebLink(link.AbsoluteUri, ref uri))
			{
				return;
			}
			try
			{
				TaskAwaiter<bool> taskAwaiter = WindowsRuntimeSystemExtensions.GetAwaiter<bool>(Launcher.LaunchUriAsync(uri));
				if (!taskAwaiter.IsCompleted)
				{
					await taskAwaiter;
					TaskAwaiter<bool> taskAwaiter2 = default;
					taskAwaiter = taskAwaiter2;
				}
				if (!taskAwaiter.GetResult())
				{
					ToastWindow.Show("Could not open the link", "No browser accepted it. Use Copy instead.", null, isError: true);
				}
			}
			catch (Exception ex)
			{
				Log.Error("Open link failed", ex);
				ToastWindow.Show("Could not open the link", ex.Message, null, isError: true);
			}
		}

		private static void SetClipboard(string text)
		{
			try
			{
				Clipboard.SetText(text);
				ToastWindow.Show("Copied", (text.Length > 80) ? (text.Substring(0, 80) + "…") : text);
			}
			catch (Exception ex)
			{
				Log.Error("Clipboard failed", ex);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/qrdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Expected Obj, but got Unknown
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Expected Obj, but got Unknown
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Expected Obj, but got Unknown
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Expected Obj, but got Unknown
			//IL_005c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0066: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				Status = (TextBlock)target;
				break;
			case 3:
				Busy = (ProgressBar)target;
				break;
			case 4:
				Results = (ItemsControl)target;
				break;
			case 7:
				CopyAllButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IStyleConnector.Connect(int connectionId, object target)
		{
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Expected Obj, but got Unknown
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_002e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 5:
				((ButtonBase)(Button)target).Click += Copy_Click;
				break;
			case 6:
				((ButtonBase)(Button)target).Click += Open_Click;
				break;
			}
		}
	}
	public class RedactDialog : Window, IComponentConnector
	{
		private readonly string _path;

		private readonly List<CropBox> _boxes = new List<CropBox>();

		private double _imgW;

		private double _imgH;

		private Nullable _start;

		private Nullable _drawing;

		private int _selected = -1;

		internal Border Root;

		internal TextBlock FileName;

		internal TextBlock Loading;

		internal Viewbox View;

		internal Canvas Stage;

		internal Image Img;

		internal Canvas Boxes;

		internal Button UndoButton;

		internal Button ClearButton;

		internal Button SaveButton;

		internal TextBlock Hint;

		private bool _contentLoaded;

		private double Scale
		{
			get
			{
				if (!(_imgW <= 0.0))
				{
					return Math.Max(0.0001, Math.Min(((FrameworkElement)View).ActualWidth / _imgW, ((FrameworkElement)View).ActualHeight / _imgH));
				}
				return 1.0;
			}
		}

		public RedactDialog(string path)
		{
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Expected Obj, but got Unknown
			//IL_0066: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Expected Obj, but got Unknown
			//IL_007d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Expected Obj, but got Unknown
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_009e: Expected Obj, but got Unknown
			//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b5: Expected Obj, but got Unknown
			//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cc: Expected Obj, but got Unknown
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Expected Obj, but got Unknown
			//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f5: Expected Obj, but got Unknown
			//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_0107: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			((UIElement)Stage).MouseLeftButtonDown += OnDown;
			((UIElement)Stage).MouseMove += OnMove;
			((UIElement)Stage).MouseLeftButtonUp += OnUp;
			((FrameworkElement)View).SizeChanged += (object _, SizeChangedEventArgs _) =>
			{
				Redraw();
			};
			((ButtonBase)UndoButton).Click += (object _, RoutedEventArgs _) =>
			{
				Undo();
			};
			((ButtonBase)ClearButton).Click += (object _, RoutedEventArgs _) =>
			{
				_boxes.Clear();
				_selected = -1;
				Redraw();
			};
			((ButtonBase)SaveButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await SaveAsync();
			};
			((UIElement)this).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				//IL_0012: Unknown result type (might be due to invalid IL or missing references)
				//IL_0019: Invalid comparison between Unknown and I4
				//IL_0044: Unknown result type (might be due to invalid IL or missing references)
				//IL_004b: Invalid comparison between Unknown and I4
				//IL_004d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0053: Invalid comparison between Unknown and I4
				if ((int)e.Key == 13)
				{
					((Window)this).Close();
				}
				else if ((int)e.Key == 32 && _selected >= 0)
				{
					_boxes.RemoveAt(_selected);
					_selected = -1;
					Redraw();
				}
				else if ((int)e.Key == 69 && (int)Keyboard.Modifiers == 2)
				{
					Undo();
				}
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				await LoadAsync();
			};
		}

		private async Task LoadAsync()
		{
			try
			{
				(BitmapSource, int, int) tuple = await Task.Run(() => PreviewLoader.Load(_path));
				BitmapSource item = tuple.Item1;
				int item2 = tuple.Item2;
				int item3 = tuple.Item3;
				_imgW = item2;
				_imgH = item3;
				Img.Source = (ImageSource)(object)item;
				Image img = Img;
				Canvas stage = Stage;
				double num = (((FrameworkElement)Boxes).Width = item2);
				double width = (((FrameworkElement)stage).Width = num);
				((FrameworkElement)img).Width = width;
				Image img2 = Img;
				Canvas stage2 = Stage;
				num = (((FrameworkElement)Boxes).Height = item3);
				width = (((FrameworkElement)stage2).Height = num);
				((FrameworkElement)img2).Height = width;
				((UIElement)Loading).Visibility = (Visibility)2;
				((UIElement)Stage).Visibility = (Visibility)0;
			}
			catch (Exception ex)
			{
				Log.Error("Redact preview failed: " + _path, ex);
				Loading.Text = "Could not open this image: " + ex.Message;
			}
		}

		private Point Clamp(Point p)
		{
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			return new Point(Math.Clamp(p.X, 0.0, _imgW), Math.Clamp(p.Y, 0.0, _imgH));
		}

		private void OnDown(object sender, MouseButtonEventArgs e)
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			Point value = Clamp(((MouseEventArgs)e).GetPosition((IInputElement)(object)Stage));
			for (int num = _boxes.Count - 1; num >= 0; num--)
			{
				CropBox val = _boxes[num];
				if (value.X >= val.X && value.X <= val.Right && value.Y >= val.Y && value.Y <= val.Bottom)
				{
					_selected = num;
					Redraw();
					return;
				}
			}
			_selected = -1;
			_start = value;
			((UIElement)Stage).CaptureMouse();
		}

		private void OnMove(object sender, MouseEventArgs e)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Unknown result type (might be due to invalid IL or missing references)
			Nullable start = _start;
			if (start.HasValue)
			{
				Point valueOrDefault = start.GetValueOrDefault();
				Point val = Clamp(e.GetPosition((IInputElement)(object)Stage));
				_drawing = new CropBox(Math.Min(valueOrDefault.X, val.X), Math.Min(valueOrDefault.Y, val.Y), Math.Abs(val.X - valueOrDefault.X), Math.Abs(val.Y - valueOrDefault.Y));
				Redraw();
			}
		}

		private void OnUp(object sender, MouseButtonEventArgs e)
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			((UIElement)Stage).ReleaseMouseCapture();
			Nullable drawing = _drawing;
			if (drawing.HasValue)
			{
				CropBox valueOrDefault = drawing.GetValueOrDefault();
				if (valueOrDefault.W * Scale >= 4.0 && valueOrDefault.H * Scale >= 4.0)
				{
					_boxes.Add(valueOrDefault);
				}
			}
			_start = null;
			_drawing = null;
			Redraw();
		}

		private void Undo()
		{
			if (_boxes.Count != 0)
			{
				_boxes.RemoveAt(_boxes.Count - 1);
				_selected = -1;
				Redraw();
			}
		}

		private void Redraw()
		{
			//IL_0059: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0060: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_0090: Unknown result type (might be due to invalid IL or missing references)
			//IL_0095: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00af: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
			//IL_00da: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e9: Expected Obj, but got Unknown
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Expected Obj, but got Unknown
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Expected Obj, but got Unknown
			((Panel)Boxes).Children.Clear();
			double scale = Scale;
			List<(CropBox, int)> list = _boxes.Select((CropBox b, int i) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				return (b: b, i: i);
			}).ToList();
			Nullable drawing = _drawing;
			if (drawing.HasValue)
			{
				CropBox valueOrDefault = drawing.GetValueOrDefault();
				list.Add((valueOrDefault, -2));
			}
			foreach (var item3 in list)
			{
				CropBox item = item3.Item1;
				int item2 = item3.Item2;
				Rectangle val = new Rectangle
				{
					Width = item.W,
					Height = item.H,
					Fill = (Brush)(object)Brushes.Black,
					Stroke = (Brush)((item2 != _selected && item2 != -2) ? ((SolidColorBrush)null) : new SolidColorBrush(Color.FromRgb(byte.MaxValue, (byte)138, (byte)31))),
					StrokeThickness = 2.0 / scale
				};
				Canvas.SetLeft((UIElement)(object)val, item.X);
				Canvas.SetTop((UIElement)(object)val, item.Y);
				((Panel)Boxes).Children.Add((UIElement)(object)val);
			}
			((UIElement)SaveButton).IsEnabled = _boxes.Count > 0;
			((UIElement)UndoButton).IsEnabled = _boxes.Count > 0;
		}

		private async Task SaveAsync()
		{
			((UIElement)SaveButton).IsEnabled = false;
			List<CropBox> boxes = _boxes.ToList();
			try
			{
				string text = await Task.Run(() => ImageRedactor.Save(_path, (IReadOnlyList<CropBox>)boxes));
				ToastWindow.Show("Redacted", $"{Path.GetFileName(text)} ({boxes.Count} area{((boxes.Count == 1) ? "" : "s")}, metadata removed)", new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Redact failed: " + _path, ex);
				ToastWindow.Show("Redact failed", ex.Message, null, isError: true);
				((UIElement)SaveButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/redactdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Expected Obj, but got Unknown
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Expected Obj, but got Unknown
			//IL_0055: Unknown result type (might be due to invalid IL or missing references)
			//IL_005f: Expected Obj, but got Unknown
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_006c: Expected Obj, but got Unknown
			//IL_006f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Expected Obj, but got Unknown
			//IL_007c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Expected Obj, but got Unknown
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_0093: Expected Obj, but got Unknown
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Expected Obj, but got Unknown
			//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ad: Expected Obj, but got Unknown
			//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ba: Expected Obj, but got Unknown
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c7: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				Loading = (TextBlock)target;
				break;
			case 4:
				View = (Viewbox)target;
				break;
			case 5:
				Stage = (Canvas)target;
				break;
			case 6:
				Img = (Image)target;
				break;
			case 7:
				Boxes = (Canvas)target;
				break;
			case 8:
				UndoButton = (Button)target;
				break;
			case 9:
				ClearButton = (Button)target;
				break;
			case 10:
				SaveButton = (Button)target;
				break;
			case 11:
				Hint = (TextBlock)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	public class SplitDialog : Window, IComponentConnector
	{
		private readonly string _path;

		private int _pages;

		internal Border Root;

		internal TextBlock FileName;

		internal TextBlock PageInfo;

		internal RadioButton EveryPage;

		internal RadioButton EveryN;

		internal TextBox NBox;

		internal RadioButton Custom;

		internal TextBox RangesBox;

		internal TextBlock OutputLine;

		internal Button SplitButton;

		private bool _contentLoaded;

		public SplitDialog(string path)
		{
			//IL_0060: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Expected Obj, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Expected Obj, but got Unknown
			//IL_0098: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a2: Expected Obj, but got Unknown
			//IL_00af: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b9: Expected Obj, but got Unknown
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cb: Expected Obj, but got Unknown
			InitializeComponent();
			DialogChrome.Apply((Window)(object)this, Root);
			_path = path;
			FileName.Text = Path.GetFileName(path);
			RadioButton[] array = new RadioButton[3] { EveryPage, EveryN, Custom };
			for (int i = 0; i < array.Length; i++)
			{
				((ToggleButton)array[i]).Checked += (object _, RoutedEventArgs _) =>
				{
					Update();
				};
			}
			((TextBoxBase)NBox).TextChanged += (object _, TextChangedEventArgs _) =>
			{
				if (((UIElement)NBox).IsKeyboardFocused)
				{
					((ToggleButton)EveryN).IsChecked = true;
				}
				Update();
			};
			((TextBoxBase)RangesBox).TextChanged += (object _, TextChangedEventArgs _) =>
			{
				if (((UIElement)RangesBox).IsKeyboardFocused)
				{
					((ToggleButton)Custom).IsChecked = true;
				}
				Update();
			};
			((ButtonBase)SplitButton).Click += async (object _, RoutedEventArgs _) =>
			{
				await SplitAsync();
			};
			((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
			{
				try
				{
					_pages = await Task.Run(() => PdfPages.Count(_path));
					PageInfo.Text = $"{_pages} page{((_pages == 1) ? "" : "s")}";
					RangesBox.Text = ((_pages > 3) ? $"1-{_pages / 2}, {_pages / 2 + 1}-" : "1");
					Update();
				}
				catch (Exception ex)
				{
					Log.Error("Split open failed: " + _path, ex);
					OutputLine.Text = "Could not open this PDF: " + ex.Message;
				}
			};
		}

		private (List<(int From, int To)>? Ranges, string? Error) Plan()
		{
			if (_pages == 0)
			{
				return (Ranges: null, Error: "Loading…");
			}
			try
			{
				if (((ToggleButton)EveryPage).IsChecked == true)
				{
					return (Ranges: PageRanges.EveryN(1, _pages), Error: null);
				}
				if (((ToggleButton)EveryN).IsChecked == true)
				{
					if (!int.TryParse(NBox.Text.Trim(), out var result) || result < 1)
					{
						return (Ranges: null, Error: "Type a number of pages, for example 2.");
					}
					return (Ranges: PageRanges.EveryN(result, _pages), Error: null);
				}
				return (Ranges: PageRanges.Parse(RangesBox.Text, _pages), Error: null);
			}
			catch (FormatException ex)
			{
				return (Ranges: null, Error: ex.Message);
			}
		}

		private void Update()
		{
			if (OutputLine != null)
			{
				var (list, text) = Plan();
				((UIElement)SplitButton).IsEnabled = list != null;
				OutputLine.Text = text ?? ((list.Count == 1) ? "Will make 1 PDF, saved next to the original." : $"Will make {list.Count} PDF files in a new folder \"{Path.GetFileNameWithoutExtension(_path)}-split\".");
			}
		}

		private async Task SplitAsync()
		{
			List<(int From, int To)> ranges;
			(ranges, _) = Plan();
			if (ranges == null)
			{
				return;
			}
			((UIElement)SplitButton).IsEnabled = false;
			try
			{
				string text = await Task.Run(() => PdfPages.Split(_path, (IReadOnlyList<(int, int)>)ranges));
				ToastWindow.Show("Split done", (ranges.Count == 1) ? Path.GetFileName(text) : $"{ranges.Count} PDF files in {Path.GetFileName(text)}", new string[1] { text });
				ShellReveal.Select(new string[1] { text });
				((Window)this).Close();
			}
			catch (Exception ex)
			{
				Log.Error("Split failed: " + _path, ex);
				OutputLine.Text = ex.Message;
				((UIElement)SplitButton).IsEnabled = true;
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri uri = new Uri("/Zest;component/ui/splitdialog.xaml", UriKind.Relative);
				Application.LoadComponent((object)this, uri);
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IComponentConnector.Connect(int connectionId, object target)
		{
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Expected Obj, but got Unknown
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_004e: Expected Obj, but got Unknown
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Expected Obj, but got Unknown
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0068: Expected Obj, but got Unknown
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0075: Expected Obj, but got Unknown
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Expected Obj, but got Unknown
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Expected Obj, but got Unknown
			//IL_0092: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Expected Obj, but got Unknown
			//IL_009f: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a9: Expected Obj, but got Unknown
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b6: Expected Obj, but got Unknown
			switch (connectionId)
			{
			case 1:
				Root = (Border)target;
				break;
			case 2:
				FileName = (TextBlock)target;
				break;
			case 3:
				PageInfo = (TextBlock)target;
				break;
			case 4:
				EveryPage = (RadioButton)target;
				break;
			case 5:
				EveryN = (RadioButton)target;
				break;
			case 6:
				NBox = (TextBox)target;
				break;
			case 7:
				Custom = (RadioButton)target;
				break;
			case 8:
				RangesBox = (TextBox)target;
				break;
			case 9:
				OutputLine = (TextBlock)target;
				break;
			case 10:
				SplitButton = (Button)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
	internal static class ThemeManager
	{
		[Serializable]
		[CompilerGenerated]
		private sealed class <>c
		{
			public static readonly <>c <>9 = new <>c();

			public static UserPreferenceChangedEventHandler <>9__9_0;

			internal void <Initialize>b__9_0(object _, UserPreferenceChangedEventArgs e)
			{
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				//IL_000d: Invalid comparison between Unknown and I4
				if ((int)e.Category != 4)
				{
					return;
				}
				bool dark = ReadSystemDark();
				if (dark == IsDark)
				{
					return;
				}
				Application current = Application.Current;
				if (current != null)
				{
					((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)(() =>
					{
						Apply(dark);
					}), Array.Empty<object>());
				}
			}
		}

		private const string PersonalizeKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";

		private static ResourceDictionary? _current;

		public static bool IsDark { get; private set; }

		public static event Action? Changed;

		public static void Initialize()
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Expected Obj, but got Unknown
			Apply(ReadSystemDark());
			UserPreferenceChangedEventHandler val = <>c.<>9__9_0;
			if (val == null)
			{
				UserPreferenceChangedEventHandler val2 = (object _, UserPreferenceChangedEventArgs e) =>
				{
					//IL_0007: Unknown result type (might be due to invalid IL or missing references)
					//IL_000d: Invalid comparison between Unknown and I4
					if ((int)e.Category == 4)
					{
						bool dark = ReadSystemDark();
						if (dark != IsDark)
						{
							Application current = Application.Current;
							if (current != null)
							{
								((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)(() =>
								{
									Apply(dark);
								}), Array.Empty<object>());
							}
						}
					}
				};
				<>c.<>9__9_0 = val2;
				val = val2;
			}
			SystemEvents.UserPreferenceChanged += val;
		}

		private static bool ReadSystemDark()
		{
			string environmentVariable = Environment.GetEnvironmentVariable("ZEST_THEME");
			if (environmentVariable == "dark")
			{
				return true;
			}
			if (environmentVariable == "light")
			{
				return false;
			}
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
				return registryKey?.GetValue("AppsUseLightTheme") is int num && num == 0;
			}
			catch
			{
				return false;
			}
		}

		private static void Apply(bool dark)
		{
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0039: Expected Obj, but got Unknown
			Application current = Application.Current;
			if (current != null)
			{
				ResourceDictionary val = new ResourceDictionary
				{
					Source = new Uri("pack://application:,,,/UI/Themes/" + (dark ? "Dark" : "Light") + ".xaml")
				};
				Collection<ResourceDictionary> mergedDictionaries = current.Resources.MergedDictionaries;
				if (_current != null)
				{
					mergedDictionaries.Remove(_current);
				}
				mergedDictionaries.Insert(0, val);
				_current = val;
				IsDark = dark;
				Changed?.Invoke();
			}
		}
	}
	internal sealed class ToastWindow : Window
	{
		private static ToastWindow? _current;

		private const double Edge = 18.0;

		private const double Radius = 8.0;

		public static void Show(string title, string message, IReadOnlyList<string>? files = null, bool isError = false)
		{
			ToastWindow? current = _current;
			_current = new ToastWindow(title, message, files, isError);
			((Window)_current).Show();
			if (current != null)
			{
				((Window)current).Close();
			}
		}

		private ToastWindow(string title, string message, IReadOnlyList<string>? files, bool isError)
		{
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007e: Expected Obj, but got Unknown
			//IL_007e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0083: Unknown result type (might be due to invalid IL or missing references)
			//IL_008a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a5: Expected Obj, but got Unknown
			//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0107: Expected Obj, but got Unknown
			//IL_0117: Unknown result type (might be due to invalid IL or missing references)
			//IL_011c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0141: Unknown result type (might be due to invalid IL or missing references)
			//IL_014b: Unknown result type (might be due to invalid IL or missing references)
			//IL_015a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0163: Expected Obj, but got Unknown
			//IL_017f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0184: Unknown result type (might be due to invalid IL or missing references)
			//IL_0193: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_01da: Expected Obj, but got Unknown
			//IL_01e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0206: Unknown result type (might be due to invalid IL or missing references)
			//IL_020b: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0215: Expected Obj, but got Unknown
			//IL_021a: Expected Obj, but got Unknown
			//IL_0222: Unknown result type (might be due to invalid IL or missing references)
			//IL_0227: Unknown result type (might be due to invalid IL or missing references)
			//IL_023d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0243: Unknown result type (might be due to invalid IL or missing references)
			//IL_024d: Expected Obj, but got Unknown
			//IL_024d: Unknown result type (might be due to invalid IL or missing references)
			//IL_025c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0267: Unknown result type (might be due to invalid IL or missing references)
			//IL_026e: Unknown result type (might be due to invalid IL or missing references)
			//IL_027a: Expected Obj, but got Unknown
			//IL_027b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0282: Expected Obj, but got Unknown
			//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_02c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_02df: Unknown result type (might be due to invalid IL or missing references)
			//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_031b: Unknown result type (might be due to invalid IL or missing references)
			//IL_031c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0321: Unknown result type (might be due to invalid IL or missing references)
			//IL_0330: Unknown result type (might be due to invalid IL or missing references)
			//IL_033f: Unknown result type (might be due to invalid IL or missing references)
			//IL_034e: Unknown result type (might be due to invalid IL or missing references)
			//IL_036f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0370: Unknown result type (might be due to invalid IL or missing references)
			//IL_037f: Expected Obj, but got Unknown
			//IL_0381: Expected Obj, but got Unknown
			//IL_03b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_03c2: Expected Obj, but got Unknown
			//IL_03e6: Unknown result type (might be due to invalid IL or missing references)
			//IL_03f0: Expected Obj, but got Unknown
			//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_03f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_041e: Expected Obj, but got Unknown
			ToastWindow toastWindow = this;
			((Window)this).Title = "Zest message";
			((Window)this).WindowStyle = (WindowStyle)0;
			((Window)this).AllowsTransparency = true;
			((Control)this).Background = (Brush)(object)Brushes.Transparent;
			((Window)this).ShowInTaskbar = false;
			((Window)this).Topmost = true;
			((Window)this).ShowActivated = false;
			((Window)this).ResizeMode = (ResizeMode)0;
			((Window)this).SizeToContent = (SizeToContent)3;
			((Control)this).FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
			TextBlock val = new TextBlock
			{
				Text = title,
				FontSize = 14.0,
				FontWeight = FontWeights.SemiBold
			};
			((FrameworkElement)val).SetResourceReference(TextBlock.ForegroundProperty, (object)"Ink");
			TextBlock val2 = new TextBlock
			{
				Text = message,
				FontSize = 12.5,
				TextWrapping = (TextWrapping)2,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			};
			((FrameworkElement)val2).SetResourceReference(TextBlock.ForegroundProperty, (object)"Muted");
			StackPanel val3 = new StackPanel
			{
				Margin = new Thickness(12.0, 12.0, 18.0, 13.0),
				MaxWidth = 330.0,
				VerticalAlignment = (VerticalAlignment)1
			};
			((Panel)val3).Children.Add((UIElement)(object)val);
			((Panel)val3).Children.Add((UIElement)(object)val2);
			Grid val4 = new Grid
			{
				Width = 28.0,
				Height = 28.0,
				Margin = new Thickness(14.0, 12.0, 0.0, 12.0),
				VerticalAlignment = (VerticalAlignment)0
			};
			((Panel)val4).Children.Add((UIElement)new Ellipse
			{
				Fill = (Brush)(isError ? new SolidColorBrush(Color.FromRgb((byte)217, (byte)59, (byte)43)) : ((Brush)((FrameworkElement)this).FindResource((object)"OrangeGradient")))
			});
			((Panel)val4).Children.Add((UIElement)new TextBlock
			{
				Text = (isError ? "\ue783" : "\ue73e"),
				FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
				FontSize = 13.0,
				Foreground = (Brush)(object)Brushes.White,
				HorizontalAlignment = (HorizontalAlignment)1,
				VerticalAlignment = (VerticalAlignment)1
			});
			DockPanel val5 = new DockPanel();
			DockPanel.SetDock((UIElement)(object)val4, (Dock)0);
			((Panel)val5).Children.Add((UIElement)(object)val4);
			((Panel)val5).Children.Add((UIElement)(object)val3);
			Border val6 = new Border
			{
				Margin = new Thickness(18.0),
				CornerRadius = new CornerRadius(8.0),
				BorderThickness = new Thickness(1.0),
				Child = (UIElement)(object)val5
			};
			IReadOnlyList<string>? readOnlyList = files;
			((FrameworkElement)val6).Cursor = ((readOnlyList != null && readOnlyList.Count > 0) ? Cursors.Hand : null);
			((UIElement)val6).Effect = (Effect)new DropShadowEffect
			{
				BlurRadius = 20.0,
				ShadowDepth = 4.0,
				Direction = 270.0,
				Opacity = (ThemeManager.IsDark ? 0.5 : 0.18),
				Color = Colors.Black
			};
			Border val7 = val6;
			((FrameworkElement)val7).SetResourceReference(Border.BackgroundProperty, (object)"Surface");
			((FrameworkElement)val7).SetResourceReference(Border.BorderBrushProperty, (object)"Stroke");
			((ContentControl)this).Content = val7;
			((UIElement)this).MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				IReadOnlyList<string>? readOnlyList2 = files;
				if (readOnlyList2 != null && readOnlyList2.Count > 0)
				{
					ShellReveal.Select(files);
				}
				((Window)toastWindow).Close();
			};
			((Window)this).SourceInitialized += (object? _, EventArgs _) =>
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				WindowEffects.MakeNoActivateToolWindow(new WindowInteropHelper((Window)(object)toastWindow).Handle);
			};
			((FrameworkElement)this).Loaded += (object _, RoutedEventArgs _) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				//IL_0005: Unknown result type (might be due to invalid IL or missing references)
				//IL_006d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0072: Unknown result type (might be due to invalid IL or missing references)
				//IL_007c: Expected Obj, but got Unknown
				Rect workArea = SystemParameters.WorkArea;
				((Window)toastWindow).Left = workArea.Right - ((FrameworkElement)toastWindow).ActualWidth;
				((Window)toastWindow).Top = workArea.Bottom - ((FrameworkElement)toastWindow).ActualHeight;
				((UIElement)toastWindow).BeginAnimation(UIElement.OpacityProperty, (AnimationTimeline)new DoubleAnimation(0.0, 1.0, Duration.op_Implicit(TimeSpan.FromMilliseconds(160.0))));
			};
			DispatcherTimer life = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(isError ? 7.0 : 4.5)
			};
			life.Tick += (object? _, EventArgs _) =>
			{
				//IL_0022: Unknown result type (might be due to invalid IL or missing references)
				//IL_0027: Unknown result type (might be due to invalid IL or missing references)
				//IL_002d: Expected Obj, but got Unknown
				life.Stop();
				DoubleAnimation val8 = new DoubleAnimation(0.0, Duration.op_Implicit(TimeSpan.FromMilliseconds(250.0)));
				((Timeline)val8).Completed += (object? obj, EventArgs e) =>
				{
					((Window)toastWindow).Close();
				};
				((UIElement)toastWindow).BeginAnimation(UIElement.OpacityProperty, (AnimationTimeline)(object)val8);
			};
			life.Start();
			((Window)this).Closed += (object? _, EventArgs _) =>
			{
				if (_current == toastWindow)
				{
					_current = null;
				}
			};
		}
	}
	internal sealed record SliceDef(string Id, string Label, string Glyph, int Phase = 0)
	{
		public bool Enabled => Phase == 0;
	}
	internal static class WheelMenus
	{
		private const string Sync = "\ue8ab";

		private const string Shrink = "\ue73f";

		private const string CropIcon = "\ue7a8";

		private const string Pen = "\ue70f";

		private const string Palette = "\ue790";

		private const string Info = "\ue946";

		private const string Lock = "\ue72e";

		private const string Marker = "\ue7e6";

		private const string Cut = "\ue8c6";

		private const string List = "\ue8fd";

		private const string Qr = "\ued14";

		public static IReadOnlyList<SliceDef> MainMenu(FileKind kind)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Expected I4, but got Unknown
			return (kind - 1) switch
			{
				0 => (IReadOnlyList<SliceDef>)new SliceDef[8]
				{
					new SliceDef("convert", "Convert", "\ue8ab"),
					new SliceDef("compress", "Compress", "\ue73f"),
					new SliceDef("crop", "Crop", "\ue7a8"),
					new SliceDef("edit", "Edit", "\ue70f"),
					new SliceDef("addbg", "Add BG", "\ue790"),
					new SliceDef("metadata", "Metadata", "\ue946"),
					new SliceDef("redact", "Redact", "\ue72e"),
					new SliceDef("annotate", "Annotate", "\ue7e6")
				}, 
				1 => new SliceDef[6]
				{
					new SliceDef("convert", "Convert", "\ue8ab"),
					new SliceDef("compress", "Compress", "\ue73f"),
					new SliceDef("split", "Split", "\ue8c6"),
					new SliceDef("organize", "Organize", "\ue8fd"),
					new SliceDef("readqr", "Read QR", "\ued14"),
					new SliceDef("metadata", "Metadata", "\ue946")
				}, 
				2 => new SliceDef[2]
				{
					new SliceDef("compress", "Compress", "\ue73f"),
					new SliceDef("to:MP4", "To MP4", "\ue8ab")
				}, 
				_ => Array.Empty<SliceDef>(), 
			};
		}

		public static IReadOnlyList<SliceDef> ConvertTargets(FileKind kind)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Invalid comparison between Unknown and I4
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Invalid comparison between Unknown and I4
			if ((int)kind != 1)
			{
				if ((int)kind == 2)
				{
					return PdfConverter.Targets.Select((string t) => new SliceDef("to:" + t, t, "")).ToArray();
				}
				return Array.Empty<SliceDef>();
			}
			return ImageConverter.Targets.Select((string t) => new SliceDef("to:" + t, t, "")).ToArray();
		}
	}
	internal enum WheelMode
	{
		Probe,
		Drag,
		Click
	}
	internal sealed class WheelWindow : Window
	{
		private sealed record WheelPalette(Color Pith, Color Segment, Color Hover, Brush Text, Brush HoverText, Brush Dim, Brush Accent, double ShadowOpacity)
		{
			public static WheelPalette Light { get; } = new WheelPalette(Color.FromRgb(byte.MaxValue, (byte)250, (byte)244), Color.FromRgb(byte.MaxValue, (byte)230, (byte)203), Color.FromRgb(byte.MaxValue, (byte)154, (byte)60), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromRgb((byte)58, (byte)36, (byte)16))), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromRgb((byte)36, (byte)20, (byte)6))), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromArgb((byte)128, (byte)90, (byte)64, (byte)40))), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromRgb((byte)194, (byte)87, (byte)10))), 0.3);

			public static WheelPalette Dark { get; } = new WheelPalette(Color.FromRgb((byte)43, (byte)38, (byte)34), Color.FromRgb((byte)64, (byte)52, (byte)43), Color.FromRgb(byte.MaxValue, (byte)154, (byte)60), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromRgb((byte)245, (byte)238, (byte)232))), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromRgb((byte)31, (byte)18, (byte)8))), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromArgb((byte)128, (byte)245, (byte)238, (byte)232))), (Brush)(object)WheelWindow.Frozen<SolidColorBrush>(new SolidColorBrush(Color.FromRgb(byte.MaxValue, (byte)193, (byte)138))), 0.55);

			static WheelPalette()
			{
				//IL_000f: Unknown result type (might be due to invalid IL or missing references)
				//IL_0023: Unknown result type (might be due to invalid IL or missing references)
				//IL_0034: Unknown result type (might be due to invalid IL or missing references)
				//IL_003f: Unknown result type (might be due to invalid IL or missing references)
				//IL_0044: Unknown result type (might be due to invalid IL or missing references)
				//IL_004e: Expected Obj, but got Unknown
				//IL_0053: Unknown result type (might be due to invalid IL or missing references)
				//IL_0058: Unknown result type (might be due to invalid IL or missing references)
				//IL_0062: Expected Obj, but got Unknown
				//IL_006d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0072: Unknown result type (might be due to invalid IL or missing references)
				//IL_007c: Expected Obj, but got Unknown
				//IL_0085: Unknown result type (might be due to invalid IL or missing references)
				//IL_008a: Unknown result type (might be due to invalid IL or missing references)
				//IL_0094: Expected Obj, but got Unknown
				//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
				//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
				//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
				//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
				//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
				//IL_00ec: Expected Obj, but got Unknown
				//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
				//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
				//IL_0100: Expected Obj, but got Unknown
				//IL_0114: Unknown result type (might be due to invalid IL or missing references)
				//IL_0119: Unknown result type (might be due to invalid IL or missing references)
				//IL_0123: Expected Obj, but got Unknown
				//IL_0132: Unknown result type (might be due to invalid IL or missing references)
				//IL_0137: Unknown result type (might be due to invalid IL or missing references)
				//IL_0141: Expected Obj, but got Unknown
			}
		}

		private sealed class SliceVisual
		{
			public required SliceDef Def;

			public required SolidColorBrush Fill;

			public required TranslateTransform Shift;

			public required TextBlock Glyph;

			public required TextBlock Label;

			public required double MidAngle;
		}

		private const double Size = 384.0;

		private const double C = 192.0;

		private const double R = 160.0;

		private const double Inner = 60.0;

		private const double Pop = 6.0;

		private const double GapDeg = 2.4;

		private static readonly Brush Peel = (Brush)(object)Frozen<LinearGradientBrush>(new LinearGradientBrush(Color.FromRgb(byte.MaxValue, (byte)166, (byte)64), Color.FromRgb((byte)242, (byte)107, (byte)15), 45.0));

		private readonly WheelPalette _p = (ThemeManager.IsDark ? WheelPalette.Dark : WheelPalette.Light);

		private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

		private readonly Canvas _slicesLayer = new Canvas();

		private readonly Grid _root = new Grid();

		private readonly ScaleTransform _rootScale = new ScaleTransform(1.0, 1.0, 192.0, 192.0);

		private readonly TextBlock _centerBig = new TextBlock();

		private readonly TextBlock _centerSmall = new TextBlock();

		private readonly DispatcherTimer _dwell = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(60.0)
		};

		private readonly List<SliceVisual> _visuals = new List<SliceVisual>();

		private IReadOnlyList<SliceDef> _slices = Array.Empty<SliceDef>();

		private IReadOnlyList<string> _files = Array.Empty<string>();

		private FileKind _kind;

		private bool _isConvertMenu;

		private bool _revealed;

		private bool _dropped;

		private bool _closing;

		private int _hover = -2;

		private long _hoverSince;

		private int _anchorX;

		private int _anchorY;

		private int _centerPxX;

		private int _centerPxY;

		private Brush TextBrush => _p.Text;

		private Brush DimBrush => _p.Dim;

		private Brush AccentBrush => _p.Accent;

		public WheelMode Mode { get; private set; }

		public event Action<SliceDef, IReadOnlyList<string>>? ActionChosen;

		private static T Frozen<T>(T f) where T : Freezable
		{
			((Freezable)f).Freeze();
			return f;
		}

		public WheelWindow()
		{
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Expected Obj, but got Unknown
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Expected Obj, but got Unknown
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Expected Obj, but got Unknown
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0069: Expected Obj, but got Unknown
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Expected Obj, but got Unknown
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0093: Expected Obj, but got Unknown
			//IL_0127: Unknown result type (might be due to invalid IL or missing references)
			//IL_0131: Expected Obj, but got Unknown
			//IL_0158: Unknown result type (might be due to invalid IL or missing references)
			//IL_0162: Expected Obj, but got Unknown
			//IL_016a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0174: Expected Obj, but got Unknown
			//IL_017c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0186: Expected Obj, but got Unknown
			//IL_018e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0198: Expected Obj, but got Unknown
			//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_01aa: Expected Obj, but got Unknown
			//IL_01b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01bc: Expected Obj, but got Unknown
			//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ce: Expected Obj, but got Unknown
			//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e0: Expected Obj, but got Unknown
			//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f2: Expected Obj, but got Unknown
			((Window)this).WindowStyle = (WindowStyle)0;
			((Window)this).AllowsTransparency = true;
			((Control)this).Background = (Brush)(object)Brushes.Transparent;
			((Window)this).ResizeMode = (ResizeMode)0;
			((Window)this).ShowInTaskbar = false;
			((Window)this).Topmost = true;
			((Window)this).ShowActivated = false;
			double width = (((FrameworkElement)this).Height = 384.0);
			((FrameworkElement)this).Width = width;
			((UIElement)this).AllowDrop = true;
			((Window)this).Title = "Zest";
			((Control)this).FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
			((FrameworkElement)this).UseLayoutRounding = true;
			BuildChrome();
			((Window)this).SourceInitialized += (object? _, EventArgs _) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				WindowEffects.MakeNoActivateToolWindow(new WindowInteropHelper((Window)(object)this).Handle);
			};
			((Window)this).DpiChanged += (object _, DpiChangedEventArgs _) =>
			{
				PlaceAt(_anchorX, _anchorY);
			};
			((UIElement)this).DragEnter += OnDragEnter;
			((UIElement)this).DragOver += OnDragOver;
			((UIElement)this).DragLeave += (object _, DragEventArgs e) =>
			{
				SetHover(-2);
				((RoutedEventArgs)e).Handled = true;
			};
			((UIElement)this).Drop += OnDrop;
			((UIElement)this).MouseMove += (object _, MouseEventArgs e) =>
			{
				//IL_000d: Unknown result type (might be due to invalid IL or missing references)
				if (Mode == WheelMode.Click)
				{
					SetHover(HitTest(e.GetPosition((IInputElement)(object)this)));
				}
			};
			((UIElement)this).MouseLeftButtonUp += (object _, MouseButtonEventArgs e) =>
			{
				//IL_000d: Unknown result type (might be due to invalid IL or missing references)
				if (Mode == WheelMode.Click)
				{
					Choose(HitTest(((MouseEventArgs)e).GetPosition((IInputElement)(object)this)), afterDrop: false);
				}
			};
			((UIElement)this).MouseRightButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				CloseWheel();
			};
			((UIElement)this).KeyDown += (object _, KeyEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				if ((int)e.Key == 13)
				{
					CloseWheel();
				}
			};
			((Window)this).Deactivated += (object? _, EventArgs _) =>
			{
				if (Mode == WheelMode.Click && _revealed)
				{
					CloseWheel();
				}
			};
			_dwell.Tick += (object? _, EventArgs _) =>
			{
				CheckDwell();
			};
		}

		public void ShowProbe(int x, int y)
		{
			Mode = WheelMode.Probe;
			((UIElement)this).Opacity = 0.02;
			BuildSlices(WheelMenus.MainMenu((FileKind)1));
			_centerBig.Text = "";
			_centerSmall.Text = "";
			((Window)this).Show();
			PlaceAt(x, y);
		}

		public void ShowForFiles(IReadOnlyList<string> files, int x, int y)
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			_files = files;
			_kind = FileKinds.DetectGroup(files);
			Mode = WheelMode.Click;
			ShowMain();
			((UIElement)this).Opacity = 0.0;
			((Window)this).Show();
			PlaceAt(x, y);
			Reveal();
			WindowEffects.ClearNoActivate(new WindowInteropHelper((Window)(object)this).Handle);
			((Window)this).Activate();
			((UIElement)this).Focus();
		}

		public void OnMouseReleased()
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Expected Obj, but got Unknown
			DispatcherTimer t = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(350.0)
			};
			t.Tick += (object? _, EventArgs _) =>
			{
				t.Stop();
				if (!_dropped && Mode != WheelMode.Click)
				{
					CloseWheel();
				}
			};
			t.Start();
		}

		public void CloseWheel()
		{
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Expected Obj, but got Unknown
			if (_closing)
			{
				return;
			}
			_closing = true;
			_dwell.Stop();
			if (!_revealed)
			{
				((Window)this).Close();
				return;
			}
			DoubleAnimation val = new DoubleAnimation(((UIElement)this).Opacity, 0.0, Duration.op_Implicit(TimeSpan.FromMilliseconds(110.0)));
			((Timeline)val).Completed += (object? _, EventArgs _) =>
			{
				((Window)this).Close();
			};
			((UIElement)this).BeginAnimation(UIElement.OpacityProperty, (AnimationTimeline)(object)val);
		}

		private void OnDragEnter(object sender, DragEventArgs e)
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			((RoutedEventArgs)e).Handled = true;
			if (!_revealed)
			{
				IReadOnlyList<string> files = GetFiles(e.Data);
				FileKind val = FileKinds.DetectGroup(files);
				if ((int)val == 0)
				{
					e.Effects = (DragDropEffects)0;
					return;
				}
				_files = files;
				_kind = val;
				Mode = WheelMode.Drag;
				ShowMain();
				Reveal();
			}
			UpdateDragHover(e);
		}

		private void OnDragOver(object sender, DragEventArgs e)
		{
			((RoutedEventArgs)e).Handled = true;
			if (!_revealed)
			{
				e.Effects = (DragDropEffects)0;
			}
			else
			{
				UpdateDragHover(e);
			}
		}

		private void UpdateDragHover(DragEventArgs e)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			int num = HitTest(e.GetPosition((IInputElement)(object)this));
			SetHover(num);
			e.Effects = (DragDropEffects)((num >= 0 || (num == -1 && _isConvertMenu)) ? 1 : 0);
		}

		private void OnDrop(object sender, DragEventArgs e)
		{
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			((RoutedEventArgs)e).Handled = true;
			if (_revealed)
			{
				_dropped = true;
				IReadOnlyList<string> files = GetFiles(e.Data);
				if (files.Count > 0)
				{
					_files = files;
				}
				Choose(HitTest(e.GetPosition((IInputElement)(object)this)), afterDrop: true);
			}
		}

		private static IReadOnlyList<string> GetFiles(IDataObject data)
		{
			try
			{
				if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] source)
				{
					return source.Where(File.Exists).ToArray();
				}
			}
			catch
			{
			}
			return Array.Empty<string>();
		}

		private void Choose(int index, bool afterDrop)
		{
			if (index == -1)
			{
				if (_isConvertMenu)
				{
					BackToMain(afterDrop);
				}
				else
				{
					CloseWheel();
				}
				return;
			}
			if (index < 0 || index >= _slices.Count)
			{
				CloseWheel();
				return;
			}
			SliceDef sliceDef = _slices[index];
			if (sliceDef.Id == "convert" && !_isConvertMenu)
			{
				OpenConvertMenu();
				if (afterDrop)
				{
					SwitchToClickMode();
				}
			}
			else
			{
				ActionChosen?.Invoke(sliceDef, _files);
				CloseWheel();
			}
		}

		private void SwitchToClickMode()
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			Mode = WheelMode.Click;
			WindowEffects.ClearNoActivate(new WindowInteropHelper((Window)(object)this).Handle);
			((Window)this).Activate();
			((UIElement)this).Focus();
		}

		private void BackToMain(bool afterDrop)
		{
			ShowMain();
			Pulse();
			if (afterDrop)
			{
				SwitchToClickMode();
			}
		}

		private void CheckDwell()
		{
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			if (Mode == WheelMode.Click)
			{
				if ((NativeMethods.GetAsyncKeyState(1) & 0x8000) != 0 && NativeMethods.GetCursorPos(out var p))
				{
					DpiScale dpi = VisualTreeHelper.GetDpi((Visual)(object)this);
					double dpiScaleX = dpi.DpiScaleX;
					double num = p.X - _centerPxX;
					double num2 = p.Y - _centerPxY;
					if (Math.Sqrt(num * num + num2 * num2) > 166.0 * dpiScaleX)
					{
						CloseWheel();
					}
				}
			}
			else if (Mode == WheelMode.Drag)
			{
				long num3 = Environment.TickCount64 - _hoverSince;
				if (!_isConvertMenu && _hover >= 0 && _slices[_hover].Id == "convert" && num3 >= 450)
				{
					OpenConvertMenu();
				}
				else if (_isConvertMenu && _hover == -1 && num3 >= 650)
				{
					ShowMain();
					Pulse();
				}
			}
		}

		private void ShowMain()
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			_isConvertMenu = false;
			BuildSlices(WheelMenus.MainMenu(_kind));
			ShowCenterDefault();
		}

		private void OpenConvertMenu()
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			_isConvertMenu = true;
			BuildSlices(WheelMenus.ConvertTargets(_kind));
			ShowCenterDefault();
			Pulse();
		}

		private int HitTest(Point p)
		{
			return SliceGeometry.HitTest(p.X - 192.0, p.Y - 192.0, _slices.Count, 60.0, 166.0);
		}

		private void SetHover(int index)
		{
			if (index != _hover)
			{
				if (_hover >= 0 && _hover < _visuals.Count)
				{
					Animate(_visuals[_hover], on: false);
				}
				_hover = index;
				_hoverSince = Environment.TickCount64;
				if (_hover >= 0 && _hover < _visuals.Count)
				{
					SliceVisual sliceVisual = _visuals[_hover];
					Animate(sliceVisual, on: true);
					_centerBig.Text = sliceVisual.Def.Label;
					? val = _centerSmall;
					string text;
					if (sliceVisual.Def.Enabled)
					{
						text = (_isConvertMenu ? "Convert to" : "");
					}
					else
					{
						text = $"Coming in Phase {sliceVisual.Def.Phase}";
					}
					((TextBlock)val).Text = text;
				}
				else if (_hover == -1 && _isConvertMenu)
				{
					_centerBig.Text = "Back";
					_centerSmall.Text = ((Mode == WheelMode.Drag) ? "hold here" : "click here");
				}
				else
				{
					ShowCenterDefault();
				}
			}
		}

		private void Animate(SliceVisual v, bool on)
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected Obj, but got Unknown
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_007c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0088: Expected Obj, but got Unknown
			//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ed: Expected Obj, but got Unknown
			//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0100: Unknown result type (might be due to invalid IL or missing references)
			//IL_0105: Unknown result type (might be due to invalid IL or missing references)
			//IL_0111: Expected Obj, but got Unknown
			//IL_0187: Unknown result type (might be due to invalid IL or missing references)
			//IL_0180: Unknown result type (might be due to invalid IL or missing references)
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(140.0);
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = (EasingMode)1
			};
			Color val;
			if (on && v.Def.Enabled)
			{
				val = _p.Hover;
			}
			else
			{
				val = (on ? Blend(_p.Segment, _p.Pith) : _p.Segment);
			}
			((Animatable)v.Fill).BeginAnimation(SolidColorBrush.ColorProperty, (AnimationTimeline)new ColorAnimation(val, Duration.op_Implicit(timeSpan))
			{
				EasingFunction = (IEasingFunction)(object)easingFunction
			});
			var (num, num2) = SliceGeometry.PointAt(0.0, 0.0, on ? 6.0 : 0.0, v.MidAngle);
			((Animatable)v.Shift).BeginAnimation(TranslateTransform.XProperty, (AnimationTimeline)new DoubleAnimation(num, Duration.op_Implicit(timeSpan))
			{
				EasingFunction = (IEasingFunction)(object)easingFunction
			});
			((Animatable)v.Shift).BeginAnimation(TranslateTransform.YProperty, (AnimationTimeline)new DoubleAnimation(num2, Duration.op_Implicit(timeSpan))
			{
				EasingFunction = (IEasingFunction)(object)easingFunction
			});
			Brush foreground;
			if (on && v.Def.Enabled)
			{
				foreground = _p.HoverText;
			}
			else
			{
				foreground = (v.Def.Enabled ? TextBrush : DimBrush);
			}
			v.Glyph.Foreground = foreground;
			v.Label.Foreground = foreground;
			v.Label.FontWeight = ((on || string.IsNullOrEmpty(v.Def.Glyph)) ? FontWeights.SemiBold : FontWeights.Normal);
		}

		private static Color Blend(Color a, Color b)
		{
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			return Color.FromRgb((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));
		}

		private void ShowCenterDefault()
		{
			long num = 0L;
			foreach (string file in _files)
			{
				try
				{
					num += new FileInfo(file).Length;
				}
				catch
				{
				}
			}
			_centerBig.Text = ((_files.Count == 0) ? "" : SizeFormat.Format(num));
			? val = _centerSmall;
			string text;
			if (_isConvertMenu)
			{
				text = "Convert to…";
			}
			else
			{
				text = ((_files.Count > 1) ? $"{_files.Count} files" : "");
			}
			((TextBlock)val).Text = text;
		}

		private void BuildChrome()
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_008a: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Expected Obj, but got Unknown
			//IL_009b: Expected Obj, but got Unknown
			//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Expected Obj, but got Unknown
			//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ff: Expected Obj, but got Unknown
			//IL_011d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0122: Unknown result type (might be due to invalid IL or missing references)
			//IL_0131: Unknown result type (might be due to invalid IL or missing references)
			//IL_0140: Unknown result type (might be due to invalid IL or missing references)
			//IL_0147: Unknown result type (might be due to invalid IL or missing references)
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0156: Expected Obj, but got Unknown
			//IL_015c: Expected Obj, but got Unknown
			//IL_0194: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f7: Expected Obj, but got Unknown
			//IL_0249: Unknown result type (might be due to invalid IL or missing references)
			//IL_024e: Unknown result type (might be due to invalid IL or missing references)
			//IL_025d: Unknown result type (might be due to invalid IL or missing references)
			//IL_026c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0275: Expected Obj, but got Unknown
			//IL_02a3: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_02c8: Expected Obj, but got Unknown
			((UIElement)_root).RenderTransform = (Transform)(object)_rootScale;
			Ellipse val = new Ellipse
			{
				Width = 332.0,
				Height = 332.0,
				Fill = Peel,
				IsHitTestVisible = false,
				Effect = (Effect)new DropShadowEffect
				{
					BlurRadius = 24.0,
					ShadowDepth = 4.0,
					Direction = 270.0,
					Opacity = _p.ShadowOpacity,
					Color = Colors.Black
				}
			};
			Canvas.SetLeft((UIElement)(object)val, 26.0);
			Canvas.SetTop((UIElement)(object)val, 26.0);
			Ellipse val2 = new Ellipse
			{
				Width = 322.0,
				Height = 322.0,
				Fill = (Brush)(object)Frozen<SolidColorBrush>(new SolidColorBrush(_p.Pith)),
				IsHitTestVisible = false
			};
			Canvas.SetLeft((UIElement)(object)val2, 31.0);
			Canvas.SetTop((UIElement)(object)val2, 31.0);
			Ellipse val3 = new Ellipse
			{
				Width = 120.0,
				Height = 120.0,
				Fill = (Brush)(object)Frozen<SolidColorBrush>(new SolidColorBrush(_p.Pith))
			};
			Canvas.SetLeft((UIElement)(object)val3, 132.0);
			Canvas.SetTop((UIElement)(object)val3, 132.0);
			_centerBig.FontSize = 18.0;
			_centerBig.FontWeight = FontWeights.Bold;
			_centerBig.Foreground = TextBrush;
			_centerSmall.FontSize = 11.0;
			_centerSmall.Foreground = AccentBrush;
			StackPanel val4 = new StackPanel
			{
				Width = 108.0,
				VerticalAlignment = (VerticalAlignment)1,
				IsHitTestVisible = false
			};
			TextBlock[] array = new TextBlock[2] { _centerBig, _centerSmall };
			foreach (TextBlock val5 in array)
			{
				val5.TextAlignment = (TextAlignment)2;
				val5.TextTrimming = (TextTrimming)1;
				((Panel)val4).Children.Add((UIElement)(object)val5);
			}
			Grid val6 = new Grid
			{
				Width = 120.0,
				Height = 120.0,
				IsHitTestVisible = false
			};
			((Panel)val6).Children.Add((UIElement)(object)val4);
			Canvas.SetLeft((UIElement)(object)val6, 132.0);
			Canvas.SetTop((UIElement)(object)val6, 132.0);
			Canvas val7 = new Canvas
			{
				Width = 384.0,
				Height = 384.0
			};
			((Panel)val7).Children.Add((UIElement)(object)val);
			((Panel)val7).Children.Add((UIElement)(object)val2);
			((Panel)val7).Children.Add((UIElement)(object)_slicesLayer);
			((Panel)val7).Children.Add((UIElement)(object)val3);
			((Panel)val7).Children.Add((UIElement)(object)val6);
			((Panel)_root).Children.Add((UIElement)(object)val7);
			((ContentControl)this).Content = _root;
		}

		private void BuildSlices(IReadOnlyList<SliceDef> slices)
		{
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_0080: Expected Obj, but got Unknown
			//IL_0080: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Expected Obj, but got Unknown
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_008c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Expected Obj, but got Unknown
			//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0112: Unknown result type (might be due to invalid IL or missing references)
			//IL_0119: Unknown result type (might be due to invalid IL or missing references)
			//IL_0129: Expected Obj, but got Unknown
			//IL_0129: Unknown result type (might be due to invalid IL or missing references)
			//IL_012e: Unknown result type (might be due to invalid IL or missing references)
			//IL_013a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0158: Unknown result type (might be due to invalid IL or missing references)
			//IL_0164: Unknown result type (might be due to invalid IL or missing references)
			//IL_015d: Unknown result type (might be due to invalid IL or missing references)
			//IL_016e: Unknown result type (might be due to invalid IL or missing references)
			//IL_018a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0191: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c2: Expected Obj, but got Unknown
			//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d8: Expected Obj, but got Unknown
			//IL_020a: Unknown result type (might be due to invalid IL or missing references)
			//IL_024b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0250: Unknown result type (might be due to invalid IL or missing references)
			//IL_026f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0274: Unknown result type (might be due to invalid IL or missing references)
			_slices = slices;
			_visuals.Clear();
			((Panel)_slicesLayer).Children.Clear();
			_hover = -2;
			int count = slices.Count;
			for (int i = 0; i < count; i++)
			{
				SliceDef sliceDef = slices[i];
				double a = SliceGeometry.StartAngle(i, count) + 1.2;
				double a2 = SliceGeometry.EndAngle(i, count) - 1.2;
				double num = SliceGeometry.MidAngle(i, count);
				SolidColorBrush fill = new SolidColorBrush(_p.Segment);
				TranslateTransform val = new TranslateTransform();
				Path val2 = new Path
				{
					Data = Sector(a, a2, 63.0, 157.0),
					Fill = (Brush)(object)fill,
					RenderTransform = (Transform)(object)val
				};
				bool flag = string.IsNullOrEmpty(sliceDef.Glyph);
				TextBlock val3 = new TextBlock
				{
					Text = sliceDef.Glyph,
					FontFamily = IconFont,
					FontSize = 22.0,
					Foreground = (sliceDef.Enabled ? TextBrush : DimBrush),
					HorizontalAlignment = (HorizontalAlignment)1,
					Visibility = (Visibility)(flag ? 2 : 0)
				};
				TextBlock val4 = new TextBlock
				{
					Text = sliceDef.Label,
					FontSize = (flag ? 15.0 : 10.5),
					FontWeight = (flag ? FontWeights.SemiBold : FontWeights.Normal),
					Foreground = (sliceDef.Enabled ? TextBrush : DimBrush),
					HorizontalAlignment = (HorizontalAlignment)1,
					Margin = new Thickness(0.0, (double)((!flag) ? 2 : 0), 0.0, 0.0)
				};
				StackPanel val5 = new StackPanel
				{
					IsHitTestVisible = false,
					RenderTransform = (Transform)(object)val
				};
				((Panel)val5).Children.Add((UIElement)(object)val3);
				((Panel)val5).Children.Add((UIElement)(object)val4);
				((UIElement)val5).Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				(double, double) tuple = SliceGeometry.PointAt(192.0, 192.0, 114.0, num);
				double item = tuple.Item1;
				double item2 = tuple.Item2;
				Size desiredSize = ((UIElement)val5).DesiredSize;
				Canvas.SetLeft((UIElement)(object)val5, item - desiredSize.Width / 2.0);
				desiredSize = ((UIElement)val5).DesiredSize;
				Canvas.SetTop((UIElement)(object)val5, item2 - desiredSize.Height / 2.0);
				((Panel)_slicesLayer).Children.Add((UIElement)(object)val2);
				((Panel)_slicesLayer).Children.Add((UIElement)(object)val5);
				_visuals.Add(new SliceVisual
				{
					Def = sliceDef,
					Fill = fill,
					Shift = val,
					Glyph = val3,
					Label = val4,
					MidAngle = num
				});
			}
		}

		private static Geometry Sector(double a0, double a1, double rIn, double rOut)
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Expected Obj, but got Unknown
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Expected Obj, but got Unknown
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0061: Unknown result type (might be due to invalid IL or missing references)
			//IL_006b: Expected Obj, but got Unknown
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0095: Expected Obj, but got Unknown
			//IL_009f: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ab: Expected Obj, but got Unknown
			bool flag = a1 - a0 > 180.0;
			PathFigure val = new PathFigure
			{
				StartPoint = P(rOut, a0),
				IsClosed = true
			};
			val.Segments.Add((PathSegment)new ArcSegment(P(rOut, a1), new Size(rOut, rOut), 0.0, flag, (SweepDirection)1, true));
			val.Segments.Add((PathSegment)new LineSegment(P(rIn, a1), true));
			val.Segments.Add((PathSegment)new ArcSegment(P(rIn, a0), new Size(rIn, rIn), 0.0, flag, (SweepDirection)0, true));
			PathGeometry val2 = new PathGeometry((IEnumerable<PathFigure>)new PathFigure[1] { val });
			((Freezable)val2).Freeze();
			return (Geometry)val2;
			static Point P(double r, double num)
			{
				//IL_0028: Unknown result type (might be due to invalid IL or missing references)
				var (num2, num3) = SliceGeometry.PointAt(192.0, 192.0, r, num);
				return new Point(num2, num3);
			}
		}

		private void Reveal()
		{
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Expected Obj, but got Unknown
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Expected Obj, but got Unknown
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Expected Obj, but got Unknown
			//IL_00db: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f1: Expected Obj, but got Unknown
			if (!_revealed)
			{
				_revealed = true;
				TimeSpan timeSpan = TimeSpan.FromMilliseconds(180.0);
				BackEase easingFunction = new BackEase
				{
					Amplitude = 0.3,
					EasingMode = (EasingMode)1
				};
				((UIElement)this).BeginAnimation(UIElement.OpacityProperty, (AnimationTimeline)new DoubleAnimation((((UIElement)this).Opacity < 0.5) ? 0.0 : ((UIElement)this).Opacity, 1.0, Duration.op_Implicit(TimeSpan.FromMilliseconds(120.0))));
				((Animatable)_rootScale).BeginAnimation(ScaleTransform.ScaleXProperty, (AnimationTimeline)new DoubleAnimation(0.84, 1.0, Duration.op_Implicit(timeSpan))
				{
					EasingFunction = (IEasingFunction)(object)easingFunction
				});
				((Animatable)_rootScale).BeginAnimation(ScaleTransform.ScaleYProperty, (AnimationTimeline)new DoubleAnimation(0.84, 1.0, Duration.op_Implicit(timeSpan))
				{
					EasingFunction = (IEasingFunction)(object)easingFunction
				});
				_dwell.Start();
			}
		}

		private void Pulse()
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected Obj, but got Unknown
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_0050: Expected Obj, but got Unknown
			//IL_006e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Expected Obj, but got Unknown
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(160.0);
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = (EasingMode)1
			};
			((Animatable)_rootScale).BeginAnimation(ScaleTransform.ScaleXProperty, (AnimationTimeline)new DoubleAnimation(0.92, 1.0, Duration.op_Implicit(timeSpan))
			{
				EasingFunction = (IEasingFunction)(object)easingFunction
			});
			((Animatable)_rootScale).BeginAnimation(ScaleTransform.ScaleYProperty, (AnimationTimeline)new DoubleAnimation(0.92, 1.0, Duration.op_Implicit(timeSpan))
			{
				EasingFunction = (IEasingFunction)(object)easingFunction
			});
		}

		private void PlaceAt(int x, int y)
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			_anchorX = x;
			_anchorY = y;
			nint handle = new WindowInteropHelper((Window)(object)this).Handle;
			if (handle != IntPtr.Zero)
			{
				DpiScale dpi = VisualTreeHelper.GetDpi((Visual)(object)this);
				double dpiScaleX = dpi.DpiScaleX;
				int num = (int)Math.Round(384.0 * dpiScaleX);
				int num2 = x - num / 2;
				int num3 = y - num / 2;
				nint hMonitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(x, y), 2u);
				NativeMethods.MONITORINFO info = new NativeMethods.MONITORINFO
				{
					cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
				};
				if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
				{
					NativeMethods.RECT rcWork = info.rcWork;
					num2 = Math.Clamp(num2, rcWork.Left, Math.Max(rcWork.Left, rcWork.Right - num));
					num3 = Math.Clamp(num3, rcWork.Top, Math.Max(rcWork.Top, rcWork.Bottom - num));
				}
				NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, num2, num3, 0, 0, 17u);
				_centerPxX = num2 + num / 2;
				_centerPxY = num3 + num / 2;
			}
		}

		static WheelWindow()
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Expected Obj, but got Unknown
			//IL_003c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Expected Obj, but got Unknown
		}
	}
}
namespace Zest.App.Services
{
	internal static class ActionRunner
	{
		public static void Run(SliceDef slice, IReadOnlyList<string> files)
		{
			if (!slice.Enabled)
			{
				ToastWindow.Show(slice.Label, $"This action is coming in Phase {slice.Phase}.");
				return;
			}
			string command;
			if (!slice.Id.StartsWith("to:"))
			{
				command = slice.Id;
			}
			else
			{
				string id = slice.Id;
				command = "convert:" + id.Substring(3, id.Length - 3);
			}
			Execute(command, files);
		}

		public static void Execute(string command, IReadOnlyList<string> files)
		{
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Invalid comparison between Unknown and I4
			//IL_006f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Invalid comparison between Unknown and I4
			//IL_007b: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Invalid comparison between Unknown and I4
			//IL_0238: Unknown result type (might be due to invalid IL or missing references)
			//IL_023a: Invalid comparison between Unknown and I4
			//IL_039f: Unknown result type (might be due to invalid IL or missing references)
			//IL_03a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_03a3: Invalid comparison between Unknown and I4
			//IL_033d: Unknown result type (might be due to invalid IL or missing references)
			//IL_033f: Invalid comparison between Unknown and I4
			//IL_01f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fb: Invalid comparison between Unknown and I4
			files = files.Where(File.Exists).ToArray();
			if (files.Count == 0)
			{
				return;
			}
			FileKind val = FileKinds.DetectGroup(files);
			if (command.StartsWith("convert:"))
			{
				string text = command;
				int length = "convert:".Length;
				string target = text.Substring(length, text.Length - length);
				if ((int)val == 2)
				{
					ConvertPdfAsync(target, files);
				}
				else if ((int)val == 3)
				{
					ConvertVideosAsync(files);
				}
				else if ((int)val == 1)
				{
					ConvertImagesAsync(target, files);
				}
				else
				{
					ToastWindow.Show("Convert", "Select only one kind: images, PDFs, or videos.");
				}
				return;
			}
			switch (command)
			{
			case "compress":
				Show((Window)(object)new CompressDialog(files));
				break;
			case "metadata":
				if (val - 1 > 1)
				{
					ToastWindow.Show("Metadata", "Metadata works with images and PDF files.");
					break;
				}
				OnlyFirst("Metadata", files);
				Show((Window)(object)new MetadataDialog(files[0]));
				break;
			case "annotate":
			case "crop":
			case "edit":
			case "addbg":
			case "redact":
				if ((int)val != 1)
				{
					ToastWindow.Show("Images only", "This action works on images.");
					break;
				}
				OnlyFirst(command switch
				{
					"crop" => "Crop", 
					"edit" => "Edit", 
					"addbg" => "Add background", 
					"redact" => "Redact", 
					_ => "Annotate", 
				}, files);
				Show(command switch
				{
					"crop" => (Window)(object)new CropDialog(files[0]), 
					"edit" => (Window)(object)new EditDialog(files[0]), 
					"addbg" => (Window)(object)new AddBgDialog(files[0]), 
					"redact" => (Window)(object)new RedactDialog(files[0]), 
					_ => (Window)(object)new AnnotateDialog(files[0]), 
				});
				break;
			case "organize":
			case "split":
				if ((int)val != 2)
				{
					ToastWindow.Show("PDF only", "This action works on PDF files.");
					break;
				}
				OnlyFirst((command == "split") ? "Split" : "Organize", files);
				Show((Window)((command == "split") ? ((object)new SplitDialog(files[0])) : ((object)new OrganizeDialog(files[0]))));
				break;
			case "readqr":
				if (val - 1 > 1)
				{
					ToastWindow.Show("Read QR", "Zest reads QR codes in images and PDF files.");
				}
				else
				{
					Show((Window)(object)new QrDialog(files));
				}
				break;
			}
		}

		private static void OnlyFirst(string action, IReadOnlyList<string> files)
		{
			if (files.Count > 1)
			{
				ToastWindow.Show(action, action + " works on one file at a time. Opening " + Path.GetFileName(files[0]) + ".");
			}
		}

		private static void Show(Window w)
		{
			DialogChrome.ShowInFront(w);
		}

		private static async Task ConvertImagesAsync(string target, IReadOnlyList<string> files)
		{
			ToastWindow.Show("Converting…", (files.Count == 1) ? Path.GetFileName(files[0]) : $"{files.Count} files to {target}");
			List<string> outputs = new List<string>();
			List<string> errors = new List<string>();
			foreach (string f in files)
			{
				try
				{
					List<string> list = outputs;
					list.Add(await ImageConverter.ConvertAsync(f, target, default(CancellationToken)));
				}
				catch (Exception ex)
				{
					Log.Error("Convert to " + target + " failed: " + f, ex);
					errors.Add(Path.GetFileName(f) + ": " + ex.Message);
				}
			}
			Finish(target, files.Count, outputs, errors, "");
		}

		private static async Task ConvertPdfAsync(string target, IReadOnlyList<string> files)
		{
			ToastWindow.Show("Converting…", (files.Count == 1) ? Path.GetFileName(files[0]) : $"{files.Count} PDFs to {target}");
			List<string> outputs = new List<string>();
			List<string> errors = new List<string>();
			List<string> notes = new List<string>();
			foreach (string f in files)
			{
				try
				{
					PdfConvertResult val = await PdfConverter.ConvertAsync(f, target, default(CancellationToken));
					outputs.AddRange(val.Outputs);
					if (val.Note.Length > 0)
					{
						notes.Add(val.Note);
					}
				}
				catch (Exception ex)
				{
					Log.Error("PDF convert to " + target + " failed: " + f, ex);
					errors.Add(Path.GetFileName(f) + ": " + ex.Message);
				}
			}
			Finish(target, files.Count, outputs, errors, string.Join("\n", notes.Distinct()));
		}

		private static async Task ConvertVideosAsync(IReadOnlyList<string> files)
		{
			List<string> outputs = new List<string>();
			List<string> errors = new List<string>();
			for (int i = 0; i < files.Count; i++)
			{
				string label = ((files.Count == 1) ? Path.GetFileName(files[i]) : $"{Path.GetFileName(files[i])} ({i + 1} of {files.Count})");
				ToastWindow.Show("Converting video to MP4…", label);
				try
				{
					int lastShown = 0;
					Progress<double> progress = new Progress<double>((double p) =>
					{
						int num = (int)(p * 100.0);
						if (num >= lastShown + 10)
						{
							lastShown = num;
							ToastWindow.Show($"Converting video… {num}%", label);
						}
					});
					List<string> list = outputs;
					list.Add(await VideoCompressor.ConvertToMp4Async(files[i], (IProgress<double>)progress, default(CancellationToken)));
				}
				catch (Exception ex)
				{
					Log.Error("Video convert failed: " + files[i], ex);
					errors.Add(Path.GetFileName(files[i]) + ": " + ex.Message);
				}
			}
			Finish("MP4", files.Count, outputs, errors, "");
		}

		private static void Finish(string target, int count, List<string> outputs, List<string> errors, string note)
		{
			if (outputs.Count > 0)
			{
				string text = ((count == 1) ? Path.GetFileName(outputs[0]) : $"{count} files converted to {target}");
				if (note.Length > 0)
				{
					text = text + "\n" + note;
				}
				if (errors.Count > 0)
				{
					text = text + "\n" + string.Join("\n", errors);
				}
				ToastWindow.Show("Converted", text, outputs, errors.Count > 0);
				ShellReveal.Select(outputs);
			}
			else
			{
				ToastWindow.Show("Convert failed", string.Join("\n", errors), null, isError: true);
			}
		}
	}
	internal static class Log
	{
		private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zest");

		public static string FilePath => Path.Combine(Dir, "log.txt");

		public static void Error(string what, Exception? ex = null)
		{
			try
			{
				Directory.CreateDirectory(Dir);
				File.AppendAllText(FilePath, $"{DateTime.Now:u}  {what}{((ex == null) ? "" : ("\n" + ex))}\n");
			}
			catch
			{
			}
		}
	}
	internal sealed class Settings
	{
		private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zest", "settings.json");

		public bool DragDetection { get; set; } = true;

		public bool DragFromAnyApp { get; set; }

		public int HoldMilliseconds { get; set; } = 500;

		public bool ContextMenu { get; set; } = true;

		public static Settings Load()
		{
			try
			{
				if (File.Exists(FilePath))
				{
					return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
				}
			}
			catch (Exception ex)
			{
				Log.Error("Could not read settings", ex);
			}
			return new Settings();
		}

		public void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
			catch (Exception ex)
			{
				Log.Error("Could not save settings", ex);
			}
		}
	}
	internal static class Startup
	{
		private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

		private const string Name = "Zest";

		public static bool IsEnabled
		{
			get
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
				return registryKey?.GetValue("Zest") is string;
			}
		}

		public static void Set(bool enabled)
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (enabled)
			{
				registryKey.SetValue("Zest", "\"" + Environment.ProcessPath + "\"");
			}
			else
			{
				registryKey.DeleteValue("Zest", throwOnMissingValue: false);
			}
		}
	}
	internal static class ShellReveal
	{
		public static void Select(IEnumerable<string> files)
		{
			foreach (IGrouping<string, string> item in from f in files
				where File.Exists(f) || Directory.Exists(f)
				group f by Path.GetDirectoryName(f.TrimEnd('\\')))
			{
				nint num = NativeMethods.ILCreateFromPathW(item.Key);
				nint[] array = (from p in item.Select(NativeMethods.ILCreateFromPathW)
					where p != IntPtr.Zero
					select p).ToArray();
				try
				{
					if (num != IntPtr.Zero && array.Length != 0)
					{
						NativeMethods.SHOpenFolderAndSelectItems(num, (uint)array.Length, array, 0u);
					}
				}
				catch (Exception ex)
				{
					Log.Error("Reveal failed", ex);
				}
				finally
				{
					NativeMethods.ILFree(num);
					nint[] array2 = array;
					for (int num2 = 0; num2 < array2.Length; num2++)
					{
						NativeMethods.ILFree(array2[num2]);
					}
				}
			}
		}
	}
	internal static class ExplorerSelection
	{
		public static List<string> GetSelectedFiles(nint foreground, out string? problem)
		{
			problem = null;
			List<string> list = new List<string>();
			string text = NativeMethods.ClassName(foreground);
			if ((text == "Progman" || text == "WorkerW") ? true : false)
			{
				problem = "Files on the desktop are not supported yet. Select files in a File Explorer window.";
				return list;
			}
			if (text != "CabinetWClass")
			{
				problem = "Select one or more files in File Explorer first, then press Ctrl+Alt+Z.";
				return list;
			}
			string title = NativeMethods.WindowText(foreground);
			dynamic val = null;
			try
			{
				val = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
				dynamic val2 = val.Windows();
				int num = val2.Count;
				List<(string, List<string>)> list2 = new List<(string, List<string>)>();
				for (int i = 0; i < num; i++)
				{
					try
					{
						dynamic val3 = val2.Item(i);
						if ((object)val3 == null || new IntPtr((long)val3.HWND) != foreground)
						{
							continue;
						}
						List<string> list3 = new List<string>();
						dynamic val4 = val3.Document.SelectedItems();
						int num2 = val4.Count;
						for (int j = 0; j < num2; j++)
						{
							string text2 = val4.Item(j).Path;
							if (File.Exists(text2))
							{
								list3.Add(text2);
							}
						}
						list2.Add(((string)val3.LocationName, list3));
					}
					catch
					{
					}
				}
				list = list2.FirstOrDefault(((string Location, List<string> Files) c) => title.StartsWith(c.Location, StringComparison.OrdinalIgnoreCase) && c.Files.Count > 0).Item2 ?? list2.FirstOrDefault(((string Location, List<string> Files) c) => c.Files.Count > 0).Item2 ?? new List<string>();
			}
			catch (Exception ex)
			{
				Log.Error("Explorer selection failed", ex);
			}
			finally
			{
				if (val != null)
				{
					Marshal.FinalReleaseComObject(val);
				}
			}
			if (list.Count == 0)
			{
				problem = "No files are selected in this File Explorer window.";
			}
			return list;
		}
	}
	internal static class DragSource
	{
		[DllImport("user32.dll")]
		private static extern nint WindowFromPoint(NativeMethods.POINT p);

		[DllImport("user32.dll")]
		private static extern nint GetAncestor(nint hwnd, uint flags);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

		public static bool IsFileDragStart(int x, int y, bool anyApp)
		{
			if (anyApp)
			{
				return true;
			}
			try
			{
				nint num = WindowFromPoint(new NativeMethods.POINT(x, y));
				if (num == IntPtr.Zero)
				{
					return false;
				}
				GetWindowThreadProcessId(GetAncestor(num, 2u), out var pid);
				string processName;
				using (Process process = Process.GetProcessById((int)pid))
				{
					processName = process.ProcessName;
				}
				string text = NativeMethods.ClassName(num);
				if (!DragSourceRules.IsFileDragStart(processName, text, (IEnumerable<string>)new string[1] { "ListItem" }, false))
				{
					return false;
				}
				return DragSourceRules.IsFileDragStart(processName, text, (IEnumerable<string>)ControlTypesAt(x, y), false);
			}
			catch (Exception ex)
			{
				Log.Error("Drag source check failed", ex);
				return false;
			}
		}

		private static List<string> ControlTypesAt(int x, int y)
		{
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			List<string> list = new List<string>();
			AutomationElement val = AutomationElement.FromPoint(new Point((double)x, (double)y));
			TreeWalker controlViewWalker = TreeWalker.ControlViewWalker;
			int num = 0;
			while (val != (AutomationElement)null && num < 4)
			{
				AutomationElementInformation current = val.Current;
				list.Add(((AutomationIdentifier)current.ControlType).ProgrammaticName.Replace("ControlType.", ""));
				val = controlViewWalker.GetParent(val);
				num++;
			}
			return list;
		}
	}
	internal sealed class DragWatcher : IDisposable
	{
		private const int StartDistance = 10;

		private const int StillDistance = 6;

		private readonly MouseHook _hook = new MouseHook();

		private readonly DispatcherTimer _timer;

		private bool _down;

		private bool _dragging;

		private bool _fired;

		private int _downX;

		private int _downY;

		private int _stillX;

		private int _stillY;

		private long _stillSince;

		private Task<bool>? _isFileSource;

		public int HoldMilliseconds { get; set; } = 500;

		public bool Enabled => _hook.IsInstalled;

		public bool AnyApp { get; set; }

		public event Action<int, int>? HoldDetected;

		public event Action? Released;

		public DragWatcher()
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Expected Obj, but got Unknown
			_timer = new DispatcherTimer((DispatcherPriority)5)
			{
				Interval = TimeSpan.FromMilliseconds(40.0)
			};
			_timer.Tick += (object? _, EventArgs _) =>
			{
				Tick();
			};
			_hook.LeftDown += OnDown;
			_hook.LeftUp += OnUp;
			_hook.Moved += OnMove;
		}

		public void Start()
		{
			_hook.Install();
		}

		public void Stop()
		{
			_hook.Uninstall();
			_timer.Stop();
			_down = false;
		}

		private void OnDown(int x, int y)
		{
			_down = true;
			_dragging = false;
			_fired = false;
			_downX = (_stillX = x);
			_downY = (_stillY = y);
			_stillSince = Environment.TickCount64;
			bool any = AnyApp;
			_isFileSource = Task.Run(() => DragSource.IsFileDragStart(x, y, any));
			_timer.Start();
		}

		private void OnMove(int x, int y)
		{
			if (_down)
			{
				if (!_dragging && Dist(x, y, _downX, _downY) > 10.0)
				{
					_dragging = true;
					_stillX = x;
					_stillY = y;
					_stillSince = Environment.TickCount64;
				}
				else if (_dragging && Dist(x, y, _stillX, _stillY) > 6.0)
				{
					_stillX = x;
					_stillY = y;
					_stillSince = Environment.TickCount64;
				}
			}
		}

		private void OnUp(int x, int y)
		{
			_down = false;
			_timer.Stop();
			if (_fired)
			{
				SafeRaise(() =>
				{
					Released?.Invoke();
				});
			}
		}

		private void Tick()
		{
			if (!_down)
			{
				_timer.Stop();
			}
			else
			{
				if (!_dragging || _fired || Environment.TickCount64 - _stillSince < HoldMilliseconds)
				{
					return;
				}
				Task<bool> isFileSource = _isFileSource;
				if (isFileSource != null && !isFileSource.IsCompleted)
				{
					return;
				}
				if (isFileSource == null || !isFileSource.IsCompletedSuccessfully || !isFileSource.Result)
				{
					_timer.Stop();
					return;
				}
				_fired = true;
				int x = _stillX;
				int y = _stillY;
				SafeRaise(() =>
				{
					HoldDetected?.Invoke(x, y);
				});
			}
		}

		private static double Dist(int x1, int y1, int x2, int y2)
		{
			return Math.Sqrt((double)(x1 - x2) * (double)(x1 - x2) + (double)(y1 - y2) * (double)(y1 - y2));
		}

		private static void SafeRaise(Action a)
		{
			try
			{
				a();
			}
			catch (Exception ex)
			{
				Log.Error("Drag watcher handler failed", ex);
			}
		}

		public void Dispose()
		{
			Stop();
			_hook.Dispose();
		}
	}
	internal sealed class InstanceChannel : IDisposable
	{
		private static readonly string Instance = Environment.GetEnvironmentVariable("ZEST_INSTANCE") ?? "";

		public static readonly string OwnerMutexName = "Local\\Zest.Owner.8F2C" + Instance;

		private readonly CancellationTokenSource _stop = new CancellationTokenSource();

		private readonly Dispatcher _ui;

		private readonly Action<string, IReadOnlyList<string>> _execute;

		private readonly DispatcherTimer _batchTimer;

		private string? _pendingCommand;

		private readonly List<string> _pendingFiles = new List<string>();

		private static string PipeName => $"Zest.Commands.{Process.GetCurrentProcess().SessionId}.{Environment.UserName}{Instance}";

		public InstanceChannel(Dispatcher ui, Action<string, IReadOnlyList<string>> execute)
		{
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Expected Obj, but got Unknown
			_ui = ui;
			_execute = execute;
			_batchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450.0), (DispatcherPriority)9, (EventHandler)((object? _, EventArgs _) =>
			{
				Flush();
			}), ui);
			_batchTimer.Stop();
		}

		public static bool TrySend(string command, IReadOnlyList<string> files, int timeoutMs)
		{
			try
			{
				using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
				namedPipeClientStream.Connect(timeoutMs);
				if (GetNamedPipeServerProcessId(namedPipeClientStream.SafePipeHandle.DangerousGetHandle(), out var serverProcessId))
				{
					AllowSetForegroundWindow(serverProcessId);
				}
				StreamWriter streamWriter = new StreamWriter(namedPipeClientStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
				{
					AutoFlush = true
				};
				streamWriter.WriteLine(command);
				foreach (string file in files)
				{
					streamWriter.WriteLine(file);
				}
				streamWriter.WriteLine();
				return new StreamReader(namedPipeClientStream, Encoding.UTF8).ReadLine() == "ok";
			}
			catch
			{
				return false;
			}
		}

		public void StartListening()
		{
			Thread thread = new Thread(Listen);
			thread.IsBackground = true;
			thread.Name = "Zest pipe";
			thread.Start();
		}

		private void Listen()
		{
			while (!_stop.IsCancellationRequested)
			{
				try
				{
					using NamedPipeServerStream namedPipeServerStream = new NamedPipeServerStream(PipeName, PipeDirection.InOut, -1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
					namedPipeServerStream.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();
					StreamReader streamReader = new StreamReader(namedPipeServerStream, Encoding.UTF8);
					string command = streamReader.ReadLine();
					List<string> files = new List<string>();
					string item;
					while (!string.IsNullOrEmpty(item = streamReader.ReadLine()))
					{
						files.Add(item);
					}
					StreamWriter streamWriter = new StreamWriter(namedPipeServerStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
					streamWriter.AutoFlush = true;
					streamWriter.WriteLine("ok");
					if (command != null)
					{
						_ui.BeginInvoke((Delegate)(Action)(() =>
						{
							Add(command, files);
						}), Array.Empty<object>());
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex2)
				{
					Log.Error("Pipe error", ex2);
					Thread.Sleep(200);
				}
			}
		}

		public void Add(string command, IReadOnlyList<string> files)
		{
			if (_pendingCommand != null && _pendingCommand != command)
			{
				Flush();
			}
			_pendingCommand = command;
			foreach (string file in files)
			{
				if (!_pendingFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
				{
					_pendingFiles.Add(file);
				}
			}
			_batchTimer.Stop();
			_batchTimer.Start();
		}

		private void Flush()
		{
			_batchTimer.Stop();
			if (_pendingCommand == null)
			{
				return;
			}
			string pendingCommand = _pendingCommand;
			string[] arg = _pendingFiles.ToArray();
			_pendingCommand = null;
			_pendingFiles.Clear();
			try
			{
				_execute(pendingCommand, arg);
			}
			catch (Exception ex)
			{
				Log.Error("Command failed: " + pendingCommand, ex);
			}
		}

		public void Dispose()
		{
			_stop.Cancel();
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

		[DllImport("user32.dll")]
		private static extern bool AllowSetForegroundWindow(uint processId);
	}
	internal static class ShellMenu
	{
		private record Item(string Key, string Text, string? Command, Item[]? Children = null);

		private const string Root = "Software\\Classes\\SystemFileAssociations";

		private const string Verb = "Zest";

		private static Item[] ImageItems => new Item[10]
		{
			new Item("01wheel", "Open Zest wheel", "wheel"),
			new Item("02convert", "Convert to", null, ImageConverter.Targets.Select((string t, int i) => new Item($"{i:00}{t.ToLowerInvariant()}", t, "convert:" + t)).ToArray()),
			new Item("03compress", "Compress…", "compress"),
			new Item("04crop", "Crop…", "crop"),
			new Item("05edit", "Edit…", "edit"),
			new Item("06addbg", "Add background…", "addbg"),
			new Item("07metadata", "Metadata…", "metadata"),
			new Item("08redact", "Redact…", "redact"),
			new Item("09annotate", "Annotate…", "annotate"),
			new Item("10readqr", "Read QR codes", "readqr")
		};

		private static Item[] PdfItems => new Item[7]
		{
			new Item("01wheel", "Open Zest wheel", "wheel"),
			new Item("02convert", "Convert to", null, PdfConverter.Targets.Select((string t, int i) => new Item($"{i:00}{t.ToLowerInvariant()}", t, "convert:" + t)).ToArray()),
			new Item("03compress", "Compress…", "compress"),
			new Item("04split", "Split…", "split"),
			new Item("05organize", "Organize pages…", "organize"),
			new Item("06readqr", "Read QR codes", "readqr"),
			new Item("07metadata", "Metadata…", "metadata")
		};

		private static Item[] VideoItems => new Item[3]
		{
			new Item("01wheel", "Open Zest wheel", "wheel"),
			new Item("02compress", "Compress…", "compress"),
			new Item("03mp4", "Convert to MP4", "convert:MP4")
		};

		private static IEnumerable<(string Ext, Item[] Items)> Targets => FileKinds.ImageExtensions.Select((string e) => (e: e, ImageItems: ImageItems)).Concat(FileKinds.VideoExtensions.Select((string e) => (e: e, VideoItems: VideoItems))).Append((".pdf", PdfItems));

		public static bool IsRegistered
		{
			get
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Classes\\SystemFileAssociations\\.jpg\\shell\\Zest");
				return registryKey != null;
			}
		}

		public static void Register(string exePath)
		{
			foreach (var target in Targets)
			{
				string item = target.Ext;
				Item[] item2 = target.Items;
				string subkey = $"{"Software\\Classes\\SystemFileAssociations"}\\{item}\\shell\\{"Zest"}";
				Registry.CurrentUser.DeleteSubKeyTree(subkey, throwOnMissingSubKey: false);
				using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(subkey);
				registryKey.SetValue("MUIVerb", "Zest");
				registryKey.SetValue("Icon", "\"" + exePath + "\",0");
				registryKey.SetValue("SubCommands", "");
				WriteItems(registryKey, item2, exePath);
			}
			NotifyShell();
		}

		private static void WriteItems(RegistryKey parent, Item[] items, string exePath)
		{
			using RegistryKey registryKey = parent.CreateSubKey("shell");
			foreach (Item item in items)
			{
				using RegistryKey registryKey2 = registryKey.CreateSubKey(item.Key);
				registryKey2.SetValue("MUIVerb", item.Text);
				if (item.Children != null)
				{
					registryKey2.SetValue("SubCommands", "");
					WriteItems(registryKey2, item.Children, exePath);
					continue;
				}
				using RegistryKey registryKey3 = registryKey2.CreateSubKey("command");
				registryKey3.SetValue("", $"\"{exePath}\" --{item.Command} \"%1\"");
			}
		}

		public static void Unregister()
		{
			foreach (var target in Targets)
			{
				string item = target.Ext;
				Registry.CurrentUser.DeleteSubKeyTree($"{"Software\\Classes\\SystemFileAssociations"}\\{item}\\shell\\{"Zest"}", throwOnMissingSubKey: false);
			}
			NotifyShell();
		}

		[DllImport("shell32.dll")]
		private static extern void SHChangeNotify(int eventId, uint flags, nint a, nint b);

		private static void NotifyShell()
		{
			SHChangeNotify(134217728, 0u, IntPtr.Zero, IntPtr.Zero);
		}
	}
	internal sealed class TrayService : IDisposable
	{
		private readonly NotifyIcon _icon;

		private readonly App _app;

		public TrayService(App app)
		{
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Unknown result type (might be due to invalid IL or missing references)
			//IL_004b: Expected Obj, but got Unknown
			//IL_004b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_0062: Expected Obj, but got Unknown
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0068: Expected Obj, but got Unknown
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c7: Expected Obj, but got Unknown
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Expected Obj, but got Unknown
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0101: Unknown result type (might be due to invalid IL or missing references)
			//IL_010d: Expected Obj, but got Unknown
			//IL_012a: Unknown result type (might be due to invalid IL or missing references)
			//IL_012f: Unknown result type (might be due to invalid IL or missing references)
			//IL_013b: Expected Obj, but got Unknown
			//IL_0158: Unknown result type (might be due to invalid IL or missing references)
			//IL_015d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0164: Unknown result type (might be due to invalid IL or missing references)
			//IL_0174: Expected Obj, but got Unknown
			//IL_020a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0214: Expected Obj, but got Unknown
			//IL_025e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0268: Expected Obj, but got Unknown
			_app = app;
			using Stream stream = typeof(TrayService).Assembly.GetManifestResourceStream("zest.ico");
			_icon = new NotifyIcon
			{
				Icon = new Icon(stream, SystemInformation.SmallIconSize),
				Text = "Zest – drag files and hold",
				Visible = true
			};
			ContextMenuStrip val = new ContextMenuStrip();
			((ToolStrip)val).Items.Add("Open files…", (Image)null, (EventHandler)((object? _, EventArgs _) =>
			{
				PickFiles();
			}));
			((ToolStrip)val).Items.Add("Wheel for Explorer selection   Ctrl+Alt+Z", (Image)null, (EventHandler)((object? _, EventArgs _) =>
			{
				Toast("Tip", "Click a File Explorer window, select files, then press Ctrl+Alt+Z.");
			}));
			((ToolStrip)val).Items.Add((ToolStripItem)new ToolStripSeparator());
			ToolStripMenuItem drag = new ToolStripMenuItem("Drag and hold to open the wheel")
			{
				CheckOnClick = true
			};
			((ToolStripItem)drag).Click += (object? _, EventArgs _) =>
			{
				_app.SetDragDetection(drag.Checked);
			};
			ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows")
			{
				CheckOnClick = true
			};
			((ToolStripItem)startup).Click += (object? _, EventArgs _) =>
			{
				try
				{
					Startup.Set(startup.Checked);
				}
				catch (Exception ex)
				{
					Log.Error("Startup toggle failed", ex);
					startup.Checked = Startup.IsEnabled;
				}
			};
			ToolStripMenuItem shellMenu = new ToolStripMenuItem("Show Zest in the right-click menu")
			{
				CheckOnClick = true
			};
			((ToolStripItem)shellMenu).Click += (object? _, EventArgs _) =>
			{
				_app.SetShellMenu(shellMenu.Checked);
			};
			ToolStripMenuItem anyApp = new ToolStripMenuItem("    …also from other apps (browsers, etc.)")
			{
				CheckOnClick = true,
				ToolTipText = "Off: only dragging a file from File Explorer or the Desktop opens the wheel."
			};
			((ToolStripItem)anyApp).Click += (object? _, EventArgs _) =>
			{
				_app.SetDragFromAnyApp(anyApp.Checked);
			};
			((ToolStrip)val).Items.Add((ToolStripItem)(object)drag);
			((ToolStrip)val).Items.Add((ToolStripItem)(object)anyApp);
			((ToolStrip)val).Items.Add((ToolStripItem)(object)shellMenu);
			((ToolStrip)val).Items.Add((ToolStripItem)(object)startup);
			((ToolStrip)val).Items.Add("Open log file", (Image)null, (EventHandler)((object? _, EventArgs _) =>
			{
				if (!File.Exists(Log.FilePath))
				{
					Toast("No errors", "The log file is empty.");
					return;
				}
				ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"))
				{
					UseShellExecute = false
				};
				processStartInfo.ArgumentList.Add(Log.FilePath);
				try
				{
					Process.Start(processStartInfo);
				}
				catch (Exception ex)
				{
					Log.Error("Could not open the log", ex);
					Toast("Log file", Log.FilePath);
				}
			}));
			((ToolStrip)val).Items.Add((ToolStripItem)new ToolStripSeparator());
			((ToolStrip)val).Items.Add("Exit", (Image)null, (EventHandler)((object? _, EventArgs _) =>
			{
				((Application)_app).Shutdown();
			}));
			((ToolStripDropDown)val).Opening += (object? _, CancelEventArgs _) =>
			{
				drag.Checked = _app.DragDetectionOn;
				anyApp.Checked = _app.DragFromAnyApp;
				((ToolStripItem)anyApp).Enabled = drag.Checked;
				startup.Checked = Startup.IsEnabled;
				shellMenu.Checked = _app.ShellMenuOn;
			};
			_icon.ContextMenuStrip = val;
			_icon.MouseClick += (object? _, MouseEventArgs e) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_000b: Invalid comparison between Unknown and I4
				if ((int)e.Button == 1048576)
				{
					PickFiles();
				}
			};
		}

		private static void Toast(string title, string msg)
		{
			ToastWindow.Show(title, msg);
		}

		private void PickFiles()
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Expected Obj, but got Unknown
			OpenFileDialog val = new OpenFileDialog
			{
				Multiselect = true,
				Title = "Choose files for Zest",
				Filter = "Images, PDF and videos|" + string.Join(";", from e in FileKinds.ImageExtensions.Concat(FileKinds.VideoExtensions).Append(".pdf")
					select "*" + e) + "|All files|*.*"
			};
			if (((CommonDialog)val).ShowDialog() == true)
			{
				NativeMethods.GetCursorPos(out var p);
				_app.ShowWheelForFiles(((FileDialog)val).FileNames, p.X, p.Y);
			}
		}

		public void Dispose()
		{
			_icon.Visible = false;
			((Component)(object)_icon).Dispose();
		}
	}
}
namespace Zest.App.Native
{
	internal sealed class GlobalHotkey : IDisposable
	{
		private const int Id = 23127;

		private readonly HwndSource _source;

		private bool _registered;

		public event Action? Pressed;

		public GlobalHotkey()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0036: Expected Obj, but got Unknown
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Expected Obj, but got Unknown
			HwndSourceParameters val = new HwndSourceParameters("ZestHotkeyWindow");
			val.ParentWindow = new IntPtr(-3);
			val.WindowStyle = 0;
			HwndSourceParameters val2 = val;
			_source = new HwndSource(val2);
			_source.AddHook((HwndSourceHook)WndProc);
		}

		public bool Register(uint modifiers, uint virtualKey)
		{
			if (_registered)
			{
				NativeMethods.UnregisterHotKey(_source.Handle, 23127);
			}
			_registered = NativeMethods.RegisterHotKey(_source.Handle, 23127, modifiers | 0x4000, virtualKey);
			return _registered;
		}

		private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
		{
			if (msg == 786 && (int)wParam == 23127)
			{
				handled = true;
				Pressed?.Invoke();
			}
			return IntPtr.Zero;
		}

		public void Dispose()
		{
			if (_registered)
			{
				NativeMethods.UnregisterHotKey(_source.Handle, 23127);
			}
			_source.Dispose();
		}
	}
	internal sealed class MouseHook : IDisposable
	{
		private readonly NativeMethods.LowLevelProc _proc;

		private nint _hook;

		public bool IsInstalled => _hook != IntPtr.Zero;

		public event Action<int, int>? LeftDown;

		public event Action<int, int>? LeftUp;

		public event Action<int, int>? Moved;

		public MouseHook()
		{
			_proc = Callback;
		}

		public void Install()
		{
			if (_hook == IntPtr.Zero)
			{
				_hook = NativeMethods.SetWindowsHookEx(14, _proc, NativeMethods.GetModuleHandle(null), 0u);
				if (_hook == IntPtr.Zero)
				{
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the mouse hook.");
				}
			}
		}

		public void Uninstall()
		{
			if (_hook != IntPtr.Zero)
			{
				NativeMethods.UnhookWindowsHookEx(_hook);
				_hook = IntPtr.Zero;
			}
		}

		private nint Callback(int nCode, nint wParam, nint lParam)
		{
			if (nCode >= 0)
			{
				NativeMethods.MSLLHOOKSTRUCT mSLLHOOKSTRUCT = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
				switch ((int)wParam)
				{
				case 513:
					LeftDown?.Invoke(mSLLHOOKSTRUCT.pt.X, mSLLHOOKSTRUCT.pt.Y);
					break;
				case 514:
					LeftUp?.Invoke(mSLLHOOKSTRUCT.pt.X, mSLLHOOKSTRUCT.pt.Y);
					break;
				case 512:
					Moved?.Invoke(mSLLHOOKSTRUCT.pt.X, mSLLHOOKSTRUCT.pt.Y);
					break;
				}
			}
			return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
		}

		public void Dispose()
		{
			Uninstall();
		}
	}
	internal static class NativeMethods
	{
		public struct POINT(int x, int y)
		{
			public int X = x;

			public int Y = y;
		}

		public struct MSLLHOOKSTRUCT
		{
			public POINT pt;

			public uint mouseData;

			public uint flags;

			public uint time;

			public nint dwExtraInfo;
		}

		public delegate nint LowLevelProc(int nCode, nint wParam, nint lParam);

		public struct RECT
		{
			public int Left;

			public int Top;

			public int Right;

			public int Bottom;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		public struct MONITORINFO
		{
			public int cbSize;

			public RECT rcMonitor;

			public RECT rcWork;

			public uint dwFlags;
		}

		public struct MARGINS
		{
			public int L;

			public int R;

			public int T;

			public int B;
		}

		public struct AccentPolicy
		{
			public int AccentState;

			public int AccentFlags;

			public int GradientColor;

			public int AnimationId;
		}

		public struct WindowCompositionAttributeData
		{
			public int Attribute;

			public nint Data;

			public int SizeOfData;
		}

		public const int WH_MOUSE_LL = 14;

		public const int WM_MOUSEMOVE = 512;

		public const int WM_LBUTTONDOWN = 513;

		public const int WM_LBUTTONUP = 514;

		public const int VK_LBUTTON = 1;

		public static readonly nint HWND_TOPMOST = new IntPtr(-1);

		public const uint SWP_NOSIZE = 1u;

		public const uint SWP_NOACTIVATE = 16u;

		public const uint SWP_SHOWWINDOW = 64u;

		public const int GWL_EXSTYLE = -20;

		public const long WS_EX_TOOLWINDOW = 128L;

		public const long WS_EX_NOACTIVATE = 134217728L;

		public const int WM_HOTKEY = 786;

		public const uint MOD_ALT = 1u;

		public const uint MOD_CONTROL = 2u;

		public const uint MOD_SHIFT = 4u;

		public const uint MOD_NOREPEAT = 16384u;

		public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

		public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

		[DllImport("user32.dll", SetLastError = true)]
		public static extern nint SetWindowsHookEx(int idHook, LowLevelProc lpfn, nint hMod, uint dwThreadId);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool UnhookWindowsHookEx(nint hhk);

		[DllImport("user32.dll")]
		public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern nint GetModuleHandle(string? lpModuleName);

		[DllImport("user32.dll")]
		public static extern short GetAsyncKeyState(int vKey);

		[DllImport("user32.dll")]
		public static extern bool GetCursorPos(out POINT p);

		[DllImport("user32.dll")]
		public static extern nint GetForegroundWindow();

		[DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(nint hWnd);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetClassName(nint hWnd, StringBuilder name, int max);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetWindowText(nint hWnd, StringBuilder text, int max);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		public static extern nint GetWindowLongPtr(nint hWnd, int index);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

		[DllImport("user32.dll")]
		public static extern nint MonitorFromPoint(POINT pt, uint flags);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO info);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

		[DllImport("user32.dll")]
		public static extern bool UnregisterHotKey(nint hWnd, int id);

		[DllImport("gdi32.dll")]
		public static extern nint CreateEllipticRgn(int l, int t, int r, int b);

		[DllImport("user32.dll")]
		public static extern int SetWindowRgn(nint hWnd, nint hRgn, bool redraw);

		[DllImport("dwmapi.dll")]
		public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

		[DllImport("dwmapi.dll")]
		public static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS m);

		[DllImport("user32.dll")]
		public static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		public static extern nint ILCreateFromPathW(string path);

		[DllImport("shell32.dll")]
		public static extern void ILFree(nint pidl);

		[DllImport("shell32.dll")]
		public static extern int SHOpenFolderAndSelectItems(nint folder, uint count, nint[] items, uint flags);

		public static string ClassName(nint hwnd)
		{
			StringBuilder stringBuilder = new StringBuilder(256);
			GetClassName(hwnd, stringBuilder, stringBuilder.Capacity);
			return stringBuilder.ToString();
		}

		public static string WindowText(nint hwnd)
		{
			StringBuilder stringBuilder = new StringBuilder(512);
			GetWindowText(hwnd, stringBuilder, stringBuilder.Capacity);
			return stringBuilder.ToString();
		}
	}
	internal static class WindowEffects
	{
		private static bool IsWin11_22H2 => Environment.OSVersion.Version.Build >= 22621;

		public static bool TryApplyMica(Window window, bool dark)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			nint num = new WindowInteropHelper(window).EnsureHandle();
			int value = 2;
			NativeMethods.DwmSetWindowAttribute(num, 33, ref value, 4);
			SetDarkTitleBar(num, dark);
			if (!IsWin11_22H2 || Environment.GetEnvironmentVariable("ZEST_NO_MICA") == "1")
			{
				return false;
			}
			int value2 = 2;
			if (NativeMethods.DwmSetWindowAttribute(num, 38, ref value2, 4) != 0)
			{
				return false;
			}
			HwndSource val = HwndSource.FromHwnd(num);
			if (((val != null) ? val.CompositionTarget : null) != null)
			{
				val.CompositionTarget.BackgroundColor = Colors.Transparent;
			}
			NativeMethods.MARGINS m = new NativeMethods.MARGINS
			{
				L = -1,
				R = -1,
				T = -1,
				B = -1
			};
			NativeMethods.DwmExtendFrameIntoClientArea(num, ref m);
			return true;
		}

		public static void SetDarkTitleBar(nint hwnd, bool dark)
		{
			int value = (dark ? 1 : 0);
			NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref value, 4);
		}

		public static void MakeNoActivateToolWindow(nint hwnd)
		{
			long num = ((IntPtr)NativeMethods.GetWindowLongPtr(hwnd, -20)).ToInt64();
			NativeMethods.SetWindowLongPtr(hwnd, -20, new IntPtr(num | 0x80 | 0x8000000));
		}

		public static void ClearNoActivate(nint hwnd)
		{
			long num = ((IntPtr)NativeMethods.GetWindowLongPtr(hwnd, -20)).ToInt64();
			NativeMethods.SetWindowLongPtr(hwnd, -20, new IntPtr(num & -134217729));
		}
	}
}
