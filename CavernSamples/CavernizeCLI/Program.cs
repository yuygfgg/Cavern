using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using CavernizeCLI.Commands;

namespace CavernizeCLI;

/// <summary>
/// Cross-platform CLI spatial audio processor using Cavern's renderer.
/// </summary>
public static class Program {
    /// <summary>
    /// Application version string.
    /// </summary>
    public const string Version = "0.0.1";

    /// <summary>
    /// Main entry point.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code: 0 for success, non-zero for errors</returns>
    public static int Main(string[] args) {
        try {
            if (args.Length == 0) {
                ShowUsage();
                return 1;
            }

            var commandParser = new CommandParser();
            return commandParser.Execute(args);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Display usage information.
    /// </summary>
    static void ShowUsage() {
        Console.WriteLine($"CavernizeCLI v{Version} - Cross-platform spatial audio processor");
        Console.WriteLine();
        Console.WriteLine("Usage: cavernize [options] -i <input> -o <output>");
        Console.WriteLine();
        Console.WriteLine("Basic Options:");
        Console.WriteLine("  -i, --input <file>     Input audio file");
        Console.WriteLine("  -o, --output <file>    Output audio file");
        Console.WriteLine("  -t, --target <layout>  Target speaker layout (e.g., 5.1, 7.1, 7.1.4)");
        Console.WriteLine("  -f, --format <format>  Output format:");
        Console.WriteLine("                         Channel-based: WAV, EAC3, AC3, FLAC");
        Console.WriteLine("                         Object-based: ADM-BWF, ADM-BWF-ATMOS, LAF");
        Console.WriteLine("  -h, --help             Show help information");
        Console.WriteLine("  -v, --version          Show version information");
        Console.WriteLine();    
        Console.WriteLine("Processing Options:");
        Console.WriteLine("  -u, --upconvert        Enable height generation from regular content");
        Console.WriteLine("  --mute-bed             Mute bed channels during upmixing");
        Console.WriteLine("  --mute-ground          Mute ground channels to create height-only output");
        Console.WriteLine("  --matrix <mode>        Matrix upmixing mode (0-5)");
        Console.WriteLine("  --virtualizer          Enable speaker virtualizer");
        Console.WriteLine("  --smoothness <value>   Set upmixing smoothness (0.0-1.0)");
        Console.WriteLine("  --force-24bit          Force 24-bit output");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  cavernize -i input.wav -o output.wav -t 7.1.4 -u");
        Console.WriteLine("  cavernize -i stereo.flac -o surround.eac3 -t 5.1 --matrix 2");
        Console.WriteLine("  cavernize -i movie.mkv -o processed.wav -t 7.1 --mute-bed");
        Console.WriteLine("  cavernize -i atmos.eac3 -o objects.wav -f ADM-ATMOS");
    }
} 