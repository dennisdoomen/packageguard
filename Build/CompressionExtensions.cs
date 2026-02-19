using Nuke.Common.IO;
using SharpCompress.Readers;
using System.IO;

public static class CompressionExtensions
{
    public static void UnTarXzTo(this AbsolutePath archive, AbsolutePath directory)
    {
        using Stream stream = File.OpenRead(archive);

        using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions
        {
            ExtractFullPath = true,
            Overwrite = true
        });

        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            reader.WriteEntryToDirectory(directory);
        }
    }
}
