using System;
using System.Linq;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace GutsV.Core.Audio;

public class AudioAnalyzer : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private float _currentBassLevel;
    private readonly object _lock = new object();

    public float BassLevel
    {
        get { lock (_lock) return _currentBassLevel; }
    }

    public bool IsListening => _capture != null && _capture.CaptureState == CaptureState.Capturing;

    public void StartListening()
    {
        StopListening();

        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
        }
        catch (Exception)
        {
            // Handle initialization error (e.g. no audio device)
        }
    }

    public void StopListening()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Simple RMS calculation for volume/bass approximation
        // In a real FFT, we would transform data. For speed/simplicity, 
        // we serve the raw amplitude as a "beat" indicator for now.
        
        // 32-bit float audio is standard for Wasapi Loopback
        if (e.BytesRecorded == 0) return;

        float max = 0;
        var buffer = new WaveBuffer(e.Buffer);
        
        // Loop through samples (float)
        // Data is interleaved, checking max amplitude in this chunk
        for (int i = 0; i < e.BytesRecorded / 4; i++)
        {
            var sample = Math.Abs(buffer.FloatBuffer[i]);
            if (sample > max) max = sample;
        }

        // Smooth decay for visual pleasantness
        lock (_lock)
        {
            // Quick attack, slow release logic
            if (max > _currentBassLevel)
                _currentBassLevel = max; 
            else
                _currentBassLevel -= 0.05f; // Decay factor

            if (_currentBassLevel < 0) _currentBassLevel = 0;
        }
    }

    public void Dispose()
    {
        StopListening();
    }
}
