using System;
using NAudio.Wave;

namespace RadiumLauncher.Services
{
    public static class AudioService
    {
        private static WaveOutEvent? output;
        private static AudioFileReader? reader;
        public static float Volume { get; private set; } = 1.0f;

        public static void Play(string path)
        {
            Stop();

            reader = new AudioFileReader(path);
            output = new WaveOutEvent();

            output.Init(reader);
            output.Volume = Volume;
            output.Play();
        }

        public static void SetVolume(float volume)
        {
            Volume = Math.Clamp(volume, 0f, 1f);
            if (output != null)
            {
                output.Volume = Volume;
            }
        }

        public static void Stop()
        {
            output?.Stop();
            output?.Dispose();
            output = null;

            reader?.Dispose();
            reader = null;
        }
    }
}