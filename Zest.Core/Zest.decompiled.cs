using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Ocr;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI;
using ZXing;
using ZXing.Common;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETCoreApp,Version=v8.0", FrameworkDisplayName = ".NET 8.0")]
[assembly: AssemblyCompany("Zest.Core")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: AssemblyProduct("Zest.Core")]
[assembly: AssemblyTitle("Zest.Core")]
[assembly: TargetPlatform("Windows10.0.19041.0")]
[assembly: SupportedOSPlatform("Windows10.0.17763.0")]
[assembly: AssemblyVersion("1.0.0.0")]
[module: RefSafetyRules(11)]
namespace Zest.Core;

public readonly record struct Pt(double X, double Y);
public abstract record Annotation(string Color);
public sealed record PenStroke(IReadOnlyList<Pt> Points, string Color, double Width) : Annotation(Color);
public sealed record ArrowShape(Pt From, Pt To, string Color, double Width) : Annotation(Color);
public sealed record HighlightBox(CropBox Box, string Color) : Annotation(Color);
public sealed record TextNote(Pt At, string Text, string Color, double Size) : Annotation(Color);
public static class ArrowGeometry
{
	public static (Pt Tip, Pt Left, Pt Right, Pt ShaftEnd) Head(Pt from, Pt to, double width)
	{
		double num = to.X - from.X;
		double num2 = to.Y - from.Y;
		double num3 = Math.Sqrt(num * num + num2 * num2);
		if (num3 < 0.001)
		{
			return (Tip: to, Left: to, Right: to, ShaftEnd: to);
		}
		double num4 = num / num3;
		double num5 = num2 / num3;
		double num6 = Math.Min(Math.Max(width * 4.0, 12.0), num3);
		double num7 = num6 * 0.55;
		Pt pt = new Pt(to.X - num4 * num6, to.Y - num5 * num6);
		return new ValueTuple<Pt, Pt, Pt, Pt>(item2: new Pt(pt.X - num5 * num7, pt.Y + num4 * num7), item3: new Pt(pt.X + num5 * num7, pt.Y - num4 * num7), item4: new Pt(to.X - num4 * num6 * 0.8, to.Y - num5 * num6 * 0.8), item1: to);
	}
}
public static class Annotator
{
	public const double HighlightOpacity = 0.4;

	public static string FontFile
	{
		get
		{
			if (!File.Exists("C:\\Windows\\Fonts\\segoeui.ttf"))
			{
				return "C:\\Windows\\Fonts\\arial.ttf";
			}
			return "C:\\Windows\\Fonts\\segoeui.ttf";
		}
	}

	public static double DefaultWidth(double imgW, double imgH)
	{
		return Math.Max(3.0, Math.Round(Math.Max(imgW, imgH) / 250.0));
	}

	public static void Draw(MagickImage image, IEnumerable<Annotation> items)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected Obj, but got Unknown
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected Obj, but got Unknown
		//IL_02b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bc: Expected Obj, but got Unknown
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		//IL_031c: Expected Obj, but got Unknown
		//IL_0332: Unknown result type (might be due to invalid IL or missing references)
		//IL_0337: Unknown result type (might be due to invalid IL or missing references)
		//IL_034c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0351: Unknown result type (might be due to invalid IL or missing references)
		//IL_0366: Unknown result type (might be due to invalid IL or missing references)
		//IL_036b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Expected Obj, but got Unknown
		//IL_03a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ae: Expected Obj, but got Unknown
		foreach (Annotation item in items)
		{
			Drawables val = new Drawables();
			Annotation annotation = item;
			if (!(annotation is HighlightBox highlightBox))
			{
				if (!(annotation is PenStroke penStroke))
				{
					if (!(annotation is ArrowShape arrowShape))
					{
						if (!(annotation is TextNote textNote) || string.IsNullOrWhiteSpace(textNote.Text))
						{
							continue;
						}
						val.Font(FontFile).FontPointSize(textNote.Size).FillColor((IMagickColor<byte>)new MagickColor(textNote.Color))
							.StrokeColor((IMagickColor<byte>)(object)MagickColors.Transparent)
							.Gravity((Gravity)1)
							.Text(textNote.At.X, textNote.At.Y, textNote.Text);
					}
					else
					{
						var (pt, pt2, pt3, pt4) = ArrowGeometry.Head(arrowShape.From, arrowShape.To, arrowShape.Width);
						val.FillColor((IMagickColor<byte>)(object)MagickColors.Transparent).StrokeColor((IMagickColor<byte>)new MagickColor(arrowShape.Color)).StrokeWidth(arrowShape.Width)
							.StrokeLineCap((LineCap)2)
							.Line(arrowShape.From.X, arrowShape.From.Y, pt4.X, pt4.Y)
							.StrokeColor((IMagickColor<byte>)(object)MagickColors.Transparent)
							.FillColor((IMagickColor<byte>)new MagickColor(arrowShape.Color))
							.Polygon(new PointD[3]
							{
								new PointD(pt.X, pt.Y),
								new PointD(pt2.X, pt2.Y),
								new PointD(pt3.X, pt3.Y)
							});
					}
				}
				else if (penStroke.Points.Count == 1)
				{
					val.FillColor((IMagickColor<byte>)new MagickColor(penStroke.Color)).StrokeColor((IMagickColor<byte>)(object)MagickColors.Transparent).Circle(penStroke.Points[0].X, penStroke.Points[0].Y, penStroke.Points[0].X + penStroke.Width / 2.0, penStroke.Points[0].Y);
				}
				else
				{
					PenStroke penStroke2 = penStroke;
					if (penStroke2.Points.Count <= 1)
					{
						continue;
					}
					val.FillColor((IMagickColor<byte>)(object)MagickColors.Transparent).StrokeColor((IMagickColor<byte>)new MagickColor(penStroke2.Color)).StrokeWidth(penStroke2.Width)
						.StrokeLineCap((LineCap)2)
						.StrokeLineJoin((LineJoin)2);
					if (penStroke2.Points.Count == 2)
					{
						val.Line(penStroke2.Points[0].X, penStroke2.Points[0].Y, penStroke2.Points[1].X, penStroke2.Points[1].Y);
					}
					else
					{
						val.Polyline(((IEnumerable<Pt>)penStroke2.Points).Select((Func<Pt, PointD>)((Pt q) =>
						{
							//IL_000e: Unknown result type (might be due to invalid IL or missing references)
							return new PointD(q.X, q.Y);
						})).ToArray());
					}
				}
			}
			else
			{
				val.FillColor((IMagickColor<byte>)(object)WithAlpha(highlightBox.Color, 0.4)).StrokeColor((IMagickColor<byte>)(object)MagickColors.Transparent).Rectangle(highlightBox.Box.X, highlightBox.Box.Y, highlightBox.Box.Right, highlightBox.Box.Bottom);
			}
			val.Draw((IMagickImage<byte>)(object)image);
		}
	}

	private static MagickColor WithAlpha(string hex, double opacity)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		MagickColor val = new MagickColor(hex);
		return new MagickColor(val.R, val.G, val.B, (byte)Math.Round(255.0 * opacity));
	}

	public static string Save(string path, IReadOnlyList<Annotation> items)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected Obj, but got Unknown
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected Obj, but got Unknown
		if (items.Count == 0)
		{
			throw new ArgumentException("Draw something first.");
		}
		MagickImage val = new MagickImage(path);
		try
		{
			int quality = ImageSaver.OriginalQuality(val);
			val.AutoOrient();
			val.Density = new Density(72.0);
			Draw(val, items);
			return ImageSaver.SaveNextTo(val, path, "-annotated", quality);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public enum BgMode
{
	Solid,
	Gradient,
	Blur
}
public enum GradientDirection
{
	Vertical,
	Horizontal,
	Diagonal
}
public sealed record BgSettings(BgMode Mode = BgMode.Solid, string Color1 = "#FFFFFF", string Color2 = "#FFB347", GradientDirection Direction = GradientDirection.Vertical, int BlurStrength = 50);
public static class BackgroundFiller
{
	public static bool HasTransparency(MagickImage image)
	{
		if (!image.HasAlpha)
		{
			return false;
		}
		IMagickImage<byte> val = image.Separate((Channels)16).First();
		try
		{
			IChannelStatistics channel = ((IMagickImage)val).Statistics().GetChannel((PixelChannel)0);
			return ((channel != null) ? new double?(channel.Minimum) : ((double?)null)) < (double)(int)Quantum.Max;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static MagickImage Compose(MagickImage source, BgSettings s)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected Obj, but got Unknown
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		uint width = source.Width;
		uint height = source.Height;
		MagickImage val = s.Mode switch
		{
			BgMode.Solid => new MagickImage((IMagickColor<byte>)new MagickColor(s.Color1), width, height), 
			BgMode.Gradient => MakeGradient(width, height, s), 
			_ => MakeBlur(source, s.BlurStrength), 
		};
		val.Composite((IMagickImage)(object)source, (CompositeOperator)54);
		val.Alpha((AlphaAction)9);
		val.Format = source.Format;
		return val;
	}

	private static MagickImage MakeGradient(uint w, uint h, BgSettings s)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected Obj, but got Unknown
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Expected Obj, but got Unknown
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Expected Obj, but got Unknown
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Expected Obj, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Expected Obj, but got Unknown
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Expected Obj, but got Unknown
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Expected Obj, but got Unknown
		string text = "gradient:" + s.Color1 + "-" + s.Color2;
		switch (s.Direction)
		{
		case GradientDirection.Horizontal:
		{
			MagickImage val2 = new MagickImage(text, (IMagickReadSettings<byte>)new MagickReadSettings
			{
				Width = h,
				Height = w
			});
			val2.Rotate(-90.0);
			return Fix(val2, w, h);
		}
		case GradientDirection.Diagonal:
		{
			uint value = (uint)Math.Ceiling((double)(w + h) / Math.Sqrt(2.0)) + 2;
			MagickImage val = new MagickImage(text, (IMagickReadSettings<byte>)new MagickReadSettings
			{
				Width = value,
				Height = value
			});
			val.Rotate(-45.0);
			val.Crop((IMagickGeometry)new MagickGeometry((int)(val.Width - w) / 2, (int)(val.Height - h) / 2, w, h));
			val.ResetPage();
			return Fix(val, w, h);
		}
		default:
			return new MagickImage(text, (IMagickReadSettings<byte>)new MagickReadSettings
			{
				Width = w,
				Height = h
			});
		}
	}

	private static MagickImage Fix(MagickImage g, uint w, uint h)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected Obj, but got Unknown
		g.ResetPage();
		if (g.Width != w || g.Height != h)
		{
			g.Resize((IMagickGeometry)new MagickGeometry(w, h)
			{
				IgnoreAspectRatio = true
			});
		}
		return g;
	}

	private static MagickImage MakeBlur(MagickImage source, int strength)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected Obj, but got Unknown
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected Obj, but got Unknown
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected Obj, but got Unknown
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected Obj, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected Obj, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Expected Obj, but got Unknown
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected Obj, but got Unknown
		uint width = source.Width;
		uint height = source.Height;
		MagickImage val = (MagickImage)source.Clone();
		try
		{
			val.Resize((IMagickGeometry)new MagickGeometry(1u, 1u)
			{
				IgnoreAspectRatio = true
			});
			IMagickColor<byte> val2 = val.GetPixels().GetPixel(0, 0).ToColor();
			MagickColor val3 = new MagickColor(val2.R, val2.G, val2.B);
			MagickImage val4 = (MagickImage)source.Clone();
			try
			{
				val4.Resize((IMagickGeometry)new MagickGeometry(200u, 200u));
				val4.Blur(0.0, 2.0 + (double)Math.Clamp(strength, 1, 100) / 6.0);
				val4.Resize((IMagickGeometry)new MagickGeometry(width, height)
				{
					IgnoreAspectRatio = true
				});
				MagickImage val5 = new MagickImage((IMagickColor<byte>)(object)val3, width, height);
				val5.Composite((IMagickImage)(object)val4, (CompositeOperator)54);
				return val5;
			}
			finally
			{
				((IDisposable)val4)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static string Save(string path, BgSettings s)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		MagickImage val = new MagickImage(path);
		try
		{
			val.AutoOrient();
			MagickImage val2 = Compose(val, s);
			try
			{
				return ImageSaver.SaveNextTo(val2, path, "-bg");
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public enum CompressMode
{
	Percentage,
	TargetSize
}
public enum CompressSupport
{
	Lossy,
	LosslessWithColorReduction,
	LosslessOnly,
	Pdf,
	Video,
	NotSupported
}
public sealed record CompressResult(string SourcePath, string? OutputPath, long OriginalBytes, long OutputBytes, bool ReachedTarget, string Message);
public static class CompressPlan
{
	public static long TargetBytes(long originalBytes, CompressMode mode, double reducePercent, double targetKb)
	{
		long val = ((mode == CompressMode.Percentage) ? ((long)((double)originalBytes * (1.0 - Math.Clamp(reducePercent, 0.0, 99.0) / 100.0))) : ((long)(targetKb * 1024.0)));
		return Math.Max(1024L, val);
	}

	public static CompressSupport SupportFor(string path)
	{
		if (FileKinds.Detect(path) == FileKind.Video)
		{
			return CompressSupport.Video;
		}
		string text = Path.GetExtension(path).ToLowerInvariant();
		CompressSupport result;
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					switch (text[2])
					{
					case 'p':
						break;
					case 'f':
						goto IL_0102;
					case 'e':
						goto IL_0111;
					case 'v':
						goto IL_013a;
					case 'i':
						goto IL_0149;
					default:
						goto IL_0168;
					}
					if (text == ".jpeg")
					{
						goto IL_0158;
					}
				}
			}
			else
			{
				char c = text[2];
				if ((uint)c <= 105u)
				{
					if (c != 'd')
					{
						if (c == 'i' && text == ".tif")
						{
							goto IL_0160;
						}
					}
					else if (text == ".pdf")
					{
						result = CompressSupport.Pdf;
						goto IL_016a;
					}
				}
				else if (c != 'n')
				{
					if (c == 'p' && text == ".jpg")
					{
						goto IL_0158;
					}
				}
				else if (text == ".png")
				{
					result = CompressSupport.LosslessWithColorReduction;
					goto IL_016a;
				}
			}
		}
		goto IL_0168;
		IL_013a:
		if (text == ".avif")
		{
			goto IL_0158;
		}
		goto IL_0168;
		IL_0149:
		if (text == ".tiff")
		{
			goto IL_0160;
		}
		goto IL_0168;
		IL_0158:
		result = CompressSupport.Lossy;
		goto IL_016a;
		IL_0102:
		if (text == ".jfif")
		{
			goto IL_0158;
		}
		goto IL_0168;
		IL_0168:
		result = CompressSupport.NotSupported;
		goto IL_016a;
		IL_0111:
		switch (text)
		{
		case ".webp":
		case ".heic":
		case ".heif":
			break;
		default:
			goto IL_0168;
		}
		goto IL_0158;
		IL_0160:
		result = CompressSupport.LosslessOnly;
		goto IL_016a;
		IL_016a:
		return result;
	}

	public static string HintFor(string path)
	{
		return SupportFor(path) switch
		{
			CompressSupport.Lossy => "Zest searches for the best quality that fits the size.", 
			CompressSupport.LosslessWithColorReduction => "PNG is lossless, so it cannot shrink much. Zest optimizes it first, then reduces colors if needed. For big savings, convert to WEBP or JPG.", 
			CompressSupport.LosslessOnly => "TIFF is saved with lossless compression. Savings may be small.", 
			CompressSupport.Pdf => "Zest re-encodes and downsamples the images inside the PDF. Text-only PDFs cannot shrink much.", 
			CompressSupport.Video => "The video is saved again as MP4 (H.264 + AAC) at a bitrate that fits the size. Width, height and length stay the same. Very small sizes make the video blurry.", 
			_ => "This format cannot be compressed in a useful way. Convert it to PNG, JPG or WEBP instead.", 
		};
	}
}
public enum CropHandle
{
	None,
	Move,
	N,
	S,
	E,
	W,
	NE,
	NW,
	SE,
	SW
}
public readonly record struct CropBox(double X, double Y, double W, double H)
{
	public double Right => X + W;

	public double Bottom => Y + H;

	public double CenterX => X + W / 2.0;

	public double CenterY => Y + H / 2.0;
}
public static class CropMath
{
	public const double MinSize = 8.0;

	public static CropBox Full(double imgW, double imgH)
	{
		return new CropBox(0.0, 0.0, imgW, imgH);
	}

	public static CropBox FitRatio(double imgW, double imgH, double ratio)
	{
		double num;
		double num2;
		if (imgW / imgH > ratio)
		{
			num = imgH;
			num2 = num * ratio;
		}
		else
		{
			num2 = imgW;
			num = num2 / ratio;
		}
		return new CropBox((imgW - num2) / 2.0, (imgH - num) / 2.0, num2, num);
	}

	public static CropBox Drag(CropBox start, CropHandle handle, double dx, double dy, double? ratio, double imgW, double imgH)
	{
		bool flag;
		switch (handle)
		{
		case CropHandle.None:
			return start;
		case CropHandle.Move:
		{
			double x = Math.Clamp(start.X + dx, 0.0, Math.Max(0.0, imgW - start.W));
			double y = Math.Clamp(start.Y + dy, 0.0, Math.Max(0.0, imgH - start.H));
			return start with
			{
				X = x,
				Y = y
			};
		}
		case CropHandle.W:
		case CropHandle.NW:
		case CropHandle.SW:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = flag;
		switch (handle)
		{
		case CropHandle.E:
		case CropHandle.NE:
		case CropHandle.SE:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag3 = flag;
		flag = ((handle == CropHandle.N || (uint)(handle - 6) <= 1u) ? true : false);
		bool flag4 = flag;
		flag = ((handle == CropHandle.S || (uint)(handle - 8) <= 1u) ? true : false);
		bool flag5 = flag;
		double num = start.X;
		double num2 = start.Y;
		double num3 = start.Right;
		double num4 = start.Bottom;
		double num5 = Math.Min(8.0, Math.Min(imgW, imgH));
		if (flag2)
		{
			num = Math.Clamp(num + dx, 0.0, num3 - num5);
		}
		if (flag3)
		{
			num3 = Math.Clamp(num3 + dx, num + num5, imgW);
		}
		if (flag4)
		{
			num2 = Math.Clamp(num2 + dy, 0.0, num4 - num5);
		}
		if (flag5)
		{
			num4 = Math.Clamp(num4 + dy, num2 + num5, imgH);
		}
		if (ratio.HasValue)
		{
			double valueOrDefault = ratio.GetValueOrDefault();
			if (!(valueOrDefault <= 0.0))
			{
				double centerX = start.CenterX;
				double centerY = start.CenterY;
				bool num6 = (flag4 | flag5) && !(flag2 | flag3);
				double num7 = num3 - num;
				double num8 = num4 - num2;
				if (num6)
				{
					num7 = num8 * valueOrDefault;
				}
				else
				{
					num8 = num7 / valueOrDefault;
				}
				double num9;
				if (flag2)
				{
					num9 = start.Right;
				}
				else
				{
					num9 = (flag3 ? (imgW - start.X) : (2.0 * Math.Min(centerX, imgW - centerX)));
				}
				double num10;
				if (flag4)
				{
					num10 = start.Bottom;
				}
				else
				{
					num10 = (flag5 ? (imgH - start.Y) : (2.0 * Math.Min(centerY, imgH - centerY)));
				}
				if (num8 > num10)
				{
					num8 = num10;
					num7 = num8 * valueOrDefault;
				}
				if (num7 > num9)
				{
					num7 = num9;
					num8 = num7 / valueOrDefault;
				}
				double x2;
				if (flag2)
				{
					x2 = start.Right - num7;
				}
				else
				{
					x2 = (flag3 ? start.X : (centerX - num7 / 2.0));
				}
				double y2;
				if (flag4)
				{
					y2 = start.Bottom - num8;
				}
				else
				{
					y2 = (flag5 ? start.Y : (centerY - num8 / 2.0));
				}
				return new CropBox(x2, y2, num7, num8);
			}
		}
		return new CropBox(num, num2, num3 - num, num4 - num2);
	}

	public static CropBox SetSize(CropBox box, double w, double h, double imgW, double imgH)
	{
		w = Math.Clamp(w, 1.0, imgW);
		h = Math.Clamp(h, 1.0, imgH);
		double x = Math.Clamp(box.CenterX - w / 2.0, 0.0, imgW - w);
		double y = Math.Clamp(box.CenterY - h / 2.0, 0.0, imgH - h);
		return new CropBox(x, y, w, h);
	}

	public static (int X, int Y, int W, int H) ToPixels(CropBox box, int imgW, int imgH)
	{
		int num = Math.Clamp((int)Math.Round(box.X), 0, imgW - 1);
		int num2 = Math.Clamp((int)Math.Round(box.Y), 0, imgH - 1);
		int item = Math.Clamp((int)Math.Round(box.W), 1, imgW - num);
		int item2 = Math.Clamp((int)Math.Round(box.H), 1, imgH - num2);
		return (X: num, Y: num2, W: item, H: item2);
	}
}
public static class DocxWriter
{
	public static void Write(Stream stream, IReadOnlyList<IReadOnlyList<string>> pages)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Expected Obj, but got Unknown
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Expected Obj, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected Obj, but got Unknown
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Expected Obj, but got Unknown
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected Obj, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Expected Obj, but got Unknown
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected Obj, but got Unknown
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected Obj, but got Unknown
		WordprocessingDocument val = WordprocessingDocument.Create(stream, (WordprocessingDocumentType)0);
		try
		{
			MainDocumentPart val2 = val.AddMainDocumentPart();
			Body val3 = new Body();
			for (int i = 0; i < pages.Count; i++)
			{
				if (i > 0)
				{
					((OpenXmlElement)val3).Append(new OpenXmlElement[1] { (OpenXmlElement)new Paragraph(new OpenXmlElement[1] { (OpenXmlElement)new Run(new OpenXmlElement[1] { (OpenXmlElement)new Break
					{
						Type = EnumValue<BreakValues>.op_Implicit(BreakValues.Page)
					} }) }) });
				}
				foreach (string item in pages[i])
				{
					((OpenXmlElement)val3).Append(new OpenXmlElement[1] { (OpenXmlElement)new Paragraph(new OpenXmlElement[1] { (OpenXmlElement)new Run(new OpenXmlElement[1] { (OpenXmlElement)new Text(item)
					{
						Space = EnumValue<SpaceProcessingModeValues>.op_Implicit(SpaceProcessingModeValues.Preserve)
					} }) }) });
				}
			}
			val2.Document = new Document(new OpenXmlElement[1] { (OpenXmlElement)val3 });
			((OpenXmlPartRootElement)val2.Document).Save();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public static class DragSourceRules
{
	private static readonly HashSet<string> FileViewClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DirectUIHWND", "SysListView32" };

	private static readonly HashSet<string> ItemTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ListItem", "DataItem" };

	public static bool IsFileDragStart(string processName, string windowClass, IEnumerable<string> itemTypesUnderMouse, bool anyApp)
	{
		if (anyApp)
		{
			return true;
		}
		if (!processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!FileViewClasses.Contains(windowClass))
		{
			return false;
		}
		return itemTypesUnderMouse.Any(ItemTypes.Contains);
	}
}
public enum FileKind
{
	Unknown,
	Image,
	Pdf,
	Video
}
public static class FileKinds
{
	public static readonly IReadOnlyList<string> ImageExtensions = new string[12]
	{
		".jpg", ".jpeg", ".jfif", ".png", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff",
		".bmp", ".gif"
	};

	public static readonly IReadOnlyList<string> VideoExtensions = new string[8] { ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".3gp" };

	private static readonly HashSet<string> ImageExt = new HashSet<string>(ImageExtensions, StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> VideoExt = new HashSet<string>(VideoExtensions, StringComparer.OrdinalIgnoreCase);

	public static FileKind Detect(string path)
	{
		string extension = Path.GetExtension(path);
		if (ImageExt.Contains(extension))
		{
			return FileKind.Image;
		}
		if (VideoExt.Contains(extension))
		{
			return FileKind.Video;
		}
		if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
		{
			return FileKind.Pdf;
		}
		return FileKind.Unknown;
	}

	public static FileKind DetectGroup(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return FileKind.Unknown;
		}
		FileKind first = Detect(paths[0]);
		if (!paths.All((string p) => Detect(p) == first))
		{
			return FileKind.Unknown;
		}
		return first;
	}
}
public static class ImageCompressor
{
	public static CompressResult Compress(string path, long targetBytes, CancellationToken ct = default(CancellationToken))
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected Obj, but got Unknown
		long length = new FileInfo(path).Length;
		CompressSupport compressSupport = CompressPlan.SupportFor(path);
		if (compressSupport == CompressSupport.NotSupported)
		{
			return new CompressResult(path, null, length, length, ReachedTarget: false, CompressPlan.HintFor(path));
		}
		MagickImage val = new MagickImage(path);
		try
		{
			val.AutoOrient();
			val.Strip();
			(byte[], string)? tuple = compressSupport switch
			{
				CompressSupport.Lossy => ((byte[], string)?)SearchQuality(val, targetBytes, ct), 
				CompressSupport.LosslessWithColorReduction => CompressPng(val, targetBytes, ct), 
				CompressSupport.LosslessOnly => CompressTiff(val), 
				_ => null, 
			};
			if (!tuple.HasValue || tuple.Value.Item1.LongLength >= length)
			{
				return new CompressResult(path, null, length, length, ReachedTarget: false, "Could not make this file smaller. It is already well compressed.");
			}
			(byte[], string) value = tuple.Value;
			byte[] item = value.Item1;
			string item2 = value.Item2;
			bool flag = item.LongLength <= targetBytes;
			string text = OutputPaths.WithSuffix(path, "-compressed");
			SafeFile.WriteNew(text, item);
			string message = (flag ? $"{SizeFormat.Format(length)} → {SizeFormat.Format(item.LongLength)}. {item2}".Trim() : $"Could not reach {SizeFormat.Format(targetBytes)}. Smallest possible: {SizeFormat.Format(item.LongLength)}. {item2}".Trim());
			return new CompressResult(path, text, length, item.LongLength, flag, message);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static (byte[], string) SearchQuality(MagickImage image, long target, CancellationToken ct)
	{
		int num = 5;
		int num2 = 95;
		byte[] array = null;
		int value = 0;
		while (num <= num2)
		{
			ct.ThrowIfCancellationRequested();
			int num3 = (num + num2) / 2;
			byte[] array2 = Encode(image, num3);
			if (array2.LongLength <= target)
			{
				array = array2;
				value = num3;
				num = num3 + 1;
			}
			else
			{
				num2 = num3 - 1;
			}
		}
		if (array != null)
		{
			return (array, $"Quality {value}.");
		}
		return (Encode(image, 5), "Quality 5 (lowest).");
	}

	private static byte[] Encode(MagickImage image, int quality)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Invalid comparison between Unknown and I4
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected Obj, but got Unknown
		MagickFormat format = image.Format;
		if (format - 93 <= 1)
		{
			return WindowsCodecs.EncodeHeicAsync(image, quality).GetAwaiter().GetResult();
		}
		MagickImage val = (MagickImage)image.Clone();
		try
		{
			val.Quality = (uint)quality;
			return val.ToByteArray();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static (byte[], string) CompressPng(MagickImage image, long target, CancellationToken ct)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Expected Obj, but got Unknown
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Expected Obj, but got Unknown
		MagickImage val = (MagickImage)image.Clone();
		byte[] array;
		try
		{
			val.Settings.SetDefine((MagickFormat)191, "compression-level", "9");
			val.Settings.SetDefine((MagickFormat)191, "compression-filter", "5");
			val.Settings.SetDefine((MagickFormat)191, "compression-strategy", "1");
			val.Format = (MagickFormat)191;
			array = val.ToByteArray();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		if (array.LongLength <= target)
		{
			return (array, "Lossless optimization.");
		}
		byte[] array2 = array;
		string text = "Lossless optimization.";
		uint[] array3 = new uint[3] { 256u, 128u, 64u };
		foreach (uint num in array3)
		{
			ct.ThrowIfCancellationRequested();
			MagickImage val2 = (MagickImage)image.Clone();
			try
			{
				val2.Quantize((IQuantizeSettings)new QuantizeSettings
				{
					Colors = num,
					DitherMethod = (DitherMethod)3
				});
				val2.Settings.SetDefine((MagickFormat)191, "compression-level", "9");
				val2.Format = (MagickFormat)197;
				byte[] array4 = val2.ToByteArray();
				if (array4.LongLength < array2.LongLength)
				{
					array2 = array4;
					text = $"Reduced to {num} colors.";
				}
				if (array4.LongLength <= target)
				{
					return (array4, $"Reduced to {num} colors.");
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		return (array2, text + " PNG cannot go lower. Convert to WEBP or JPG for smaller files.");
	}

	private static (byte[], string) CompressTiff(MagickImage image)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		MagickImage val = (MagickImage)image.Clone();
		try
		{
			val.Settings.Compression = (CompressionMethod)20;
			val.Format = (MagickFormat)248;
			return (val.ToByteArray(), "Lossless ZIP compression.");
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public static class ImageConverter
{
	public static readonly string[] Targets = new string[10] { "PNG", "JPG", "WEBP", "HEIC", "TIFF", "AVIF", "BMP", "PDF", "DOCX", "TXT" };

	public static async Task<string> ConvertAsync(string path, string target, CancellationToken ct = default(CancellationToken))
	{
		target = target.ToUpperInvariant();
		string text = target;
		if ((text == "TXT" || text == "DOCX") ? true : false)
		{
			return await OcrExport.ExportAsync(path, target, ct);
		}
		MagickImage image = await Task.Run(() => Prepare(path, target), ct);
		try
		{
			byte[] array;
			string newExtension;
			if (!(target == "HEIC"))
			{
				(array, newExtension) = await Task.Run(() => EncodeWithMagick(image, target), ct);
			}
			else
			{
				array = await WindowsCodecs.EncodeHeicAsync(image, 90, ct);
				newExtension = ".heic";
			}
			string text2 = OutputPaths.ForConvert(path, newExtension);
			SafeFile.WriteNew(text2, array);
			return text2;
		}
		finally
		{
			if (image != null)
			{
				((IDisposable)image).Dispose();
			}
		}
	}

	private static MagickImage Prepare(string path, string target)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		MagickImage val = new MagickImage(path);
		val.AutoOrient();
		bool flag;
		switch (target)
		{
		case "JPG":
		case "BMP":
		case "PDF":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag && val.HasAlpha)
		{
			val.BackgroundColor = (IMagickColor<byte>)(object)MagickColors.White;
			val.Alpha((AlphaAction)12);
		}
		return val;
	}

	private static (byte[], string) EncodeWithMagick(MagickImage image, string target)
	{
		//IL_01d4: Expected Obj, but got Unknown
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Invalid comparison between Unknown and I4
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_0188: Invalid comparison between Unknown and I4
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Invalid comparison between Unknown and I4
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Invalid comparison between Unknown and I4
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		if (target == null)
		{
			goto IL_0161;
		}
		int length = target.Length;
		(MagickFormat, string) tuple;
		if (length != 3)
		{
			if (length != 4)
			{
				goto IL_0161;
			}
			char c = target[0];
			if (c != 'A')
			{
				if (c != 'T')
				{
					if (c != 'W' || !(target == "WEBP"))
					{
						goto IL_0161;
					}
					tuple = ((MagickFormat)268, ".webp");
				}
				else
				{
					if (!(target == "TIFF"))
					{
						goto IL_0161;
					}
					tuple = ((MagickFormat)248, ".tiff");
				}
			}
			else
			{
				if (!(target == "AVIF"))
				{
					goto IL_0161;
				}
				tuple = ((MagickFormat)15, ".avif");
			}
		}
		else
		{
			switch (target[1])
			{
			case 'N':
				break;
			case 'P':
				goto IL_007d;
			case 'M':
				goto IL_008f;
			case 'D':
				goto IL_00a4;
			default:
				goto IL_0161;
			}
			if (!(target == "PNG"))
			{
				goto IL_0161;
			}
			tuple = ((MagickFormat)191, ".png");
		}
		goto IL_0172;
		IL_0172:
		var (val, item) = tuple;
		if (((int)val == 15 || (int)val == 118 || (int)val == 268) ? true : false)
		{
			image.Quality = 90u;
		}
		if ((int)val == 248)
		{
			image.Settings.Compression = (CompressionMethod)20;
		}
		image.Format = val;
		try
		{
			return (image.ToByteArray(), item);
		}
		catch (MagickException ex)
		{
			MagickException ex2 = ex;
			throw new InvalidOperationException("Could not write " + target + ": " + ((Exception)(object)ex2).Message, (Exception?)(object)ex2);
		}
		IL_007d:
		if (!(target == "JPG"))
		{
			goto IL_0161;
		}
		tuple = ((MagickFormat)118, ".jpg");
		goto IL_0172;
		IL_00a4:
		if (!(target == "PDF"))
		{
			goto IL_0161;
		}
		tuple = ((MagickFormat)176, ".pdf");
		goto IL_0172;
		IL_0161:
		throw new NotSupportedException("Unknown format: " + target);
		IL_008f:
		if (!(target == "BMP"))
		{
			goto IL_0161;
		}
		tuple = ((MagickFormat)23, ".bmp");
		goto IL_0172;
	}
}
public static class ImageCropper
{
	public static string Crop(string path, CropBox box)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Expected Obj, but got Unknown
		MagickImage val = new MagickImage(path);
		try
		{
			int quality = ImageSaver.OriginalQuality(val);
			val.AutoOrient();
			var (num, num2, num3, num4) = CropMath.ToPixels(box, (int)val.Width, (int)val.Height);
			val.Crop((IMagickGeometry)new MagickGeometry(num, num2, (uint)num3, (uint)num4));
			val.ResetPage();
			return ImageSaver.SaveNextTo(val, path, "-cropped", quality);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static (int Width, int Height) OrientedSize(string path)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		MagickImage val = new MagickImage(path);
		try
		{
			val.AutoOrient();
			return (Width: (int)val.Width, Height: (int)val.Height);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public sealed record EditSettings(int Brightness = 0, int Contrast = 0, int Saturation = 0, int Rotation = 0, bool FlipHorizontal = false, bool FlipVertical = false)
{
	public bool IsIdentity => this == new EditSettings();

	public EditSettings RotateRight()
	{
		return this with
		{
			Rotation = (Rotation + 90) % 360
		};
	}

	public EditSettings RotateLeft()
	{
		return this with
		{
			Rotation = (Rotation + 270) % 360
		};
	}
}
public static class ImageEditor
{
	public static void Apply(MagickImage image, EditSettings s)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		if (s.Brightness != 0 || s.Contrast != 0)
		{
			image.BrightnessContrast(new Percentage(Math.Clamp(s.Brightness, -100, 100)), new Percentage(Math.Clamp(s.Contrast, -100, 100)));
		}
		if (s.Saturation != 0)
		{
			image.Modulate(new Percentage(100), new Percentage(100 + Math.Clamp(s.Saturation, -100, 100)), new Percentage(100));
		}
		if (s.FlipHorizontal)
		{
			image.Flop();
		}
		if (s.FlipVertical)
		{
			image.Flip();
		}
		if (s.Rotation % 360 != 0)
		{
			image.Rotate((double)(s.Rotation % 360));
		}
	}

	public static string Save(string path, EditSettings s)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		MagickImage val = new MagickImage(path);
		try
		{
			int quality = ImageSaver.OriginalQuality(val);
			val.AutoOrient();
			Apply(val, s);
			return ImageSaver.SaveNextTo(val, path, "-edited", quality);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public sealed record MetaEntry(string Group, string Name, string Value);
public sealed record BasicMetadata(string? Title, string? Author, string? Copyright, string? DateTaken);
public static class ImageMetadata
{
	private const string DateFormat = "yyyy:MM:dd HH:mm:ss";

	public static List<MetaEntry> Read(string path)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		List<MetaEntry> list = new List<MetaEntry>();
		MagickImage val = new MagickImage();
		val.Ping(path);
		MagickImage val2 = val;
		try
		{
			list.Add(new MetaEntry("File", "Format", ((object)val.Format/*cast due to constrained. prefix*/).ToString()));
			list.Add(new MetaEntry("File", "Size", $"{val.Width} × {val.Height} px"));
			list.Add(new MetaEntry("File", "File size", SizeFormat.Format(new FileInfo(path).Length)));
			IExifProfile exifProfile = val.GetExifProfile();
			if (exifProfile != null)
			{
				foreach (IExifValue item in exifProfile.Values.OrderBy((IExifValue v) => ((object)v.Tag).ToString()))
				{
					string text = FormatValue(item.GetValue());
					if (!string.IsNullOrWhiteSpace(text) && text.Length <= 200)
					{
						list.Add(new MetaEntry(((object)item.Tag).ToString().StartsWith("GPS") ? "GPS" : "EXIF", ((object)item.Tag).ToString(), text));
					}
				}
				if (TryGetGps(exifProfile, out var lat, out var lon))
				{
					list.Insert(3, new MetaEntry("GPS", "Location", lat.ToString("0.000000", CultureInfo.InvariantCulture) + ", " + lon.ToString("0.000000", CultureInfo.InvariantCulture)));
				}
			}
			if (val.GetXmpProfile() != null)
			{
				list.Add(new MetaEntry("Other", "XMP", "present"));
			}
			if (val.GetIptcProfile() != null)
			{
				list.Add(new MetaEntry("Other", "IPTC", "present"));
			}
			IColorProfile colorProfile = val.GetColorProfile();
			if (colorProfile != null)
			{
				list.Add(new MetaEntry("Other", "Color profile", colorProfile.Description ?? "present"));
			}
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
		return list;
	}

	public static BasicMetadata ReadBasic(string path)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		MagickImage val = new MagickImage();
		val.Ping(path);
		MagickImage val2 = val;
		try
		{
			IExifProfile exifProfile = val.GetExifProfile();
			return new BasicMetadata(((exifProfile == null) ? null : exifProfile.GetValue<string>(ExifTag.ImageDescription)?.Value) ?? "", ((exifProfile == null) ? null : exifProfile.GetValue<string>(ExifTag.Artist)?.Value) ?? "", ((exifProfile == null) ? null : exifProfile.GetValue<string>(ExifTag.Copyright)?.Value) ?? "", ((exifProfile == null) ? null : exifProfile.GetValue<string>(ExifTag.DateTimeOriginal)?.Value) ?? "");
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
	}

	public static bool HasGps(string path)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		MagickImage val = new MagickImage();
		val.Ping(path);
		MagickImage val2 = val;
		try
		{
			IExifProfile exifProfile = val.GetExifProfile();
			return exifProfile != null && exifProfile.Values.Any((IExifValue v) => ((object)v.Tag).ToString().StartsWith("GPS"));
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
	}

	public static bool CanEdit(string path)
	{
		bool flag;
		switch (Path.GetExtension(path).ToLowerInvariant())
		{
		case ".heic":
		case ".heif":
		case ".bmp":
		case ".gif":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		return !flag;
	}

	public static bool TryNormalizeDate(string text, out string exifDate)
	{
		exifDate = "";
		text = text.Trim();
		if (text.Length == 0)
		{
			return true;
		}
		string[] formats = new string[5] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy:MM:dd" };
		if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			exifDate = result.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
			return true;
		}
		return false;
	}

	public static string Write(string path, BasicMetadata fields, bool removeGps)
	{
		if (!CanEdit(path))
		{
			throw new InvalidOperationException("Editing metadata is not supported for this format. You can remove it instead.");
		}
		string exifDate = fields.DateTaken;
		if (exifDate != null && exifDate.Length > 0 && !TryNormalizeDate(fields.DateTaken, out exifDate))
		{
			throw new ArgumentException("Date taken must look like 2024-05-31 14:20.");
		}
		return Save(path, "-metadata", (ExifProfile exif) =>
		{
			Set(exif, ExifTag.ImageDescription, fields.Title);
			Set(exif, ExifTag.Artist, fields.Author);
			Set(exif, ExifTag.Copyright, fields.Copyright);
			if (fields.DateTaken != null)
			{
				TryNormalizeDate(fields.DateTaken, out string exifDate2);
				Set(exif, ExifTag.DateTimeOriginal, exifDate2);
			}
			if (removeGps)
			{
				RemoveGps(exif);
			}
		}, removeAll: false);
	}

	public static string RemoveAll(string path)
	{
		return Save(path, "-clean", null, removeAll: true);
	}

	private static string Save(string path, string suffix, Action<ExifProfile>? edit, bool removeAll)
	{
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Expected Obj, but got Unknown
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected Obj, but got Unknown
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Invalid comparison between Unknown and I4
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Expected Obj, but got Unknown
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Invalid comparison between Unknown and I4
		//IL_00b7: Expected Obj, but got Unknown
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		byte[] array = File.ReadAllBytes(path);
		if (JpegSegments.IsJpeg(array))
		{
			MagickImage val = new MagickImage();
			val.Ping(array);
			MagickImage val2 = val;
			byte[] newExif;
			try
			{
				IExifProfile exifProfile = val.GetExifProfile();
				if (removeAll)
				{
					ushort num = ((exifProfile == null) ? ((ushort?)null) : exifProfile.GetValue<ushort>(ExifTag.Orientation)?.Value) ?? 1;
					if (num > 1 && num <= 8)
					{
						ExifProfile val3 = new ExifProfile();
						val3.SetValue<ushort>(ExifTag.Orientation, num);
						newExif = ((ImageProfile)val3).ToByteArray();
					}
					else
					{
						newExif = null;
					}
				}
				else
				{
					ExifProfile val4 = ((exifProfile != null) ? new ExifProfile(((IImageProfile)exifProfile).ToByteArray()) : new ExifProfile());
					edit(val4);
					newExif = (val4.Values.Any() ? ((ImageProfile)val4).ToByteArray() : null);
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
			byte[] array2 = JpegSegments.Rewrite(array, newExif, removeAll, removeAll, removeAll);
			string text = OutputPaths.WithSuffix(path, suffix);
			SafeFile.WriteNew(text, array2);
			return text;
		}
		MagickImage val5 = new MagickImage(array);
		try
		{
			int num2 = ImageSaver.OriginalQuality(val5);
			if (removeAll)
			{
				val5.AutoOrient();
				val5.Strip();
			}
			else
			{
				IExifProfile exifProfile2 = val5.GetExifProfile();
				ExifProfile val6 = ((exifProfile2 != null) ? new ExifProfile(((IImageProfile)exifProfile2).ToByteArray()) : new ExifProfile());
				edit(val6);
				val5.SetProfile((IImageProfile)(object)val6);
			}
			MagickFormat format = val5.Format;
			if (((int)format == 15 || (int)format == 268) ? true : false)
			{
				num2 = Math.Max(num2, 95);
			}
			return ImageSaver.SaveNextTo(val5, path, suffix, num2);
		}
		finally
		{
			((IDisposable)val5)?.Dispose();
		}
	}

	private static void Set(ExifProfile exif, ExifTag<string> tag, string? value)
	{
		if (value != null)
		{
			if (value.Trim().Length == 0)
			{
				exif.RemoveValue((ExifTag)(object)tag);
			}
			else
			{
				exif.SetValue<string>(tag, value.Trim());
			}
		}
	}

	private static void RemoveGps(ExifProfile exif)
	{
		foreach (IExifValue item in exif.Values.Where((IExifValue v) => ((object)v.Tag).ToString().StartsWith("GPS")).ToList())
		{
			exif.RemoveValue(item.Tag);
		}
	}

	private static bool TryGetGps(IExifProfile exif, out double lat, out double lon)
	{
		lat = (lon = 0.0);
		Rational[] array = exif.GetValue<Rational[]>(ExifTag.GPSLatitude)?.Value;
		Rational[] array2 = exif.GetValue<Rational[]>(ExifTag.GPSLongitude)?.Value;
		if (array == null || array.Length != 3 || array2 == null || array2.Length != 3)
		{
			return false;
		}
		lat = Dms(array) * (double)((!(exif.GetValue<string>(ExifTag.GPSLatitudeRef)?.Value == "S")) ? 1 : (-1));
		lon = Dms(array2) * (double)((!(exif.GetValue<string>(ExifTag.GPSLongitudeRef)?.Value == "W")) ? 1 : (-1));
		return true;
		static double Dms(Rational[] r)
		{
			return r[0].ToDouble() + r[1].ToDouble() / 60.0 + r[2].ToDouble() / 3600.0;
		}
	}

	private static string FormatValue(object? v)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		if (v != null)
		{
			if (!(v is string text))
			{
				if (!(v is Rational val))
				{
					if (!(v is SignedRational val2))
					{
						if (!(v is Rational[] source))
						{
							if (!(v is byte[]))
							{
								if (v is Array source2)
								{
									return string.Join(", ", source2.Cast<object>());
								}
								return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
							}
							return "";
						}
						return string.Join(", ", source.Select((Rational r) => r.ToDouble().ToString("0.####", CultureInfo.InvariantCulture)));
					}
					return val2.ToDouble().ToString("0.####", CultureInfo.InvariantCulture);
				}
				return val.ToDouble().ToString("0.####", CultureInfo.InvariantCulture);
			}
			return text.Trim(new char[2] { '\0', ' ' });
		}
		return "";
	}
}
public static class ImageRedactor
{
	public static void Apply(MagickImage image, IEnumerable<CropBox> boxes)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected Obj, but got Unknown
		foreach (CropBox box in boxes)
		{
			(int X, int Y, int W, int H) tuple = CropMath.ToPixels(box, (int)image.Width, (int)image.Height);
			int item = tuple.X;
			int item2 = tuple.Y;
			int item3 = tuple.W;
			int item4 = tuple.H;
			MagickImage val = new MagickImage((IMagickColor<byte>)(object)MagickColors.Black, (uint)item3, (uint)item4);
			try
			{
				image.Composite((IMagickImage)(object)val, item, item2, (CompositeOperator)13);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	public static string Save(string path, IReadOnlyList<CropBox> boxes)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected Obj, but got Unknown
		if (boxes.Count == 0)
		{
			throw new ArgumentException("Draw at least one box first.");
		}
		MagickImage val = new MagickImage(path);
		try
		{
			int quality = ImageSaver.OriginalQuality(val);
			val.AutoOrient();
			Apply(val, boxes);
			IColorProfile colorProfile = val.GetColorProfile();
			val.Strip();
			if (colorProfile != null)
			{
				val.SetProfile(colorProfile);
			}
			return ImageSaver.SaveNextTo(val, path, "-redacted", quality);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
public static class ImageSaver
{
	public static string SaveNextTo(MagickImage image, string source, string suffix, int quality = 92)
	{
		string text = OutputPaths.WithSuffix(source, suffix);
		byte[] array;
		try
		{
			array = Encode(image, quality);
		}
		catch (Exception ex) when ((ex is MagickException || ex is InvalidOperationException) ? true : false)
		{
			image.Format = (MagickFormat)191;
			array = image.ToByteArray();
			text = OutputPaths.WithSuffix(source, suffix, ".png");
		}
		SafeFile.WriteNew(text, array);
		return text;
	}

	public static byte[] Encode(MagickImage image, int quality = 92)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Invalid comparison between Unknown and I4
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Invalid comparison between Unknown and I4
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Invalid comparison between Unknown and I4
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Invalid comparison between Unknown and I4
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Invalid comparison between Unknown and I4
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Invalid comparison between Unknown and I4
		MagickFormat format = image.Format;
		if (format - 93 <= 1)
		{
			return WindowsCodecs.EncodeHeicAsync(image, quality).GetAwaiter().GetResult();
		}
		format = image.Format;
		if (((int)format == 15 || (int)format == 118 || (int)format == 268) ? true : false)
		{
			image.Quality = (uint)quality;
		}
		format = image.Format;
		bool flag = (((int)format == 23 || (int)format == 118) ? true : false);
		if (flag && image.HasAlpha)
		{
			image.BackgroundColor = (IMagickColor<byte>)(object)MagickColors.White;
			image.Alpha((AlphaAction)12);
		}
		return image.ToByteArray();
	}

	public static int OriginalQuality(MagickImage image)
	{
		uint quality = image.Quality;
		if (quality < 50 || quality > 100)
		{
			return 92;
		}
		return (int)image.Quality;
	}
}
public sealed class InvariantCulture : IDisposable
{
	private readonly CultureInfo _culture;

	private readonly CultureInfo _ui;

	public InvariantCulture()
	{
		_culture = CultureInfo.CurrentCulture;
		_ui = CultureInfo.CurrentUICulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
	}

	public void Dispose()
	{
		CultureInfo.CurrentCulture = _culture;
		CultureInfo.CurrentUICulture = _ui;
	}
}
public static class JpegSegments
{
	private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

	private static readonly byte[] XmpHeader = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");

	public static bool IsJpeg(ReadOnlySpan<byte> data)
	{
		if (data.Length > 3 && data[0] == byte.MaxValue)
		{
			return data[1] == 216;
		}
		return false;
	}

	public static byte[] Rewrite(byte[] jpeg, byte[]? newExif, bool dropXmp, bool dropIptc, bool dropComments)
	{
		if (!IsJpeg(jpeg))
		{
			throw new InvalidDataException("Not a JPEG file.");
		}
		MemoryStream outp = new MemoryStream(jpeg.Length + (newExif?.Length ?? 0) + 16);
		bool exifWritten;
		try
		{
			outp.Write(jpeg, 0, 2);
			exifWritten = false;
			int num = 2;
			while (num < jpeg.Length)
			{
				if (jpeg[num] != byte.MaxValue)
				{
					throw new InvalidDataException("Broken JPEG structure.");
				}
				byte b = jpeg[num + 1];
				switch (b)
				{
				case byte.MaxValue:
					num++;
					continue;
				default:
					WriteExif();
					break;
				case 224:
					break;
				}
				if (b == 218 || b == 217)
				{
					outp.Write(jpeg, num, jpeg.Length - num);
					break;
				}
				bool flag;
				switch (b)
				{
				case 1:
				case 208:
				case 209:
				case 210:
				case 211:
				case 212:
				case 213:
				case 214:
				case 215:
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					outp.Write(jpeg, num, 2);
					num += 2;
					continue;
				}
				int num2 = (jpeg[num + 2] << 8) | jpeg[num + 3];
				Span<byte> span = jpeg.AsSpan(num + 4, Math.Max(0, num2 - 2));
				if ((b != 225 || !span.StartsWith(ExifHeader)) && (!((b == 225) & dropXmp) || !span.StartsWith(XmpHeader)) && !((b == 237) & dropIptc) && !((b == 254) & dropComments))
				{
					outp.Write(jpeg, num, num2 + 2);
				}
				num += num2 + 2;
			}
			return outp.ToArray();
		}
		finally
		{
			if (outp != null)
			{
				((IDisposable)outp).Dispose();
			}
		}
		void WriteExif()
		{
			if (exifWritten || newExif == null)
			{
				exifWritten = true;
			}
			else
			{
				byte[] array = (newExif.AsSpan().StartsWith(ExifHeader) ? newExif : ExifHeader.Concat(newExif).ToArray());
				if (array.Length + 2 > 65535)
				{
					throw new InvalidDataException("EXIF data is too big.");
				}
				outp.WriteByte(byte.MaxValue);
				outp.WriteByte(225);
				outp.WriteByte((byte)(array.Length + 2 >> 8));
				outp.WriteByte((byte)((array.Length + 2) & 0xFF));
				outp.Write(array);
				exifWritten = true;
			}
		}
	}

	public static byte[] ImageData(byte[] jpeg)
	{
		int num = 2;
		while (num < jpeg.Length - 1 && jpeg[num] == byte.MaxValue)
		{
			bool flag;
			switch (jpeg[num + 1])
			{
			case byte.MaxValue:
				num++;
				continue;
			case 218:
				return jpeg[num..];
			case 1:
			case 208:
			case 209:
			case 210:
			case 211:
			case 212:
			case 213:
			case 214:
			case 215:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			num = ((!flag) ? (num + (((jpeg[num + 2] << 8) | jpeg[num + 3]) + 2)) : (num + 2));
		}
		return Array.Empty<byte>();
	}
}
public static class OcrExport
{
	public static async Task<string> ExportAsync(string path, string target, CancellationToken ct = default(CancellationToken))
	{
		List<string> lines = await RecognizeAsync(path, ct);
		if (lines.Count == 0)
		{
			throw new InvalidOperationException("No text was found in this image.");
		}
		string newExtension = ((target == "DOCX") ? ".docx" : ".txt");
		string text = OutputPaths.ForConvert(path, newExtension);
		if (target == "DOCX")
		{
			SafeFile.WriteNew(text, (Stream s) =>
			{
				DocxWriter.Write(s, new List<string>[1] { lines });
			});
		}
		else
		{
			SafeFile.WriteNew(text, Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines)));
		}
		return text;
	}

	public static async Task<List<string>> RecognizeAsync(string path, CancellationToken ct = default(CancellationToken))
	{
		return await RecognizePngAsync(await Task.Run(() =>
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Expected Obj, but got Unknown
			MagickImage val = new MagickImage(path);
			try
			{
				val.AutoOrient();
				val.Format = (MagickFormat)191;
				return val.ToByteArray();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}, ct), ct);
	}

	public static async Task<List<string>> RecognizePngAsync(byte[] imageBytes, CancellationToken ct = default(CancellationToken))
	{
		OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Windows OCR is not available. Add a language with OCR in Settings > Time & language > Language.");
		SoftwareBitmap bitmap = await WindowsCodecs.ToSoftwareBitmapAsync(await Task.Run(() =>
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Expected Obj, but got Unknown
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Expected Obj, but got Unknown
			MagickImage val = new MagickImage(imageBytes);
			try
			{
				uint maxImageDimension = OcrEngine.MaxImageDimension;
				if (val.Width > maxImageDimension || val.Height > maxImageDimension)
				{
					val.Resize((IMagickGeometry)new MagickGeometry(maxImageDimension, maxImageDimension));
				}
				val.Format = (MagickFormat)191;
				return val.ToByteArray();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}, ct), ct);
		try
		{
			return (await WindowsRuntimeSystemExtensions.AsTask<OcrResult>(engine.RecognizeAsync(bitmap), ct)).Lines.Select((OcrLine l) => l.Text).ToList();
		}
		finally
		{
			((IDisposable)bitmap)?.Dispose();
		}
	}
}
public static class OutputPaths
{
	public static string WithSuffix(string source, string suffix, string? newExtension = null)
	{
		string? path = Path.GetDirectoryName(source) ?? ".";
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(source);
		string text = newExtension ?? Path.GetExtension(source);
		if (!text.StartsWith('.'))
		{
			text = "." + text;
		}
		return Unique(Path.Combine(path, fileNameWithoutExtension + suffix + text));
	}

	public static string ForConvert(string source, string newExtension)
	{
		newExtension = newExtension.ToLowerInvariant();
		if (!newExtension.StartsWith('.'))
		{
			newExtension = "." + newExtension;
		}
		bool flag = string.Equals(Path.GetExtension(source), newExtension, StringComparison.OrdinalIgnoreCase);
		return WithSuffix(source, flag ? "-converted" : "", newExtension);
	}

	public static string Unique(string candidate)
	{
		if (!File.Exists(candidate) && !Directory.Exists(candidate))
		{
			return candidate;
		}
		string path = Path.GetDirectoryName(candidate) ?? ".";
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(candidate);
		string extension = Path.GetExtension(candidate);
		int num = 2;
		string text;
		while (true)
		{
			text = Path.Combine(path, $"{fileNameWithoutExtension} ({num}){extension}");
			if (!File.Exists(text) && !Directory.Exists(text))
			{
				break;
			}
			num++;
		}
		return text;
	}
}
public static class PdfCompressor
{
	private static readonly (int Quality, double Scale)[] Ladder = new (int, double)[6]
	{
		(80, 1.0),
		(65, 1.0),
		(55, 0.75),
		(45, 0.6),
		(35, 0.5),
		(25, 0.35)
	};

	public static CompressResult Compress(string path, long targetBytes, CancellationToken ct = default(CancellationToken))
	{
		long length = new FileInfo(path).Length;
		byte[] array = null;
		string value = "";
		(int, double)[] ladder = Ladder;
		for (int i = 0; i < ladder.Length; i++)
		{
			var (num, num2) = ladder[i];
			ct.ThrowIfCancellationRequested();
			(byte[] Bytes, int Changed) tuple2 = Rewrite(path, num, num2);
			var (array2, _) = tuple2;
			if (tuple2.Changed == 0)
			{
				return new CompressResult(path, null, length, length, ReachedTarget: false, "No photos found inside this PDF that can be compressed. Text-only PDFs cannot shrink much.");
			}
			if (array == null || array2.LongLength < array.LongLength)
			{
				array = array2;
				value = $"Images: quality {num}" + ((num2 < 1.0) ? $", {num2:P0} size." : ".");
			}
			if (array2.LongLength <= targetBytes)
			{
				break;
			}
		}
		if (array == null || array.LongLength >= length)
		{
			return new CompressResult(path, null, length, length, ReachedTarget: false, "Could not make this PDF smaller.");
		}
		string text = OutputPaths.WithSuffix(path, "-compressed");
		SafeFile.WriteNew(text, array);
		bool flag = array.LongLength <= targetBytes;
		string message = (flag ? $"{SizeFormat.Format(length)} → {SizeFormat.Format(array.LongLength)}. {value}" : $"Could not reach {SizeFormat.Format(targetBytes)}. Smallest possible: {SizeFormat.Format(array.LongLength)}. {value}");
		return new CompressResult(path, text, length, array.LongLength, flag, message);
	}

	private static (byte[] Bytes, int Changed) Rewrite(string path, int quality, double scale)
	{
		using (new InvariantCulture())
		{
			PdfDocument val = PdfReader.Open(path, (PdfDocumentOpenMode)0, (PdfReaderOptions)null);
			try
			{
				PdfObject[] allObjects = val.Internals.GetAllObjects();
				HashSet<PdfObject> hashSet = new HashSet<PdfObject>();
				PdfObject[] array = allObjects;
				foreach (PdfObject obj in array)
				{
					PdfDictionary val2 = (PdfDictionary)(object)((obj is PdfDictionary) ? obj : null);
					if (val2 != null && IsImage(val2))
					{
						PdfReference reference = val2.Elements.GetReference("/SMask");
						PdfObject val3 = ((reference != null) ? reference.Value : null);
						if (val3 != null)
						{
							hashSet.Add(val3);
						}
						PdfReference reference2 = val2.Elements.GetReference("/Mask");
						PdfObject val4 = ((reference2 != null) ? reference2.Value : null);
						if (val4 != null)
						{
							hashSet.Add(val4);
						}
					}
				}
				int num = 0;
				array = allObjects;
				foreach (PdfObject obj2 in array)
				{
					PdfDictionary val5 = (PdfDictionary)(object)((obj2 is PdfDictionary) ? obj2 : null);
					if (val5 == null || !IsImage(val5) || val5.Stream == null || hashSet.Contains((PdfObject)(object)val5))
					{
						continue;
					}
					bool flag = val5.Elements.ContainsKey("/SMask") || val5.Elements.ContainsKey("/Mask");
					try
					{
						if (TryReencode(val5, quality, flag ? 1.0 : scale))
						{
							num++;
						}
					}
					catch
					{
					}
				}
				val.Options.CompressContentStreams = true;
				val.Options.NoCompression = false;
				using MemoryStream memoryStream = new MemoryStream();
				val.Save((Stream)memoryStream, false);
				return (Bytes: memoryStream.ToArray(), Changed: num);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	private static bool IsImage(PdfDictionary d)
	{
		return d.Elements.GetName("/Subtype") == "/Image";
	}

	private static bool TryReencode(PdfDictionary d, int quality, double scale)
	{
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected Obj, but got Unknown
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Invalid comparison between Unknown and I4
		//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cd: Invalid comparison between Unknown and I4
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0188: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b2: Expected Obj, but got Unknown
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Expected Obj, but got Unknown
		if (d.Elements.GetBoolean("/ImageMask"))
		{
			return false;
		}
		if (d.Elements.ContainsKey("/Decode"))
		{
			return false;
		}
		string singleFilter = GetSingleFilter(d);
		PdfItem val = d.Elements["/ColorSpace"];
		PdfName val2 = (PdfName)(object)((val is PdfName) ? val : null);
		string text = ((val2 != null) ? val2.Value : "");
		int integer = d.Elements.GetInteger("/Width");
		int integer2 = d.Elements.GetInteger("/Height");
		if (integer < 64 || integer2 < 64)
		{
			return false;
		}
		MagickImage val4;
		switch (singleFilter)
		{
		case "/DCTDecode":
			if (text == "/DeviceCMYK")
			{
				return false;
			}
			val4 = new MagickImage(d.Stream.Value);
			if ((int)val4.ColorSpace == 2)
			{
				val4.Dispose();
				return false;
			}
			break;
		case "/FlateDecode":
		case "":
		{
			int num = ((text == "/DeviceRGB") ? 3 : ((text == "/DeviceGray") ? 1 : 0));
			int num2 = num;
			if (num2 == 0 || d.Elements.GetInteger("/BitsPerComponent") != 8)
			{
				return false;
			}
			if (d.Elements.ContainsKey("/DecodeParms"))
			{
				return false;
			}
			byte[] array = ((singleFilter == "") ? d.Stream.Value : d.Stream.UnfilteredValue);
			if (array.Length < integer * integer2 * num2)
			{
				return false;
			}
			MagickReadSettings val3 = new MagickReadSettings
			{
				Width = (uint)integer,
				Height = (uint)integer2,
				Depth = 8u,
				Format = (MagickFormat)((num2 == 3) ? 214 : 88)
			};
			val4 = new MagickImage(array, (IMagickReadSettings<byte>)(object)val3);
			break;
		}
		default:
			return false;
		}
		MagickImage val5 = val4;
		try
		{
			bool flag = (int)val4.ColorSpace == 3 || text == "/DeviceGray";
			if (scale < 1.0 && Math.Max(integer, integer2) > 600)
			{
				val4.Resize(new Percentage(scale * 100.0));
			}
			val4.Strip();
			val4.Quality = (uint)quality;
			val4.Format = (MagickFormat)118;
			byte[] array2 = val4.ToByteArray();
			if (array2.Length >= d.Stream.Value.Length)
			{
				return false;
			}
			d.Stream.Value = array2;
			d.Elements.SetName("/Filter", "/DCTDecode");
			d.Elements.Remove("/DecodeParms");
			d.Elements.SetInteger("/Width", (int)val4.Width);
			d.Elements.SetInteger("/Height", (int)val4.Height);
			d.Elements.SetInteger("/BitsPerComponent", 8);
			if (d.Elements["/ColorSpace"] is PdfName)
			{
				d.Elements.SetName("/ColorSpace", flag ? "/DeviceGray" : "/DeviceRGB");
			}
			d.Elements.SetInteger("/Length", array2.Length);
			return true;
		}
		finally
		{
			((IDisposable)val5)?.Dispose();
		}
	}

	private static string GetSingleFilter(PdfDictionary d)
	{
		PdfItem val = d.Elements["/Filter"];
		PdfName val2 = (PdfName)(object)((val is PdfName) ? val : null);
		if (val2 != null)
		{
			return val2.Value;
		}
		PdfArray val3 = (PdfArray)(object)((val is PdfArray) ? val : null);
		if (val3 != null && val3.Elements.Count == 1)
		{
			PdfItem val4 = val3.Elements[0];
			PdfName val5 = (PdfName)(object)((val4 is PdfName) ? val4 : null);
			if (val5 != null)
			{
				return val5.Value;
			}
		}
		if (val == null)
		{
			return "";
		}
		return "?";
	}
}
public sealed record PdfConvertResult(IReadOnlyList<string> Outputs, string Note);
public static class PdfConverter
{
	public static readonly string[] Targets = new string[4] { "DOCX", "TXT", "JPG", "PNG" };

	private const double RenderDpi = 200.0;

	public static async Task<PdfConvertResult> ConvertAsync(string path, string target, CancellationToken ct = default(CancellationToken))
	{
		target = target.ToUpperInvariant();
		PdfConvertResult result;
		switch (target)
		{
		case "TXT":
		case "DOCX":
			result = await ToTextAsync(path, target, ct);
			break;
		case "JPG":
		case "PNG":
			result = await ToImagesAsync(path, target, ct);
			break;
		default:
			throw new NotSupportedException("Unknown format: " + target);
		}
		return result;
	}

	private static async Task<PdfConvertResult> ToTextAsync(string path, string target, CancellationToken ct)
	{
		List<IReadOnlyList<string>> pages = await Task.Run(() => ExtractText(path), ct);
		string note = "";
		if (pages.All((IReadOnlyList<string> p) => p.All(string.IsNullOrWhiteSpace)))
		{
			pages = new List<IReadOnlyList<string>>();
			foreach (byte[] item in await RenderPagesAsync(path, ct))
			{
				List<IReadOnlyList<string>> list = pages;
				list.Add(await OcrExport.RecognizePngAsync(item, ct));
			}
			note = "This PDF had no text layer (scan?), so Windows OCR was used.";
			if (pages.All((IReadOnlyList<string> p) => p.Count == 0))
			{
				throw new InvalidOperationException("No text was found in this PDF.");
			}
		}
		string text = OutputPaths.ForConvert(path, (target == "DOCX") ? ".docx" : ".txt");
		if (target == "DOCX")
		{
			SafeFile.WriteNew(text, (Stream stream) =>
			{
				DocxWriter.Write(stream, pages);
			});
		}
		else
		{
			string s = string.Join(Environment.NewLine + Environment.NewLine, pages.Select((IReadOnlyList<string> p) => string.Join(Environment.NewLine, p)));
			SafeFile.WriteNew(text, Encoding.UTF8.GetBytes(s));
		}
		return new PdfConvertResult(new string[1] { text }, note);
	}

	private static List<IReadOnlyList<string>> ExtractText(string path)
	{
		PdfDocument val = PdfDocument.Open(path, (ParsingOptions)null);
		try
		{
			List<IReadOnlyList<string>> list = new List<IReadOnlyList<string>>();
			foreach (Page page in val.GetPages())
			{
				string text = ContentOrderTextExtractor.GetText(page, false);
				list.Add((from l in text.Replace("\r", "").Split('\n')
					select l.TrimEnd()).ToList());
			}
			return list;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static async Task<PdfConvertResult> ToImagesAsync(string path, string target, CancellationToken ct)
	{
		List<byte[]> list = await RenderPagesAsync(path, ct);
		string text = ((target == "JPG") ? ".jpg" : ".png");
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		if (list.Count == 1)
		{
			string text2 = OutputPaths.ForConvert(path, text);
			SafeFile.WriteNew(text2, Encode(list[0], target));
			return new PdfConvertResult(new string[1] { text2 }, "");
		}
		string text3 = OutputPaths.Unique(Path.Combine(Path.GetDirectoryName(path), fileNameWithoutExtension + "-pages"));
		Directory.CreateDirectory(text3);
		int totalWidth = Math.Max(2, list.Count.ToString().Length);
		for (int i = 0; i < list.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			SafeFile.WriteNew(Path.Combine(text3, fileNameWithoutExtension + "-p" + (i + 1).ToString().PadLeft(totalWidth, '0') + text), Encode(list[i], target));
		}
		return new PdfConvertResult(new string[1] { text3 }, $"{list.Count} pages saved in a new folder.");
	}

	private static byte[] Encode(byte[] png, string target)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected Obj, but got Unknown
		if (target == "PNG")
		{
			return png;
		}
		MagickImage val = new MagickImage(png);
		try
		{
			val.BackgroundColor = (IMagickColor<byte>)(object)MagickColors.White;
			val.Alpha((AlphaAction)12);
			val.Quality = 90u;
			val.Format = (MagickFormat)118;
			return val.ToByteArray();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static Task<List<byte[]>> RenderPagesAsync(string path, CancellationToken ct = default(CancellationToken))
	{
		return RenderPagesAsync(path, 200.0, ct);
	}

	public static async Task<List<byte[]>> RenderPagesAsync(string path, double dpi, CancellationToken ct = default(CancellationToken))
	{
		StorageFile val = await WindowsRuntimeSystemExtensions.AsTask<StorageFile>(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)), ct);
		PdfDocument pdf;
		try
		{
			pdf = await WindowsRuntimeSystemExtensions.AsTask<PdfDocument>(PdfDocument.LoadFromFileAsync((IStorageFile)(object)val), ct);
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException("Could not open this PDF (is it password protected?).", innerException);
		}
		List<byte[]> result = new List<byte[]>();
		for (uint i = 0u; i < pdf.PageCount; i++)
		{
			ct.ThrowIfCancellationRequested();
			PdfPage page = pdf.GetPage(i);
			try
			{
				InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
				try
				{
					PdfPageRenderOptions val2 = new PdfPageRenderOptions();
					Size size = page.Size;
					val2.DestinationWidth = (uint)Math.Max(1.0, Math.Round(size.Width * dpi / 96.0));
					val2.BackgroundColor = Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
					PdfPageRenderOptions val3 = val2;
					await WindowsRuntimeSystemExtensions.AsTask(page.RenderToStreamAsync((IRandomAccessStream)(object)stream, val3), ct);
					byte[] bytes = new byte[stream.Size];
					stream.Seek(0uL);
					await WindowsRuntimeSystemExtensions.AsTask<IBuffer, uint>(stream.ReadAsync(WindowsRuntimeBufferExtensions.AsBuffer(bytes), (uint)bytes.Length, (InputStreamOptions)0), ct);
					result.Add(bytes);
				}
				finally
				{
					((IDisposable)stream)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)page)?.Dispose();
			}
		}
		return result;
	}
}
public sealed record PdfInfoFields(string Title, string Author, string Subject, string Keywords);
public static class PdfMetadata
{
	public static List<MetaEntry> Read(string path)
	{
		using (new InvariantCulture())
		{
			PdfDocument val = PdfReader.Open(path, (PdfDocumentOpenMode)1, (PdfReaderOptions)null);
			try
			{
				List<MetaEntry> list = new List<MetaEntry>
				{
					new MetaEntry("File", "Pages", val.PageCount.ToString()),
					new MetaEntry("File", "PDF version", $"{val.Version / 10}.{val.Version % 10}"),
					new MetaEntry("File", "File size", SizeFormat.Format(new FileInfo(path).Length))
				};
				foreach (string item in ((PdfDictionary)val.Info).Elements.Keys.OrderBy((string k) => k))
				{
					string value = ((PdfDictionary)val.Info).Elements.GetString(item);
					if (!string.IsNullOrWhiteSpace(value))
					{
						list.Add(new MetaEntry("Info", item.TrimStart('/'), value));
					}
				}
				if (((PdfDictionary)val.Internals.Catalog).Elements.ContainsKey("/Metadata"))
				{
					list.Add(new MetaEntry("Other", "XMP", "present"));
				}
				return list;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	public static PdfInfoFields ReadFields(string path)
	{
		using (new InvariantCulture())
		{
			PdfDocument val = PdfReader.Open(path, (PdfDocumentOpenMode)1, (PdfReaderOptions)null);
			try
			{
				return new PdfInfoFields(val.Info.Title, val.Info.Author, val.Info.Subject, val.Info.Keywords);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	public static string Write(string path, PdfInfoFields f)
	{
		return Save(path, "-metadata", (PdfDocument doc) =>
		{
			doc.Info.Title = f.Title.Trim();
			doc.Info.Author = f.Author.Trim();
			doc.Info.Subject = f.Subject.Trim();
			doc.Info.Keywords = f.Keywords.Trim();
			((PdfDictionary)doc.Internals.Catalog).Elements.Remove("/Metadata");
		});
	}

	public static string RemoveAll(string path)
	{
		return Save(path, "-clean", (PdfDocument doc) =>
		{
			((PdfDictionary)doc.Info).Elements.Clear();
			((PdfDictionary)doc.Internals.Catalog).Elements.Remove("/Metadata");
		});
	}

	private static string Save(string path, string suffix, Action<PdfDocument> change)
	{
		using (new InvariantCulture())
		{
			PdfDocument val = PdfReader.Open(path, (PdfDocumentOpenMode)0, (PdfReaderOptions)null);
			try
			{
				change(val);
				using MemoryStream memoryStream = new MemoryStream();
				val.Save((Stream)memoryStream, false);
				string text = OutputPaths.WithSuffix(path, suffix);
				SafeFile.WriteNew(text, memoryStream.ToArray());
				return text;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}
}
public static class PageRanges
{
	public static List<(int From, int To)> Parse(string text, int pageCount)
	{
		List<(int, int)> list = new List<(int, int)>();
		string[] array = text.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text2 in array)
		{
			string text3 = text2.Replace(" ", "").Replace('–', '-');
			int num;
			int num2;
			if (text3.Contains('-'))
			{
				string[] array2 = text3.Split('-', 2);
				num = ((array2[0].Length == 0) ? 1 : ParsePage(array2[0], text3));
				num2 = ((array2[1].Length == 0) ? pageCount : ParsePage(array2[1], text3));
			}
			else
			{
				num = (num2 = ParsePage(text3, text3));
			}
			if (num < 1 || num2 > pageCount)
			{
				throw new FormatException($"\"{text2}\": this PDF has pages 1 to {pageCount}.");
			}
			if (num > num2)
			{
				throw new FormatException("\"" + text2 + "\": the first page must be smaller than the last.");
			}
			list.Add((num, num2));
		}
		if (list.Count == 0)
		{
			throw new FormatException("Type page ranges, for example: 1-3, 5, 8-10");
		}
		return list;
	}

	private static int ParsePage(string s, string part)
	{
		if (!int.TryParse(s, out var result))
		{
			throw new FormatException("\"" + part + "\" is not a page number or range.");
		}
		return result;
	}

	public static List<(int From, int To)> EveryN(int n, int pageCount)
	{
		if (n < 1)
		{
			throw new ArgumentOutOfRangeException("n");
		}
		List<(int, int)> list = new List<(int, int)>();
		for (int i = 1; i <= pageCount; i += n)
		{
			list.Add((i, Math.Min(i + n - 1, pageCount)));
		}
		return list;
	}
}
public sealed record PageOp(int SourceIndex, int Rotation = 0);
public static class PdfPages
{
	public static int Count(string path)
	{
		using (new InvariantCulture())
		{
			PdfDocument val = Open(path, (PdfDocumentOpenMode)1);
			try
			{
				return val.PageCount;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	public static string Split(string path, IReadOnlyList<(int From, int To)> ranges)
	{
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Expected Obj, but got Unknown
		using (new InvariantCulture())
		{
			PdfDocument val = Open(path, (PdfDocumentOpenMode)1);
			try
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
				string directoryName = Path.GetDirectoryName(path);
				string text = directoryName;
				if (ranges.Count > 1)
				{
					text = OutputPaths.Unique(Path.Combine(directoryName, fileNameWithoutExtension + "-split"));
					Directory.CreateDirectory(text);
				}
				string text2 = null;
				foreach (var range in ranges)
				{
					int item = range.From;
					int item2 = range.To;
					PdfDocument part = new PdfDocument();
					try
					{
						part.Info.Title = val.Info.Title;
						for (int i = item; i <= item2; i++)
						{
							part.AddPage(val.Pages[i - 1]);
						}
						string text3 = ((item == item2) ? $"p{item}" : $"p{item}-{item2}");
						string text4 = OutputPaths.Unique(Path.Combine(text, fileNameWithoutExtension + "-" + text3 + ".pdf"));
						SafeFile.WriteNew(text4, (Stream s) =>
						{
							part.Save(s, false);
						});
						text2 = text4;
					}
					finally
					{
						if (part != null)
						{
							((IDisposable)part).Dispose();
						}
					}
				}
				return (ranges.Count > 1) ? text : text2;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	public static string Organize(string path, IReadOnlyList<PageOp> pages)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected Obj, but got Unknown
		if (pages.Count == 0)
		{
			throw new ArgumentException("A PDF needs at least one page.");
		}
		using (new InvariantCulture())
		{
			PdfDocument val = Open(path, (PdfDocumentOpenMode)1);
			try
			{
				PdfDocument dst = new PdfDocument();
				try
				{
					dst.Info.Title = val.Info.Title;
					dst.Info.Author = val.Info.Author;
					foreach (PageOp page in pages)
					{
						PdfPage val2 = dst.AddPage(val.Pages[page.SourceIndex]);
						int rotate = ((val2.Rotate + page.Rotation) % 360 + 360) % 360;
						val2.Rotate = rotate;
					}
					string text = OutputPaths.WithSuffix(path, "-organized");
					SafeFile.WriteNew(text, (Stream s) =>
					{
						dst.Save(s, false);
					});
					return text;
				}
				finally
				{
					if (dst != null)
					{
						((IDisposable)dst).Dispose();
					}
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}

	private static PdfDocument Open(string path, PdfDocumentOpenMode mode)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		PdfReaderException ex = default;
		try
		{
			return PdfReader.Open(path, mode, (PdfReaderOptions)null);
		}
		catch (object obj) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			object obj2 = ((obj is PdfReaderException) ? obj : null);
			Unsafe.SkipInit(out int result);
			if (obj2 == null)
			{
				result = 0;
			}
			else
			{
				ex = (PdfReaderException)obj2;
				result = (((Exception)(object)ex).Message.Contains("password", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
			}
			return (byte)result != 0;
		}).Invoke())
		{
			throw new InvalidOperationException("This PDF is password protected.", (Exception?)(object)ex);
		}
	}
}
public sealed record CodeHit(int Page, string Format, string Text)
{
	public Uri? Link
	{
		get
		{
			if (!WebLinks.TryGetWebLink(Text, out Uri link))
			{
				return null;
			}
			return link;
		}
	}

	public bool IsLink => Link != null;
}
public static class QrReader
{
	private static BarcodeReaderGeneric NewReader()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Expected Obj, but got Unknown
		BarcodeReaderGeneric val = new BarcodeReaderGeneric();
		val.AutoRotate = true;
		DecodingOptions val2 = new DecodingOptions();
		val2.TryHarder = true;
		val2.TryInverted = true;
		BarcodeFormat[] array = new BarcodeFormat[4];
		RuntimeHelpers.InitializeArray(array, __ldtoken(<PrivateImplementationDetails>.36B0E2D23C84586AB16C34168DB5DB44429C3724A1ECCE4EF79F0F459463C049));
		val2.PossibleFormats = array;
		val.Options = val2;
		return val;
	}

	public static async Task<List<CodeHit>> ReadFileAsync(string path, CancellationToken ct = default(CancellationToken))
	{
		if (FileKinds.Detect(path) != FileKind.Pdf)
		{
			return await Task.Run(() =>
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				//IL_000c: Expected Obj, but got Unknown
				MagickImage val = new MagickImage(path);
				try
				{
					val.AutoOrient();
					return Read(val, 0);
				}
				finally
				{
					((IDisposable)val)?.Dispose();
				}
			}, ct);
		}
		double[] array = new double[2] { 200.0, 330.0 };
		foreach (double dpi in array)
		{
			List<byte[]> pages = await PdfConverter.RenderPagesAsync(path, dpi, ct);
			List<CodeHit> list = await Task.Run(() =>
			{
				//IL_0026: Unknown result type (might be due to invalid IL or missing references)
				//IL_002c: Expected Obj, but got Unknown
				List<CodeHit> list2 = new List<CodeHit>();
				for (int i = 0; i < pages.Count; i++)
				{
					ct.ThrowIfCancellationRequested();
					MagickImage val = new MagickImage(pages[i]);
					try
					{
						list2.AddRange(Read(val, i + 1));
					}
					finally
					{
						((IDisposable)val)?.Dispose();
					}
				}
				return list2;
			}, ct);
			if (list.Count > 0)
			{
				return list;
			}
		}
		return new List<CodeHit>();
	}

	public static List<CodeHit> Read(MagickImage image, int page)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		int width = (int)image.Width;
		int height = (int)image.Height;
		RGBLuminanceSource val = new RGBLuminanceSource(((IPixelCollection<byte>)(object)image.GetPixelsUnsafe()).ToByteArray((PixelMapping)5), width, height, (BitmapFormat)8);
		return (NewReader().DecodeMultiple((LuminanceSource)(object)val) ?? Array.Empty<Result>()).Select((Result r) =>
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			return new CodeHit(page, ((object)r.BarcodeFormat/*cast due to constrained. prefix*/).ToString().Replace("_", " "), r.Text);
		}).DistinctBy((CodeHit r) => (Page: r.Page, Text: r.Text)).ToList();
	}
}
public static class SafeFile
{
	public static void WriteNew(string path, ReadOnlySpan<byte> bytes)
	{
		using FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
		fileStream.Write(bytes);
	}

	public static void WriteNew(string path, Action<Stream> write)
	{
		try
		{
			using FileStream obj = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
			write(obj);
		}
		catch
		{
			try
			{
				if (File.Exists(path) && new FileInfo(path).Length == 0L)
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
			throw;
		}
	}
}
public static class SizeFormat
{
	private static readonly string[] Units = new string[5] { "B", "KB", "MB", "GB", "TB" };

	public static string Format(long bytes)
	{
		if (bytes < 1024)
		{
			return $"{bytes} B";
		}
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < Units.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (Math.Round(num, 1) >= 1024.0 && num2 < Units.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return ((num >= 100.0) ? Math.Round(num).ToString("0", CultureInfo.InvariantCulture) : num.ToString("0.#", CultureInfo.InvariantCulture)) + " " + Units[num2];
	}
}
public static class SliceGeometry
{
	public const int Center = -1;

	public const int Outside = -2;

	public static int HitTest(double dx, double dy, int count, double innerRadius, double outerRadius)
	{
		double num = Math.Sqrt(dx * dx + dy * dy);
		if (num < innerRadius)
		{
			return -1;
		}
		if (num > outerRadius || count <= 0)
		{
			return -2;
		}
		double num2 = Math.Atan2(dx, 0.0 - dy) * 180.0 / Math.PI;
		double num3 = 360.0 / (double)count;
		return Math.Min((int)((num2 + num3 / 2.0 + 360.0) % 360.0 / num3), count - 1);
	}

	public static double MidAngle(int index, int count)
	{
		return (double)index * 360.0 / (double)count;
	}

	public static double StartAngle(int index, int count)
	{
		return MidAngle(index, count) - 180.0 / (double)count;
	}

	public static double EndAngle(int index, int count)
	{
		return MidAngle(index, count) + 180.0 / (double)count;
	}

	public static (double X, double Y) PointAt(double cx, double cy, double r, double angleDeg)
	{
		double num = angleDeg * Math.PI / 180.0;
		return (X: cx + r * Math.Sin(num), Y: cy - r * Math.Cos(num));
	}
}
public sealed record VideoInfo(TimeSpan Duration, uint Width, uint Height, uint Bitrate);
public static class VideoCompressor
{
	private const double ContainerOverhead = 0.05;

	private const uint MinVideoBitrate = 100000u;

	public static async Task<VideoInfo> GetInfoAsync(string path)
	{
		TaskAwaiter<StorageFile> taskAwaiter = WindowsRuntimeSystemExtensions.GetAwaiter<StorageFile>(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
		if (!taskAwaiter.IsCompleted)
		{
			await taskAwaiter;
			TaskAwaiter<StorageFile> taskAwaiter2 = default;
			taskAwaiter = taskAwaiter2;
		}
		TaskAwaiter<VideoProperties> taskAwaiter3 = WindowsRuntimeSystemExtensions.GetAwaiter<VideoProperties>(taskAwaiter.GetResult().Properties.GetVideoPropertiesAsync());
		if (!taskAwaiter3.IsCompleted)
		{
			await taskAwaiter3;
			TaskAwaiter<VideoProperties> taskAwaiter4 = default;
			taskAwaiter3 = taskAwaiter4;
		}
		VideoProperties result = taskAwaiter3.GetResult();
		return new VideoInfo(result.Duration, result.Width, result.Height, result.Bitrate);
	}

	public static (uint Video, uint Audio) PlanBitrates(long targetBytes, TimeSpan duration, bool hasAudio)
	{
		if (duration <= TimeSpan.Zero)
		{
			throw new ArgumentException("The video has no length.");
		}
		double num = (double)(targetBytes * 8) * 0.95 / duration.TotalSeconds;
		double num2 = (hasAudio ? Math.Clamp(num * 0.12, 48000.0, 128000.0) : 0.0);
		return (Video: (uint)Math.Max(100000.0, num - num2), Audio: (uint)num2);
	}

	public static bool LooksBlurry(uint videoBitrate, uint width, uint height)
	{
		if (width != 0 && height != 0)
		{
			return (double)videoBitrate < (double)width * (double)height * 30.0 * 0.025;
		}
		return false;
	}

	public static async Task<CompressResult> CompressAsync(string path, long targetBytes, IProgress<double>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		long original = new FileInfo(path).Length;
		StorageFile src = await WindowsRuntimeSystemExtensions.AsTask<StorageFile>(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)), ct);
		VideoInfo info = await GetInfoAsync(path);
		MediaEncodingProfile srcProfile = await WindowsRuntimeSystemExtensions.AsTask<MediaEncodingProfile>(MediaEncodingProfile.CreateFromFileAsync((IStorageFile)(object)src), ct);
		bool hasAudio = srcProfile.Audio != (AudioEncodingProperties)null;
		(uint, uint) tuple = PlanBitrates(targetBytes, info.Duration, hasAudio);
		uint videoBps = tuple.Item1;
		uint audioBps = tuple.Item2;
		string best = null;
		long bestSize = long.MaxValue;
		uint usedVideo = videoBps;
		try
		{
			for (int attempt = 0; attempt < 3; attempt++)
			{
				MediaEncodingProfile profile = BuildProfile(srcProfile, videoBps, audioBps);
				string tmp = Path.Combine(Path.GetTempPath(), $"zest-{Guid.NewGuid():N}.mp4");
				int a = attempt;
				await TranscodeAsync(src, tmp, profile, new Progress<double>((double p) =>
				{
					progress?.Report(((double)a + p) / ((double)a + 1.0));
				}), ct);
				long length = new FileInfo(tmp).Length;
				if (length < bestSize)
				{
					if (best != null)
					{
						File.Delete(best);
					}
					best = tmp;
					bestSize = length;
					usedVideo = videoBps;
				}
				else
				{
					File.Delete(tmp);
				}
				if (bestSize <= targetBytes || videoBps <= 100000)
				{
					break;
				}
				double num = (double)targetBytes / (double)length * 0.95;
				videoBps = (uint)Math.Max(100000.0, (double)videoBps * num);
			}
			if (best == null || bestSize >= original)
			{
				return new CompressResult(path, null, original, original, ReachedTarget: false, "Could not make this video smaller. It is already well compressed for this size.");
			}
			string text = OutputPaths.WithSuffix(path, "-compressed", ".mp4");
			File.Move(best, text, overwrite: false);
			best = null;
			bool flag = bestSize <= targetBytes;
			string value = $"{usedVideo / 1000:N0} kbps";
			string text2 = (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ? "" : " Saved as MP4.");
			if (LooksBlurry(usedVideo, info.Width, info.Height))
			{
				text2 += " This is a low bitrate for this resolution, so it may look blurry.";
			}
			string message = (flag ? $"{SizeFormat.Format(original)} → {SizeFormat.Format(bestSize)}. Video {value}.{text2}" : $"Could not reach {SizeFormat.Format(targetBytes)}. Smallest: {SizeFormat.Format(bestSize)} (video {value}).{text2}");
			return new CompressResult(path, text, original, bestSize, flag, message);
		}
		finally
		{
			if (best != null)
			{
				TryDelete(best);
			}
		}
	}

	public static async Task<string> ConvertToMp4Async(string path, IProgress<double>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		StorageFile src = await WindowsRuntimeSystemExtensions.AsTask<StorageFile>(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)), ct);
		MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4((VideoEncodingQuality)0);
		string tmp = Path.Combine(Path.GetTempPath(), $"zest-{Guid.NewGuid():N}.mp4");
		try
		{
			await TranscodeAsync(src, tmp, profile, progress, ct);
			string text = OutputPaths.ForConvert(path, ".mp4");
			File.Move(tmp, text, overwrite: false);
			return text;
		}
		finally
		{
			TryDelete(tmp);
		}
	}

	private static MediaEncodingProfile BuildProfile(MediaEncodingProfile source, uint videoBps, uint audioBps)
	{
		MediaEncodingProfile val = MediaEncodingProfile.CreateMp4((VideoEncodingQuality)1);
		VideoEncodingProperties video = source.Video;
		if (video != (VideoEncodingProperties)null && video.Width != 0 && video.Height != 0)
		{
			val.Video.Width = video.Width & 0xFFFFFFFEu;
			val.Video.Height = video.Height & 0xFFFFFFFEu;
			if (video.FrameRate.Numerator != 0 && video.FrameRate.Denominator != 0)
			{
				var (numerator, denominator) = StandardFrameRate((double)video.FrameRate.Numerator / (double)video.FrameRate.Denominator);
				val.Video.FrameRate.Numerator = numerator;
				val.Video.FrameRate.Denominator = denominator;
			}
			val.Video.PixelAspectRatio.Numerator = 1u;
			val.Video.PixelAspectRatio.Denominator = 1u;
		}
		val.Video.Bitrate = videoBps;
		if (source.Audio == (AudioEncodingProperties)null || audioBps == 0)
		{
			val.Audio = null;
		}
		else
		{
			val.Audio.Bitrate = audioBps;
			val.Audio.ChannelCount = Math.Clamp(source.Audio.ChannelCount, 1u, 2u);
			AudioEncodingProperties audio = val.Audio;
			uint sampleRate = source.Audio.SampleRate;
			bool flag = ((sampleRate == 44100 || sampleRate == 48000) ? true : false);
			audio.SampleRate = (flag ? source.Audio.SampleRate : 48000u);
		}
		return val;
	}

	public static (uint Num, uint Den) StandardFrameRate(double fps)
	{
		return new (uint, uint)[9]
		{
			(24000u, 1001u),
			(24u, 1u),
			(25u, 1u),
			(30000u, 1001u),
			(30u, 1u),
			(50u, 1u),
			(60000u, 1001u),
			(60u, 1u),
			(120u, 1u)
		}.MinBy(((uint, uint) s) => Math.Abs((double)s.Item1 / (double)s.Item2 - fps));
	}

	private static async Task TranscodeAsync(StorageFile src, string outPath, MediaEncodingProfile profile, IProgress<double>? progress, CancellationToken ct)
	{
		StorageFile val = await WindowsRuntimeSystemExtensions.AsTask<StorageFile>((await WindowsRuntimeSystemExtensions.AsTask<StorageFolder>(StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outPath)), ct)).CreateFileAsync(Path.GetFileName(outPath), (CreationCollisionOption)2), ct);
		try
		{
			PrepareTranscodeResult val2 = await WindowsRuntimeSystemExtensions.AsTask<PrepareTranscodeResult>(new MediaTranscoder
			{
				HardwareAccelerationEnabled = true
			}.PrepareFileTranscodeAsync((IStorageFile)(object)src, (IStorageFile)(object)val, profile), ct);
			if (!val2.CanTranscode)
			{
				TranscodeFailureReason failureReason = val2.FailureReason;
				string message;
				if ((int)failureReason == 2)
				{
					message = "Windows could not use these encoding settings for this video.";
				}
				else
				{
					message = (((int)failureReason != 3) ? "Windows cannot convert this video." : "Windows cannot read this video format. Install the matching codec from the Microsoft Store (for example \"VP9 Video Extensions\" for WEBM or \"HEVC Video Extensions\" for H.265).");
				}
				throw new InvalidOperationException(message);
			}
			IAsyncActionWithProgress<double> val3 = val2.TranscodeAsync();
			val3.Progress = (IAsyncActionWithProgress<double> _, double percent) =>
			{
				progress?.Report(Math.Clamp(percent / 100.0, 0.0, 1.0));
			};
			await WindowsRuntimeSystemExtensions.AsTask<double>(val3, ct);
		}
		catch
		{
			TryDelete(outPath);
			throw;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
public static class WebLinks
{
	public static bool TryGetWebLink(string? text, out Uri? link)
	{
		link = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.Any((char c) => char.IsWhiteSpace(c) || char.IsControl(c)))
		{
			return false;
		}
		if (text2.Length > 2048)
		{
			return false;
		}
		if (!Uri.TryCreate(text2, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		if (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps)
		{
			return false;
		}
		if (result.IsUnc || result.IsFile || string.IsNullOrEmpty(result.Host))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(result.UserInfo))
		{
			return false;
		}
		link = result;
		return true;
	}
}
public static class WindowsCodecs
{
	public const string HeifHelp = "To save HEIC, install \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store.";

	public static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(byte[] png, CancellationToken ct = default(CancellationToken))
	{
		InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
		try
		{
			await WindowsRuntimeSystemExtensions.AsTask<uint, uint>(stream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(png)), ct);
			stream.Seek(0uL);
			return await WindowsRuntimeSystemExtensions.AsTask<SoftwareBitmap>((await WindowsRuntimeSystemExtensions.AsTask<BitmapDecoder>(BitmapDecoder.CreateAsync((IRandomAccessStream)(object)stream), ct)).GetSoftwareBitmapAsync((BitmapPixelFormat)87, (BitmapAlphaMode)0), ct);
		}
		finally
		{
			((IDisposable)stream)?.Dispose();
		}
	}

	public static async Task<byte[]> EncodeHeicAsync(MagickImage image, int quality, CancellationToken ct = default(CancellationToken))
	{
		MagickImage val = (MagickImage)image.Clone();
		byte[] png;
		try
		{
			val.Format = (MagickFormat)191;
			png = val.ToByteArray();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		SoftwareBitmap bitmap = await ToSoftwareBitmapAsync(png, ct);
		try
		{
			InMemoryRandomAccessStream output = new InMemoryRandomAccessStream();
			try
			{
				try
				{
					BitmapPropertySet val2 = new BitmapPropertySet();
					val2.Add("ImageQuality", new BitmapTypedValue((object)((float)Math.Clamp(quality, 1, 100) / 100f), (PropertyType)8));
					BitmapPropertySet val3 = val2;
					BitmapEncoder encoder;
					try
					{
						encoder = await WindowsRuntimeSystemExtensions.AsTask<BitmapEncoder>(BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, (IRandomAccessStream)(object)output, (IEnumerable<KeyValuePair<string, BitmapTypedValue>>)val3), ct);
					}
					catch (ArgumentException)
					{
						encoder = await WindowsRuntimeSystemExtensions.AsTask<BitmapEncoder>(BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, (IRandomAccessStream)(object)output), ct);
					}
					encoder.SetSoftwareBitmap(bitmap);
					await WindowsRuntimeSystemExtensions.AsTask(encoder.FlushAsync(), ct);
				}
				catch (Exception ex2) when (!(ex2 is OperationCanceledException))
				{
					throw new InvalidOperationException("To save HEIC, install \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store.", ex2);
				}
				byte[] bytes = new byte[output.Size];
				output.Seek(0uL);
				await WindowsRuntimeSystemExtensions.AsTask<IBuffer, uint>(output.ReadAsync(WindowsRuntimeBufferExtensions.AsBuffer(bytes), (uint)bytes.Length, (InputStreamOptions)0), ct);
				if (bytes.Length == 0)
				{
					throw new InvalidOperationException("To save HEIC, install \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store.");
				}
				return bytes;
			}
			finally
			{
				((IDisposable)output)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)bitmap)?.Dispose();
		}
	}
}
