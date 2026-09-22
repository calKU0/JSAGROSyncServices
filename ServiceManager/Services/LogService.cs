using ServiceManager.Enums;
using ServiceManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceManager.Services
{
    public class LogService
    {
        public async Task<List<LogFileItem>> GetLogFilesAsync(string logFolderPath)
        {
            if (string.IsNullOrWhiteSpace(logFolderPath) || !Directory.Exists(logFolderPath))
                return new List<LogFileItem>();

            return await Task.Run(() =>
            {
                return Directory.GetFiles(logFolderPath, "*.txt")
                    .Select(CreateLogFileItem)
                    .OfType<LogFileItem>()
                    .OrderByDescending(item => item.Date)
                    .ToList();
            });
        }

        public LogLine ParseLogLine(string line)
        {
            var level = LogLevel.Information;
            if (line.Contains("ERR]", StringComparison.Ordinal)) level = LogLevel.Error;
            else if (line.Contains("WRN]", StringComparison.Ordinal)) level = LogLevel.Warning;
            return new LogLine { Level = level, Message = line };
        }

        private static readonly byte[] WarningMarker = Encoding.ASCII.GetBytes("WRN]");
        private static readonly byte[] ErrorMarker = Encoding.ASCII.GetBytes("ERR]");

        /// <summary>
        /// Zlicza wystapienia dwoch znacznikow w pliku, czytajac go blokami.
        /// Znaczniki sa czysto ASCII, wiec skanowanie bajtow jest bezpieczne dla UTF-8.
        /// </summary>
        private static (int Warnings, int Errors) CountMarkers(string filePath)
        {
            const int overlap = 3; // najdluzszy znacznik ma 4 bajty

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);

            var buffer = new byte[64 * 1024];
            int warnings = 0, errors = 0, carried = 0, read;

            while ((read = stream.Read(buffer, carried, buffer.Length - carried)) > 0)
            {
                var span = buffer.AsSpan(0, carried + read);

                warnings += Count(span, WarningMarker);
                errors += Count(span, ErrorMarker);

                // Znacznik moze sie rozjechac miedzy blokami - przenosimy ogon na poczatek.
                carried = Math.Min(overlap, span.Length);
                span[^carried..].CopyTo(buffer);
            }

            return (warnings, errors);
        }

        private static int Count(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        {
            int count = 0, index;

            while ((index = haystack.IndexOf(needle)) >= 0)
            {
                count++;
                haystack = haystack[(index + needle.Length)..];
            }

            return count;
        }

        private static LogFileItem? CreateLogFileItem(string filePath)
        {
            int warnings = 0;
            int errors = 0;

            try
            {
                // Logi dzienne potrafia miec setki MB, a interesuja nas tylko dwa znaczniki -
                // liczymy je na strumieniu bajtow, bez dekodowania i alokacji linii.
                (warnings, errors) = CountMarkers(filePath);

                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string datePart = fileName.Replace("log-", "");

                string formattedDate = fileName;
                DateTime? parsedDate = null;
                if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, DateTimeStyles.None, out DateTime dt))
                {
                    formattedDate = dt.ToString("dd.MM.yyyy");
                    parsedDate = dt;
                }

                return new LogFileItem
                {
                    Name = formattedDate,
                    Path = filePath,
                    WarningsCount = warnings,
                    ErrorsCount = errors,
                    Date = parsedDate ?? DateTime.MinValue
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
