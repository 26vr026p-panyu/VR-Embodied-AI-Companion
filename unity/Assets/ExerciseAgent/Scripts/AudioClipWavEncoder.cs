using System;
using System.IO;
using UnityEngine;

public static class AudioClipWavEncoder
{
    public static byte[] Encode(
        AudioClip clip,
        int sampleFrames
    )
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));

        sampleFrames = Mathf.Clamp(
            sampleFrames,
            0,
            clip.samples
        );

        int channels = clip.channels;
        int sampleRate = clip.frequency;

        float[] samples =
            new float[sampleFrames * channels];

        clip.GetData(samples, 0);

        byte[] pcmData =
            new byte[samples.Length * 2];

        int offset = 0;

        foreach (float sample in samples)
        {
            float clamped =
                Mathf.Clamp(sample, -1f, 1f);

            short pcmSample =
                (short)(clamped * short.MaxValue);

            byte[] bytes =
                BitConverter.GetBytes(pcmSample);

            pcmData[offset++] = bytes[0];
            pcmData[offset++] = bytes[1];
        }

        using MemoryStream stream =
            new MemoryStream();

        using BinaryWriter writer =
            new BinaryWriter(stream);

        int byteRate =
            sampleRate * channels * 2;

        int blockAlign =
            channels * 2;

        // RIFF header
        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("RIFF")
        );

        writer.Write(36 + pcmData.Length);

        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("WAVE")
        );

        // fmt chunk
        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("fmt ")
        );

        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)16); // 16 bit

        // data chunk
        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("data")
        );

        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        writer.Flush();

        return stream.ToArray();
    }
}