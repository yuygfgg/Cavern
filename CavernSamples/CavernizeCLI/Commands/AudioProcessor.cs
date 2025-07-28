using System;
using System.IO;
using System.Numerics;

using Cavern;
using Cavern.Channels;
using Cavern.Format;
using Cavern.Format.Common;
using Cavern.Format.Environment;
using Cavern.Format.Renderers;
using Cavern.Utilities;
using Cavern.Virtualizer;
using Cavern.CavernSettings;
using Cavern.Filters;

using Cavernize.Logic.Models;
using Cavernize.Logic.Models.RenderTargets;

namespace CavernizeCLI.Commands;

/// <summary>
/// Performs audio processing using Cavern's spatial renderer.
/// </summary>
public class AudioProcessor {
    /// <summary>
    /// The audio track to process.
    /// </summary>
    readonly CavernizeTrack track;

    /// <summary>
    /// Processing options.
    /// </summary>
    readonly CommandParser.ProcessingOptions options;

    /// <summary>
    /// Cavern listener for spatial processing.
    /// </summary>
    readonly Listener listener;

    /// <summary>
    /// Target render configuration.
    /// </summary>
    readonly RenderTarget renderTarget;

    /// <summary>
    /// Creates an audio processor for the specified track and options.
    /// </summary>
    /// <param name="track">Audio track to process</param>
    /// <param name="options">Processing options</param>
    public AudioProcessor(CavernizeTrack track, CommandParser.ProcessingOptions options) {
        this.track = track ?? throw new ArgumentNullException(nameof(track));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        
        listener = new Listener();
        listener.SampleRate = track.SampleRate;
        
        renderTarget = CreateRenderTarget();
    }

    /// <summary>
    /// Process the audio and write to output file.
    /// </summary>
    /// <returns>Exit code: 0 for success, non-zero for errors</returns>
    public int Process() {
        try {
            if (!options.Quiet) {
                Console.WriteLine("Initializing audio processing...");
            }
            
            if (options.OutputFormat.IsEnvironmental()) {
                PrepareRendererForEnvironmental();
                if (!options.Quiet) {
                    Console.WriteLine("Processing audio...");
                }
                return ProcessEnvironmentalFormat();
            } else {
                PrepareRenderer();
                if (!options.Quiet) {
                    Console.WriteLine("Processing audio...");
                }
                return ProcessChannelBasedFormat();
            }
            
        } catch (Exception ex) {
            Console.Error.WriteLine($"Processing failed: {ex.Message}");
            return 1;
        }
    }
    
    /// <summary>
    /// Process audio for channel-based formats (PCM, E-AC-3, etc.).
    /// </summary>
    /// <returns>Exit code: 0 for success, non-zero for errors</returns>
    int ProcessChannelBasedFormat() {
        // Create output writer
        using var writer = CreateOutputWriter();
        if (writer == null) {
            Console.Error.WriteLine("Failed to create output writer.");
            return 1;
        }
        
        writer.WriteHeader();
        
        RenderAudio(writer);
        
        if (!options.Quiet) {
            Console.WriteLine("Processing completed successfully.");
        }
        return 0;
    }
    
    /// <summary>
    /// Process audio for environmental/object-based formats (ADM BWF, DAMF, etc.).
    /// </summary>
    /// <returns>Exit code: 0 for success, non-zero for errors</returns>
    int ProcessEnvironmentalFormat() {
        if (!options.Quiet) {
            Console.WriteLine($"Processing as environmental format: {options.OutputFormat}");
        }
        
        var environmentWriter = CreateEnvironmentWriter();
        if (environmentWriter == null) {
            Console.Error.WriteLine("Failed to create environment writer.");
            return 1;
        }

        try {
            RenderEnvironmentalAudio(environmentWriter);
            
            if (!options.Quiet) {
                Console.WriteLine("Environmental processing completed successfully.");
            }
            return 0;
        } finally {
            environmentWriter?.Dispose();
        }
    }

    /// <summary>
    /// Configure the Cavern listener based on target layout.
    /// </summary>
    void ConfigureListener() {
        Channel[] targetChannels = ParseTargetLayout(options.TargetLayout);
        Listener.ReplaceChannels(targetChannels);
        
        listener.SampleRate = track.SampleRate;
        
        if (options.EnableVirtualizer) {
            try {
                VirtualizerFilter.SetupForSpeakers();
                listener.SampleRate = VirtualizerFilter.FilterSampleRate;
            } catch (Exception ex) {
                if (!options.Quiet) {
                    Console.WriteLine($"Warning: Could not setup virtualizer: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Parse target layout string into channel array.
    /// </summary>
    /// <param name="layout">Layout string (e.g., "5.1", "7.1.4")</param>
    /// <returns>Array of channels</returns>
    Channel[] ParseTargetLayout(string layout) {
        ReferenceChannel[] refChannels = ParseTargetLayoutToReference(layout);
        return ChannelPrototype.ToLayout(refChannels);
    }

    /// <summary>
    /// Create the appropriate render target based on options.
    /// </summary>
    /// <returns>Configured render target</returns>
    RenderTarget CreateRenderTarget() {
        // For now, create a simple channel-based render target using ReferenceChannel array
        ReferenceChannel[] refChannels = ParseTargetLayoutToReference(options.TargetLayout);
        return new RenderTarget("CLI Target", refChannels);
    }
    
    /// <summary>
    /// Parse target layout string into ReferenceChannel array.
    /// </summary>
    /// <param name="layout">Layout string (e.g., "5.1", "7.1.4")</param>
    /// <returns>Array of reference channels</returns>
    ReferenceChannel[] ParseTargetLayoutToReference(string layout) {
        return layout.ToLowerInvariant() switch {
            "2.0" or "stereo" => ChannelPrototype.ref200,
            "5.1" => ChannelPrototype.ref510,
            "7.1" => ChannelPrototype.ref710,
            "7.1.4" => ChannelPrototype.ref714,
            "5.1.4" => ChannelPrototype.ref514,
            "9.1.6" => ChannelPrototype.ref916,
            _ => throw new ArgumentException($"Unsupported target layout: {layout}")
        };
    }

    /// <summary>
    /// Prepare the renderer for processing.
    /// </summary>
    void PrepareRenderer() {
        ConfigureListener();
        
        renderTarget.Apply(false);

        listener.DetachAllSources();
        
        var upmixingSettings = new UpmixingSettings(false) {
            Cavernize = options.Upconvert,
            MatrixUpmixing = options.MatrixMode > 0,
            Smoothness = options.Smoothness
        };
        
        track.Attach(listener, upmixingSettings);
        
        listener.Volume = 1.0f;
    }
    
    /// <summary>
    /// Prepare the renderer for environmental formats.
    /// </summary>
    void PrepareRendererForEnvironmental() {
        listener.SampleRate = track.SampleRate;
        
        if (options.EnableVirtualizer) {
            try {
                VirtualizerFilter.SetupForSpeakers();
                listener.SampleRate = VirtualizerFilter.FilterSampleRate;
            } catch (Exception ex) {
                if (!options.Quiet) {
                    Console.WriteLine($"Warning: Could not setup virtualizer: {ex.Message}");
                }
            }
        }
        
        listener.DetachAllSources();
        
        var upmixingSettings = new UpmixingSettings(false) {
            Cavernize = options.Upconvert,
            MatrixUpmixing = options.MatrixMode > 0,
            Smoothness = options.Smoothness
        };
        
        track.Attach(listener, upmixingSettings);
        
        listener.Volume = 1.0f;
        
        if (!options.Quiet) {
            Console.WriteLine($"Environmental format setup: {listener.ActiveSources.Count} sources, {Listener.Channels.Length} total channels");
        }
    }

    /// <summary>
    /// Create the output audio writer based on format and options.
    /// </summary>
    /// <returns>Configured audio writer</returns>
    AudioWriter CreateOutputWriter() {
        var bitDepth = options.OutputFormat == Codec.PCM_Float ? BitDepth.Float32 : 
                       options.Force24Bit ? BitDepth.Int24 : BitDepth.Int16;
        
        int channelCount = renderTarget.OutputChannels;
        
        string extension = Path.GetExtension(options.OutputFile).ToLowerInvariant();
        
        if (extension == ".wav" && options.OutputFormat == Codec.PCM_LE) {
            return new RIFFWaveWriter(options.OutputFile, renderTarget.Channels[..channelCount],
                track.Length, listener.SampleRate, bitDepth);
        }
        
        return AudioWriter.Create(options.OutputFile, channelCount, track.Length, 
            listener.SampleRate, bitDepth);
    }
    
    /// <summary>
    /// Create the environment writer for object-based formats.
    /// </summary>
    /// <returns>Configured environment writer</returns>
    EnvironmentWriter CreateEnvironmentWriter() {
        var bitDepth = options.OutputFormat == Codec.PCM_Float ? BitDepth.Float32 : 
                       options.Force24Bit ? BitDepth.Int24 : BitDepth.Int16;
        
        return options.OutputFormat switch {
            Codec.LimitlessAudio => new LimitlessAudioFormatEnvironmentWriter(options.OutputFile, listener, track.Length, bitDepth),
            Codec.ADM_BWF => new BroadcastWaveFormatWriter(options.OutputFile, listener, track.Length, bitDepth),
            Codec.ADM_BWF_Atmos => CreateDolbyAtmosBWFWriter(bitDepth),
            _ => throw new ArgumentException($"Unsupported environment format: {options.OutputFormat}")
        };
    }
    
    /// <summary>
    /// Create Dolby Atmos BWF writer with proper static objects handling.
    /// </summary>
    /// <param name="bitDepth">Output bit depth</param>
    /// <returns>Configured Dolby Atmos BWF writer</returns>
    DolbyAtmosBWFWriter CreateDolbyAtmosBWFWriter(BitDepth bitDepth) {
        // Extract static objects if available from E-AC-3 renderer
        (ReferenceChannel, Source)[] staticObjects = [];
        
        if (track.Renderer is EnhancedAC3Renderer eac3 && eac3.HasObjects) {
            var staticChannels = eac3.GetStaticChannels();
            var allObjects = eac3.Objects;
            staticObjects = new (ReferenceChannel, Source)[staticChannels.Length];
            for (int i = 0; i < staticChannels.Length; i++) {
                staticObjects[i] = (staticChannels[i], allObjects[i]);
            }
        }
        
        return new DolbyAtmosBWFWriter(options.OutputFile, listener, track.Length, bitDepth, staticObjects);
    }
    
    /// <summary>
    /// Render audio for environmental/object-based formats.
    /// </summary>
    /// <param name="environmentWriter">The environment writer to use</param>
    void RenderEnvironmentalAudio(EnvironmentWriter environmentWriter) {
        long totalSamples = track.Length;
        long renderedSamples = 0;
        int lastProgressPercent = -1;
        
        if (!options.Quiet) {
            Console.WriteLine($"Rendering environmental audio format: {options.OutputFormat}");
            Console.WriteLine($"Total samples to process: {totalSamples}");
        }
        
        if (environmentWriter is BroadcastWaveFormatWriter bwfWriter) {
            RenderBroadcastWaveFormat(bwfWriter, totalSamples);
            return;
        }
        
        while (renderedSamples < totalSamples) {
            environmentWriter.WriteNextFrame();
            
            renderedSamples += listener.UpdateRate;
            
            int progressPercent = (int)((renderedSamples * 100) / totalSamples);
            if (!options.Quiet && progressPercent != lastProgressPercent && progressPercent % 5 == 0) {
                Console.WriteLine($"Progress: {progressPercent}%");
                lastProgressPercent = progressPercent;
            }
            
            if (renderedSamples >= totalSamples) {
                break;
            }
        }
        
        if (!options.Quiet) {
            Console.WriteLine("Progress: 100%");
            Console.WriteLine("Finalizing environmental format export...");
        }
    }
    
    /// <summary>
    /// Render ADM BWF format with proper progress handling.
    /// </summary>
    /// <param name="bwfWriter">BWF writer instance</param>
    /// <param name="totalSamples">Total samples to process</param>
    void RenderBroadcastWaveFormat(BroadcastWaveFormatWriter bwfWriter, long totalSamples) {
        if (!options.Quiet) {
            Console.WriteLine("Rendering ADM BWF format with metadata generation...");
        }
        
        long renderedSamples = 0;
        int lastProgressPercent = -1;
        
        // 95% for audio, 5% for metadata
        const double progressSplit = 0.95;
        long audioProcessingSamples = (long)(totalSamples * progressSplit);
        
        while (renderedSamples < totalSamples) {
            bwfWriter.WriteNextFrame();
            renderedSamples += listener.UpdateRate;
            
            int progressPercent;
            if (renderedSamples <= audioProcessingSamples) {
                progressPercent = (int)((renderedSamples * progressSplit * 100) / totalSamples);
            } else {
                progressPercent = (int)(progressSplit * 100 + ((renderedSamples - audioProcessingSamples) * 5) / (totalSamples - audioProcessingSamples));
            }
            
            if (!options.Quiet && progressPercent != lastProgressPercent && progressPercent % 5 == 0) {
                Console.WriteLine($"Progress: {progressPercent}%");
                lastProgressPercent = progressPercent;
            }
            
            if (renderedSamples >= totalSamples) {
                break;
            }
        }
        
        bwfWriter.FinalFeedback = (progress) => {
            int finalProgress = (int)(progressSplit * 100 + progress * 5);
            if (!options.Quiet && finalProgress == 100) {
                Console.WriteLine("Progress: 100%");
                Console.WriteLine("ADM metadata generation completed.");
            }
        };
        bwfWriter.FinalFeedbackStart = progressSplit;
        
        if (!options.Quiet) {
            Console.WriteLine("Finalizing ADM BWF format with metadata...");
        }
    }

    /// <summary>
    /// Perform the actual audio rendering.
    /// </summary>
    /// <param name="writer">Output audio writer</param>
    void RenderAudio(AudioWriter writer) {
        const int defaultBlockSize = 16384;
        int blockSize = defaultBlockSize;
        
        if (blockSize < listener.UpdateRate) {
            blockSize = listener.UpdateRate;
        } else if (blockSize % listener.UpdateRate != 0) {
            blockSize += listener.UpdateRate - blockSize % listener.UpdateRate;
        }
        
        blockSize *= renderTarget.OutputChannels;
        
        long totalSamples = track.Length;
        long renderedSamples = 0;
        int lastProgressPercent = -1;
        
        float[] writeCache = new float[blockSize / renderTarget.OutputChannels * Listener.Channels.Length];
        int cachePosition = 0;
        
        VirtualizerFilter virtualizer = null;
        Normalizer normalizer = null;
        if (options.EnableVirtualizer) {
            virtualizer = new VirtualizerFilter();
            virtualizer.SetLayout();
            normalizer = new Normalizer(true) {
                decayFactor = 10 * (float)listener.UpdateRate / listener.SampleRate
            };
        }
        
        while (renderedSamples < totalSamples) {
            float[] result = listener.Render();
            
            ApplyCustomMuting(result);
            
            Array.Copy(result, 0, writeCache, cachePosition, result.Length);
            cachePosition += result.Length;
            
            bool shouldFlush = renderedSamples + listener.UpdateRate >= totalSamples;
            
            if (cachePosition >= writeCache.Length || shouldFlush) {
                if (virtualizer == null) {
                    writer.WriteBlock(writeCache, 0, cachePosition);
                } else {
                    virtualizer.Process(writeCache, listener.SampleRate);
                    normalizer.Process(writeCache);
                    writer.WriteChannelLimitedBlock(writeCache, renderTarget.OutputChannels,
                        Listener.Channels.Length, 0, cachePosition);
                }
                cachePosition = 0;
            }
            
            renderedSamples += listener.UpdateRate;
            
            int progressPercent = (int)((renderedSamples * 100) / totalSamples);
            if (!options.Quiet && progressPercent != lastProgressPercent && progressPercent % 5 == 0) {
                Console.WriteLine($"Progress: {progressPercent}%");
                lastProgressPercent = progressPercent;
            }
        }
        
        if (!options.Quiet) {
            Console.WriteLine("Progress: 100%");
        }
    }

    /// <summary>
    /// Apply custom muting options.
    /// </summary>
    /// <param name="result">Audio buffer to potentially modify</param>
    void ApplyCustomMuting(float[] result) {
        if (!options.MuteBed && !options.MuteGround) {
            return;
        }
        
        var objects = track.Renderer?.Objects;
        if (objects == null || objects.Count == 0) {
            return;
        }
        
        for (int i = 0, c = objects.Count; i < c; ++i) {
            var source = objects[i];
            
            Vector3 rawPos = source.Position / Listener.EnvironmentSize;

            bool shouldMuteBed = options.MuteBed && 
                                 (MathF.Abs(rawPos.X % 1) < 0.01f &&
                                  MathF.Abs(rawPos.Y % 1) < 0.01f && 
                                  MathF.Abs(rawPos.Z % 1) < 0.01f);
                                  
            bool shouldMuteGround = options.MuteGround && rawPos.Y == 0;
            
            source.Mute = shouldMuteBed || shouldMuteGround;
        }
    }
}