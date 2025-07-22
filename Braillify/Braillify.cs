using System.Runtime.InteropServices;
using System.Text;
using ILGPU;
using ILGPU.Runtime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ILGPU.Algorithms;
using SixLabors.ImageSharp.Processing;

namespace Braillify;

internal class Braillify {
	private static void Kernel(Index1D i, ArrayView<uint> data, ArrayView<ushort> output, int width, int height,
		double brightness) {
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
			var r = (colour >> 16) % (1 << 8);
			var g = (colour >> 8) % (1 << 8);
			var b = (colour >> 0) % (1 << 8);
			var rSq = r * r;
			var gSq = g * g;
			var bSq = b * b;
			var grey = XMath.Sqrt((rSq + gSq + bSq) / 3.0);

			if (grey > brightness * 255) {
				/*rSqSum += rSqSum;
				gSqSum += gSqSum;
				bSqSum += bSqSum;*/
				output[i] |= (ushort)(1 << j);
			}
		}
	}

	private void Compute(Accelerator a, MemoryBuffer1D<uint, Stride1D.Dense> inputBuff,
		MemoryBuffer1D<ushort, Stride1D.Dense> outputBuff, int width, int height, double brightness) {
		var loadedKernel =
			a.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<ushort>, int, int, double>(Kernel);

		loadedKernel((int)outputBuff.Length, inputBuff.View, outputBuff.View, width, height, brightness);

		a.Synchronize();

		var braille = outputBuff.GetAsArray1D();

		var brailleBuilder = new StringBuilder();

		for (var i = 0; i < height / 4; i++) {
			for (var j = 0; j < width / 2; j++) {
				brailleBuilder.Append((char)(braille[j + (i * width / 2)]));
			}

			brailleBuilder.AppendLine();
		}

		Console.WriteLine(brailleBuilder);
	}

	private void Init(uint[] data, int width, int height, double brightness) {
		var context = Context.Create(builder => builder.AllAccelerators());
		var d = context.GetPreferredDevice(preferCPU: false);
		var a = d.CreateAccelerator(context);


		var inputBuff = a.Allocate1D(data);
		var outputBuff = a.Allocate1D<ushort>(width * height / 8);

		Compute(a, inputBuff, outputBuff, width, height, brightness);

		a.Dispose();
		context.Dispose();
	}

	public static void Main(string[] args) {
		var inPath = "";
		int width;
		int height;
		var brightness = 50 / 100.0;

		var scale = 100.0;

		for (var i = 0; i < args.Length; i += 2) {
			if (args[i][0] != '-') {
				throw new Exception();
			}

			switch (args[i][1]) {
				case 'p':
					inPath = args[i + 1].Replace("\"", "").Replace("'", "");
					break;
				/*case 'o':
					outPath = args[i + 1].replaceAll("\"", "").replaceAll("'", "");
					break;*/
				case 'd':
					scale = double.Parse(args[i + 1]);
					break;
				/*case 's':
					switch (args[i + 1].toLowerCase()) {
						case "space":
							space = ' ';
							break;
						case "blank":
							space = (char) 10240;
							break;
						case "dot":
							space = (char) 10241;
							break;
					}
				break;*/
				/*case 'i':
					invert = (args[i + 1].toUpperCase().charAt(0) == 'Y');
					break;
				case 'e':
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
				case 'b':
					brightness = int.Parse(args[i + 1]) / 100.0;
					break;
				default:
					Console.WriteLine("Unknown flag: " + args[i]);
					throw new Exception();
			}
		}

		try {
			var image = Image.Load<Rgba32>(inPath);

			width = (int)(scale * image.Width / 100.0);
			height = (int)(scale * image.Height / 100.0);


			width += width % 2;

			if (height % 4 != 0) {
				height += 4 - (height % 4);
			}

			if (width != image.Width || height != image.Height) {
				image.Mutate(ctx => ctx.Resize(width, height));
			}

			Span<Rgba32> rgbaSpan;

			if (image.DangerousTryGetSinglePixelMemory(out var memory)) {
				rgbaSpan = memory.Span;
			}
			else {
				rgbaSpan = new Rgba32[image.Width * image.Height];
				image.CopyPixelDataTo(rgbaSpan);
			}

			var intSpan = MemoryMarshal.Cast<Rgba32, uint>(rgbaSpan);
			var data = intSpan.ToArray();

			var b = new Braillify();
			b.Init(data, image.Width, image.Height, brightness);
		}
		catch (Exception e) {
			Console.WriteLine("Error");
			Console.WriteLine(e);
		}
	}
}