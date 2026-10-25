namespace Kastom.Core.Runtime;

internal static class AtomicFile
{
    /// <summary>
    /// Записати у .tmp, скинути на диск, потім замінити файл: обрив чи помилка посеред запису
    /// лишають стару версію файлу цілою.
    /// </summary>
    public static void WriteAllText(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // Тимчасовий файл лишиться; наступний запис його перезапише.
            }

            throw;
        }
    }
}
