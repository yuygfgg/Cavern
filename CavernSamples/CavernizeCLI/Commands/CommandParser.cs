using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cavern;
using Cavern.Channels;
using Cavern.Format.Common;

using Cavernize.Logic.Models;
using Cavernize.Logic.Language;

namespace CavernizeCLI.Commands;

/// <summary>
/// Parses command line arguments and executes the audio processing.
/// </summary>
public class CommandParser {
    /// <summary>
    /// Processing options parsed from command line.
    /// </summary>
    public class ProcessingOptions {
        public string InputFile { get; set; }
        public string OutputFile { get; set; }
        public string TargetLayout { get; set; } = "7.1";
        public Codec OutputFormat { get; set; } = Codec.PCM_LE;
        public bool Upconvert { get; set; } = false;
        public bool MuteBed { get; set; } = false;
        public bool MuteGround { get; set; } = false;
        public int MatrixMode { get; set; } = 0;
        public bool EnableVirtualizer { get; set; } = false;
        public float Smoothness { get; set; } = 0.5f;
        public bool Force24Bit { get; set; } = false;
        public bool ShowHelp { get; set; } = false;
        public bool ShowVersion { get; set; } = false;
    }

    /// <summary>
    /// Execute command line processing.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code</returns>
    public int Execute(string[] args) {
        var options = ParseArguments(args);

        if (options.ShowVersion) {
            Console.WriteLine($"CavernizeCLI v{Program.Version}");
            return 0;
        }

        if (options.ShowHelp) {
            ShowDetailedHelp();
            return 0;
        }

        if (string.IsNullOrEmpty(options.InputFile)) {
            Console.Error.WriteLine("Error: Input file is required. Use -i or --input to specify.");
            return 1;
        }

        if (string.IsNullOrEmpty(options.OutputFile)) {
            Console.Error.WriteLine("Error: Output file is required. Use -o or --output to specify.");
            return 1;
        }

        if (!File.Exists(options.InputFile)) {
            Console.Error.WriteLine($"Error: Input file '{options.InputFile}' does not exist.");
            return 1;
        }

        try {
            return ProcessAudio(options);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Processing failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Parse command line arguments into processing options.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Parsed options</returns>
    ProcessingOptions ParseArguments(string[] args) {
        var options = new ProcessingOptions();

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i].ToLowerInvariant();

            switch (arg) {
                case "-i":
                case "--input":
                    if (i + 1 >= args.Length) {
                        throw new ArgumentException("Input file path is required after -i/--input");
                    }
                    options.InputFile = args[++i];
                    break;

                case "-o":
                case "--output":
                    if (i + 1 >= args.Length) {
                        throw new ArgumentException("Output file path is required after -o/--output");
                    }
                    options.OutputFile = args[++i];
                    break;

                case "-t":
                case "--target":
                    if (i + 1 >= args.Length) {
                        throw new ArgumentException("Target layout is required after -t/--target");
                    }
                    options.TargetLayout = args[++i];
                    break;

                case "-f":
                case "--format":
                    if (i + 1 >= args.Length) {
                        throw new ArgumentException("Output format is required after -f/--format");
                    }
                    options.OutputFormat = ParseOutputFormat(args[++i]);
                    break;

                case "-u":
                case "--upconvert":
                    options.Upconvert = true;
                    break;

                case "--mute-bed":
                    options.MuteBed = true;
                    break;

                case "--mute-ground":
                    options.MuteGround = true;
                    break;

                case "--matrix":
                    if (i + 1 >= args.Length) {
                        throw new ArgumentException("Matrix mode value is required after --matrix");
                    }
                    if (!int.TryParse(args[++i], out int matrixMode) || matrixMode < 0 || matrixMode > 5) {
                        throw new ArgumentException("Matrix mode must be an integer between 0 and 5");
                    }
                    options.MatrixMode = matrixMode;
                    break;

                case "--virtualizer":
                    options.EnableVirtualizer = true;
                    break;

                case "--smoothness":
                    if (i + 1 >= args.Length) {
                        throw new ArgumentException("Smoothness value is required after --smoothness");
                    }
                    if (!float.TryParse(args[++i], out float smoothness) || smoothness < 0.0f || smoothness > 1.0f) {
                        throw new ArgumentException("Smoothness must be a float between 0.0 and 1.0");
                    }
                    options.Smoothness = smoothness;
                    break;

                case "--force-24bit":
                    options.Force24Bit = true;
                    break;

                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;

                case "-v":
                case "--version":
                    options.ShowVersion = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        return options;
    }

    /// <summary>
    /// Parse output format from string.
    /// </summary>
    /// <param name="format">Format string</param>
    /// <returns>Codec enum value</returns>
    Codec ParseOutputFormat(string format) {
        return format.ToUpperInvariant() switch {
            "WAV" or "PCM" => Codec.PCM_LE,
            "EAC3" or "E-AC-3" => Codec.EnhancedAC3,
            "AC3" => Codec.AC3,
            "FLAC" => Codec.FLAC,
            "ADM" or "ADM-BWF" or "ADM_BWF" => Codec.ADM_BWF,
            "ADM-ATMOS" or "ADM-BWF-ATMOS" => Codec.ADM_BWF_Atmos,
            "LAF" or "LIMITLESS" => Codec.LimitlessAudio,
            _ => throw new ArgumentException($"Unsupported output format: {format}")
        };
    }

    /// <summary>
    /// Process the audio file with the specified options.
    /// </summary>
    /// <param name="options">Processing options</param>
    /// <returns>Exit code</returns>
    int ProcessAudio(ProcessingOptions options) {
        Console.WriteLine($"Processing: {options.InputFile} -> {options.OutputFile}");
        Console.WriteLine($"Target Layout: {options.TargetLayout}");
        Console.WriteLine($"Output Format: {options.OutputFormat}");

        if (options.Upconvert) {
            Console.WriteLine("- Upconversion enabled");
        }
        if (options.MuteBed) {
            Console.WriteLine("- Bed channels muted");
        }
        if (options.MuteGround) {
            Console.WriteLine("- Ground channels muted");
        }
        if (options.EnableVirtualizer) {
            Console.WriteLine("- Speaker virtualizer enabled");
        }

        Console.WriteLine();

        var languageStrings = new TrackStrings();

        using var audioFile = new AudioFile(options.InputFile, languageStrings);
        
        if (audioFile.Tracks.Count == 0) {
            Console.Error.WriteLine("No supported audio tracks found in the input file.");
            return 1;
        }

        // Use the first supported track
        var track = audioFile.Tracks.FirstOrDefault(t => t.Supported);
        if (track == null) {
            Console.Error.WriteLine("No supported audio tracks found in the input file.");
            return 1;
        }

        Console.WriteLine($"Using track: {track.FormatHeader}");
        Console.WriteLine($"Sample Rate: {track.SampleRate} Hz");
        Console.WriteLine($"Length: {track.Length} samples");
        Console.WriteLine();

        var processor = new AudioProcessor(track, options);
        return processor.Process();
    }

    /// <summary>
    /// Show detailed help information.
    /// </summary>
    void ShowDetailedHelp() {
        Console.WriteLine($"CavernizeCLI v{Program.Version} - Cross-platform spatial audio processor");
        Console.WriteLine();
        Console.WriteLine("This tool processes audio files using Cavern's spatial audio renderer,");
        Console.WriteLine("enabling upmixing, spatial processing, and format conversion.");
        Console.WriteLine();
        // Show same usage as in Program.cs but with more detailed explanations
        Console.WriteLine("Usage: cavernize [options] -i <input> -o <output>");
        Console.WriteLine();
        Console.WriteLine("Required Options:");
        Console.WriteLine("  -i, --input <file>     Input audio file (WAV, FLAC, MP4, MKV, etc.)");
        Console.WriteLine("  -o, --output <file>    Output audio file");
        Console.WriteLine();
        Console.WriteLine("Target Layout Options:");
        Console.WriteLine("  -t, --target <layout>  Target speaker layout:");
        Console.WriteLine("                         - Stereo: 2.0");
        Console.WriteLine("                         - Surround: 5.1, 7.1");
        Console.WriteLine("                         - Immersive: 5.1.4, 7.1.4, 9.1.6");
        Console.WriteLine();
        Console.WriteLine("Output Format Options:");
        Console.WriteLine("  -f, --format <format>  Output format:");
        Console.WriteLine("                         Channel-based formats:");
        Console.WriteLine("                         - WAV: Uncompressed PCM");
        Console.WriteLine("                         - EAC3: Enhanced AC-3 (Dolby Digital Plus)");
        Console.WriteLine("                         - AC3: AC-3 (Dolby Digital)");
        Console.WriteLine("                         - FLAC: Free Lossless Audio Codec");
        Console.WriteLine("                         Object-based formats:");
        Console.WriteLine("                         - ADM-BWF: Audio Definition Model BWF");
        Console.WriteLine("                         - ATMOS: Dolby Atmos BWF format");
        Console.WriteLine("                         - LAF: Limitless Audio Format");
        Console.WriteLine();
        Console.WriteLine("Object-based formats preserve spatial object information and metadata,");
        Console.WriteLine("suitable for professional post-production and immersive audio workflows.");
        Console.WriteLine();
        Console.WriteLine("For more information, visit: https://cavern.sbence.hu/");
    }
}