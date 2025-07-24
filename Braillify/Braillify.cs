using System.Runtime.InteropServices;
using System.Text;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using FFMediaToolkit;
using ILGPU;
using ILGPU.Runtime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ILGPU.Algorithms;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Processing;

namespace Braillify;

internal class Braillify {
	private static void Kernel(Index1D i, ArrayView<Rgba32> data, ArrayView<ushort> output, int width, int height,
		double brightness, int invert, ushort space) {
		var x = i % (width / 2);
		var y = i / (width / 2);
		output[i] = '⠀';

		/*var rSqSum = 0;
		var gSqSum = 0;
		var bSqSum = 0;*/

		for (var j = 0; j < 8; j++) {
			int dX;
			int dY;

			if (j < 6) {
				dX = j / 3;
				dY = j % 3;
			}
			else {
				dX = j % 2;
				dY = 3;
			}

			var colour = data[x * 2 + dX + (y * 4 + dY) * width];
			var r = colour.R;
			var g = colour.G;
			var b = colour.B;
			var rSq = r * r;
			var gSq = g * g;
			var bSq = b * b;
			var grey = XMath.Sqrt((rSq + gSq + bSq) / 3.0);


			if ((grey > brightness * 255) == (invert == 0)) {
				/*rSqSum += rSqSum;
				gSqSum += gSqSum;
				bSqSum += bSqSum;*/
				output[i] |= (ushort)(1 << j);
			}
		}

		if (output[i] == '⠀') {
			output[i] = space;
		}
	}

	private StringBuilder Compute(Accelerator a, MemoryBuffer1D<Rgba32, Stride1D.Dense> inputBuff,
		MemoryBuffer1D<ushort, Stride1D.Dense> outputBuff, int width, int height, double brightness, bool invert,
		char space) {
		var loadedKernel =
			a.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Rgba32>, ArrayView<ushort>, int, int, double, int, ushort>(
				Kernel);


		loadedKernel((int)outputBuff.Length, inputBuff.View, outputBuff.View, width, height, brightness,
			invert ? 1 : 0, space);

		a.Synchronize();

		var braille = outputBuff.GetAsArray1D();

		var brailleBuilder = new StringBuilder();
		brailleBuilder.Append("\e[H");

		for (var i = 0; i < height / 4; i++) {
			for (var j = 0; j < width / 2; j++) {
				brailleBuilder.Append((char)(braille[j + (i * width / 2)]));
			}

			brailleBuilder.AppendLine();
		}

		return brailleBuilder;
	}

	private StringBuilder Init(Span<Rgba32> data, int width, int height, double brightness, bool invert, char space) {
		using var context = Context.Create(builder => builder.AllAccelerators());
		var d = context.GetPreferredDevice(preferCPU: false);
		using var a = d.CreateAccelerator(context);


		using var inputBuff = a.Allocate1D<Rgba32>(data.Length);
		inputBuff.View.BaseView.CopyFromCPU((ReadOnlySpan<Rgba32>)data);
		using var outputBuff = a.Allocate1D<ushort>(width * height / 8);

		return Compute(a, inputBuff, outputBuff, width, height, brightness, invert, space);
	}

	public static void Main(string[] args) {
		var b = new Braillify();
		var read = false;
		var loop = false;
		var inPath = "";
		var outPath = "";
		int width;
		int height;
		var invert = false;
		var frameSelect = 1;
		var space = '⠀';
		var brightness = 50 / 100.0;

		var scale = 100.0;

		for (var i = 0; i < args.Length; i += 2) {
			if (args[i][0] != '-') {
				throw new Exception();
			}

			switch (args[i][1]) {
				case 'r':
					read = (args[i + 1].ToLower()[0] == 'y');
					break;
				case 'l':
					loop = (args[i + 1].ToLower()[0] == 'y');
					break;
				case 'p':
					inPath = args[i + 1].Replace("\"", "").Replace("'", "");
					break;
				case 'o':
					outPath = args[i + 1].Replace("\"", "").Replace("'", "");
					break;
				case 'd':
					scale = double.Parse(args[i + 1]);
					break;
				case 's':
					switch (args[i + 1].ToLower()) {
						case "space":
							space = ' ';
							break;
						case "blank":
							space = '⠀';
							break;
						case "dot":
							space = (char)('⠀' + 1);
							break;
					}

					break;
				case 'i':
					invert = (args[i + 1].ToLower()[0] == 'y');
					break;
				/*case 'e':
					edge = (args[i + 1].toUpperCase().charAt(0) == 'Y');
					break;
				case 'c':
					colour = (args[i + 1].toUpperCase().charAt(0) == 'Y');
					break;
				case 'm':
					switch (args[i + 1].toLowerCase()) {
						case "min":
							mode = 0;
							break;
						case "rms":
							mode = 1;
							break;
						case "max":
							mode = 2;
							break;
						case "r":
							mode = 3;
							break;
						case "g":
							mode = 4;
							break;
						case "b":
							mode = 5;
							break;
					}
					break;*/
				case 'f':
					frameSelect = int.Parse(args[i + 1]);
					break;
				case 'b':
					brightness = int.Parse(args[i + 1]) / 100.0;
					break;
				default:
					Console.WriteLine("Unknown flag: " + args[i]);
					throw new Exception();
			}
		}

		if (outPath.Length == 0) {
			Console.WriteLine("\ec");
		}

		if (read) {
			var brailleString = File.ReadAllText(inPath);
			var keys = brailleString.Split(';');
			var frameDelay = int.Parse(keys[0].Split(":")[1]);
			var brailles = keys[1].Split(":")[1].Split("#");
			Console.WriteLine("\ec");

			do {
				foreach (var braille in brailles) {
					Console.WriteLine(braille);
					Thread.Sleep(frameDelay);
				}
			} while (loop);
		}
		else {
			try {
				var image = Image.Load<Rgba32>(inPath, out var format);

				if (format == GifFormat.Instance) {
					throw new UnknownImageFormatException("");
				}

				width = (int)(scale * image.Width / 100.0);
				height = (int)(scale * image.Height / 100.0);


				width += width % 2;

				if (height % 4 != 0) {
					height += 4 - (height % 4);
				}

				if (width != image.Width || height != image.Height) {
					var width1 = width;
					var height1 = height;
					image.Mutate(ctx => ctx.Resize(width1, height1));
				}

				var dataArr = new Rgba32[width * height];
				var data = new Span<Rgba32>(dataArr);

				if (image.DangerousTryGetSinglePixelMemory(out var memory)) {
					data = memory.Span;
				}
				else {
					image.CopyPixelDataTo(data);
				}

				if (outPath.Length == 0) {
					Console.WriteLine(b.Init(data, width, height, brightness, invert, space));
				}
				else {
					File.WriteAllText(outPath, b.Init(data, width, height, brightness, invert, space).ToString());
				}
			}
			catch (UnknownImageFormatException) {
				try {
					if (outPath.Length == 0) {
						Console.WriteLine("Converting Video, hold on for a bit");
					}

					var brailles = new List<StringBuilder>();
					FFmpegLoader.FFmpegPath = "/usr/lib";
					using var file = MediaFile.Open(inPath);
					var framePick = 0;
					var frameDelay = (int)(1000 * frameSelect / file.Video.Info.AvgFrameRate);


					var frameWidth = file.Video.Info.FrameSize.Width;
					var frameHeight = file.Video.Info.FrameSize.Height;

					width = (int)(scale * frameWidth / 100.0);
					height = (int)(scale * frameHeight / 100.0);

					Memory<Rgba32> memory;

					while (file.Video.TryGetNextFrame(out var img)) {
						if (framePick == 0) {
							var span = img.Data;

							var dataArr = new Rgba32[width * height];

							var data = new Span<Rgba32>(dataArr);

							switch (img.PixelFormat) {
								case ImagePixelFormat.Rgba32:
									data = MemoryMarshal.Cast<byte, Rgba32>(span);
									var image = Image.LoadPixelData<Rgba32>(data, frameWidth, frameHeight);


									if (width != frameWidth || height != frameHeight) {
										var width1 = width;
										var height1 = height;
										image.Mutate(ctx => ctx.Resize(width1, height1));
									}

									if (image.DangerousTryGetSinglePixelMemory(out memory)) {
										data = memory.Span;
									}
									else {
										image.CopyPixelDataTo(data);
									}

									break;
								case ImagePixelFormat.Bgr24:
									var imageBgr24 = Image.LoadPixelData<Bgr24>(span, frameWidth, frameHeight);

									var imageRbga32 = imageBgr24.CloneAs<Rgba32>(imageBgr24.GetConfiguration());

									if (width != frameWidth || height != frameHeight) {
										var width1 = width;
										var height1 = height;
										imageRbga32.Mutate(ctx => ctx.Resize(width1, height1));
									}

									if (imageRbga32.DangerousTryGetSinglePixelMemory(out memory)) {
										data = memory.Span;
									}
									else {
										imageRbga32.CopyPixelDataTo(data);
									}

									break;
							}


							brailles.Add(b.Init(data, width, height, brightness, invert, space));
						}


						framePick++;
						framePick %= frameSelect;
					}

					if (outPath.Length == 0) {
						Console.WriteLine("Processing Finished, Press any Key to Continue...");
						Console.ReadKey();
						Console.WriteLine("\ec");
					}

					if (outPath.Length == 0) {
						do {
							foreach (var braille in brailles) {
								Console.WriteLine(braille);
								Thread.Sleep(frameDelay);
							}
						} while (loop);
					}
					else {
						var braillesString = new StringBuilder();

						braillesString.Append(
							"Frame Delay:" + frameDelay + ";Data:");

						foreach (var braille in brailles) {
							braillesString.Append(braille);
							braillesString.Append('#');
						}

						File.WriteAllText(outPath, braillesString.ToString());
					}
				}
				catch (Exception e2) {
					Console.WriteLine(e2);
				}
			}
			catch (Exception e) {
				Console.WriteLine(e);
			}
		}
	}
}