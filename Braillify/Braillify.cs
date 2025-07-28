using System.Diagnostics;
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

internal class Braillify : IDisposable {
	private readonly Context _context;
	private readonly Accelerator _accelerator;
	private readonly Stopwatch _stp;

	private Braillify() {
		_stp = new Stopwatch();
		_stp.Start();
		_context = Context.Create(builder => builder.AllAccelerators().EnableAlgorithms());
		var d = _context.GetPreferredDevice(preferCPU: false);
		_accelerator = d.CreateAccelerator(_context);
	}

	~Braillify() {
		Dispose(false);
	}

	private static void Kernel(Index1D i, ArrayView<Rgba32> data, ArrayView<int> output, int width, int height,
		double brightness, int invert, int space) {
		var x = i % (width / 2);
		var y = i / (width / 2);

		output[i] = '⠀';

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
				output[i] |= (1 << j);
			}
		}

		if (output[i] == '⠀') {
			output[i] = space;
		}
	}

	private static void KernelAlt(Index1D i, ArrayView<Rgba32> data, ArrayView<int> output, int width, int height,
		double brightness, int invert, int space) {
		int[] oct = [
			32, 118440, 118443, 129922, 118016, 9624, 118017, 118018, 118019, 118020, 9629, 118021, 118022, 118023,
			118024, 9600, 118025, 118026, 118027, 118028, 130022, 118029, 118030, 118031, 118032, 118033, 118034,
			118035, 118036, 118037, 118038, 118039, 118040, 118041, 118042, 118043, 118044, 118045, 118046, 118047,
			130023, 118048, 118049, 118050, 118051, 118052, 118053, 118054, 118055, 118056, 118057, 118058, 118059,
			118060, 118061, 118062, 118063, 118064, 118065, 118066, 118067, 118068, 118069, 129925, 118435, 118070,
			118071, 118072, 118073, 118074, 118075, 118076, 118077, 118078, 118079, 118080, 118081, 118082, 118083,
			118084, 9622, 118085, 118086, 118087, 118088, 9612, 118089, 118090, 118091, 118092, 9630, 118093, 118094,
			118095, 118096, 9627, 118097, 118098, 118099, 118100, 118101, 118102, 118103, 118104, 118105, 118106,
			118107, 118108, 118109, 118110, 118111, 118112, 118113, 118114, 118115, 118116, 118117, 118118, 118119,
			118120, 118121, 118122, 118123, 118124, 118125, 118126, 118127, 118128, 118432, 118129, 118130, 118131,
			118132, 118133, 118134, 118135, 118136, 118137, 118138, 118139, 118140, 118141, 118142, 118143, 118144,
			118145, 118146, 118147, 118148, 118149, 118150, 118151, 118152, 118153, 118154, 118155, 118156, 118157,
			118158, 118159, 9623, 118160, 118161, 118162, 118163, 9626, 118164, 118165, 118166, 118167, 9616, 118168,
			118169, 118170, 118171, 9628, 118172, 118173, 118174, 118175, 118176, 118177, 118178, 118179, 118180,
			118181, 118182, 118183, 118184, 118185, 118186, 118187, 9602, 118188, 118189, 118190, 118191, 118192,
			118193, 118194, 118195, 118196, 118197, 118198, 118199, 118200, 118201, 118202, 118203, 118204, 118205,
			118206, 118207, 118208, 118209, 118210, 118211, 118212, 118213, 118214, 118215, 118216, 118217, 118218,
			118219, 118220, 118221, 118222, 118223, 118224, 118225, 118226, 118227, 118228, 118229, 118230, 118231,
			118232, 118233, 118234, 9604, 118235, 118236, 118237, 118238, 9625, 118239, 118240, 118241, 118242, 9631,
			118243, 9606, 118244, 118245, 9608
		];

		var x = i % (width / 2);
		var y = i / (width / 2);


		output[i] = 0;


		for (var j = 0; j < 8; j++) {
			var dX = j % 2;
			var dY = j / 2;


			var colour = data[x * 2 + dX + (y * 4 + dY) * width];
			var r = colour.R;
			var g = colour.G;
			var b = colour.B;
			var rSq = r * r;
			var gSq = g * g;
			var bSq = b * b;
			var grey = XMath.Sqrt((rSq + gSq + bSq) / 3.0);


			if ((grey > brightness * 255) == (invert == 0)) {
				output[i] |= (1 << j);
			}
		}


		output[i] = oct[output[i]];
	}

	private StringBuilder Compute(Accelerator a, MemoryBuffer1D<Rgba32, Stride1D.Dense> inputBuff,
		MemoryBuffer1D<int, Stride1D.Dense> outputBuff, int width, int height, double brightness, bool invert,
		char space, bool alt) {
		Action<Index1D, ArrayView<Rgba32>, ArrayView<int>, int, int, double, int, int> loadedKernel;

		if (alt) {
			loadedKernel =
				a.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Rgba32>, ArrayView<int>, int, int, double, int, int>(
					KernelAlt);
		}
		else {
			loadedKernel =
				a.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Rgba32>, ArrayView<int>, int, int, double, int, int>(
					Kernel);
		}

		loadedKernel((int)outputBuff.Length, inputBuff.View, outputBuff.View, width, height, brightness,
			invert ? 1 : 0, space);

		a.Synchronize();

		var braille = outputBuff.GetAsArray1D();

		var brailleBuilder = new StringBuilder((height * width / 8) + height + 3);
		brailleBuilder.Append("\e[H");

		for (var i = 0; i < height / 4; i++) {
			for (var j = 0; j < width / 2; j++) {
				brailleBuilder.Append(new Rune(braille[j + (i * width / 2)]).ToString());
			}

			brailleBuilder.AppendLine();
		}

		return brailleBuilder;
	}

	private StringBuilder ScheduleTask(Span<Rgba32> data, int width, int height, double brightness, bool invert,
		char space,
		bool alt) {
		using var inputBuff = _accelerator.Allocate1D<Rgba32>(data.Length);
		inputBuff.View.BaseView.CopyFromCPU((ReadOnlySpan<Rgba32>)data);
		using var outputBuff = _accelerator.Allocate1D<int>(width * height / 8);

		return Compute(_accelerator, inputBuff, outputBuff, width, height, brightness, invert, space, alt);
	}

	public static void Main(string[] args) {
		using var b = new Braillify();
		var elapsedTime = -1L;
		var alt = false;
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
				case 'l':
					loop = (args[i + 1].ToLower()[0] != 'n');
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
				case 'a':
					alt = (args[i + 1].ToLower()[0] != 'n');
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
					invert = (args[i + 1].ToLower()[0] != 'n');
					break;
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

		int fileType;

		var brailleString = File.ReadAllText(inPath);

		if (brailleString.Split(";")[0] == "Braillify") {
			fileType = 0;
		}
		else {
			try {
				using var image = Image.Load<Rgba32>(inPath, out var format);

				if (format == GifFormat.Instance) {
					throw new Exception("GIF");
				}

				fileType = 1;
			}
			catch (Exception) {
				try {
					FFmpegLoader.FFmpegPath = "/usr/lib";
					using var file = MediaFile.Open(inPath);
					fileType = 2;
				}
				catch (Exception) {
					fileType = 0;
				}
			}
		}

		switch (fileType) {
			case 0: {
				var keys = brailleString.Split(';');
				var frameDelay = int.Parse(keys[1].Split(":")[1]);
				var brailles = keys[2].Split(":")[1].Split("#");
				Console.WriteLine("\ec");
				elapsedTime = b._stp.ElapsedMilliseconds - frameDelay;

				do {
					foreach (var braille in brailles) {
						elapsedTime += frameDelay;

						if (b._stp.ElapsedMilliseconds < elapsedTime) {
							Thread.Sleep((int)(elapsedTime - b._stp.ElapsedMilliseconds));
						}

						Console.Write(braille.Substring(0, braille.Length - 1));
					}
				} while (loop);

				Console.WriteLine("");
				break;
			}
			case 1: {
				using var image = Image.Load<Rgba32>(inPath, out var format);

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
					Console.WriteLine(b.ScheduleTask(data, width, height, brightness, invert, space, alt));
				}
				else {
					File.WriteAllText(outPath,
						b.ScheduleTask(data, width, height, brightness, invert, space, alt).ToString());
				}
			}

				break;
			case 2: {
				if (outPath.Length == 0) {
					Console.WriteLine("\ec");
				}
				else {
					Console.WriteLine("Converting Video, hold on for a bit");
				}


				var brailles = new List<StringBuilder>();
				using var file = MediaFile.Open(inPath);
				var framePick = 0;
				var frameDelay = (int)(1000 * frameSelect / file.Video.Info.AvgFrameRate);


				var frameWidth = file.Video.Info.FrameSize.Width;
				var frameHeight = file.Video.Info.FrameSize.Height;

				width = (int)(scale * frameWidth / 100.0);
				height = (int)(scale * frameHeight / 100.0);

				width += width % 2;

				if (height % 4 != 0) {
					height += 4 - (height % 4);
				}

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
								var backing = new Bgr24[frameWidth * frameHeight];
								var spanBgr = new Span<Bgr24>(backing);

								var stride = img.Stride;

								for (var i = 0; i < frameHeight; i++) {
									for (var j = 0; j < frameWidth; j++) {
										spanBgr[j + i * frameWidth] = new Bgr24(span[j * 3 + i * stride],
											span[j * 3 + i * stride + 1],
											span[j * 3 + i * stride + 2]);
									}
								}


								var imageBgr24 = Image.LoadPixelData<Bgr24>(spanBgr, frameWidth, frameHeight);

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

						if (outPath.Length == 0) {
							if (elapsedTime > 0) {
								elapsedTime += frameDelay;

								if (b._stp.ElapsedMilliseconds < elapsedTime) {
									Thread.Sleep((int)(elapsedTime - b._stp.ElapsedMilliseconds));
								}
							}
							else {
								elapsedTime = b._stp.ElapsedMilliseconds;
							}


							Console.WriteLine(b.ScheduleTask(data, width, height, brightness, invert, space,
								alt));
						}
						else {
							brailles.Add(b.ScheduleTask(data, width, height, brightness, invert, space, alt));
						}
					}


					framePick++;
					framePick %= frameSelect;
				}

				if (outPath.Length != 0) {
					var braillesString = new StringBuilder();

					braillesString.Append(
						"Braillify;Frame Delay:" + frameDelay + ";Data:");

					foreach (var braille in brailles) {
						braillesString.Append(braille);
						braillesString.Append('#');
					}

					File.WriteAllText(outPath, braillesString.ToString());
				}
			}

				break;
		}
	}

	private void ReleaseUnmanagedResources() {
		_accelerator.Dispose();
		_context.Dispose();
	}

	private void Dispose(bool disposing) {
		ReleaseUnmanagedResources();

		if (disposing) {
			_context.Dispose();
			_accelerator.Dispose();
		}
	}

	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}