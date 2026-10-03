using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;

namespace PhotoKeepKill;

internal static class SoundEffects
{
    private const int SampleRate = 22050;
    private static readonly List<(MemoryStream Stream, SoundPlayer Player)> Players = [];
    private static readonly SoundPlayer Keep = Create([(659.25, 75), (880, 100)]);
    private static readonly SoundPlayer Trash = Create([(392, 90), (293.66, 125)]);
    private static readonly SoundPlayer Album = Create([(523.25, 65), (659.25, 65), (783.99, 105)]);
    private static readonly SoundPlayer Favorite = Create([(783.99, 65), (987.77, 85), (1174.66, 115)]);

    public static void PlayKeep() => Keep.Play();
    public static void PlayTrash() => Trash.Play();
    public static void PlayAlbum() => Album.Play();
    public static void PlayFavorite() => Favorite.Play();

    private static SoundPlayer Create((double Frequency, int DurationMs)[] notes)
    {
        var samples = new List<short>();
        foreach (var note in notes)
        {
            var count = SampleRate * note.DurationMs / 1000;
            for (var i = 0; i < count; i++)
            {
                var attack = Math.Min(1, i / (SampleRate * 0.008));
                var release = Math.Min(1, (count - i) / (SampleRate * 0.022));
                var envelope = Math.Min(attack, release);
                var wave = Math.Sin(2 * Math.PI * note.Frequency * i / SampleRate);
                samples.Add((short)(wave * envelope * 0.085 * short.MaxValue));
            }
            samples.AddRange(new short[SampleRate / 60]);
        }

        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.ASCII, leaveOpen: true))
        {
            var dataLength = samples.Count * sizeof(short);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
            writer.Write(SampleRate * sizeof(short)); writer.Write((short)sizeof(short)); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataLength);
            foreach (var sample in samples) writer.Write(sample);
        }
        var audio = new MemoryStream(bytes.ToArray());
        var player = new SoundPlayer(audio);
        player.Load();
        Players.Add((audio, player));
        return player;
    }
}
