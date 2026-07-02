using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AnkiNet;

static class AnkiExporter
{
    private const string DeckName = "Словарь";

    public static void Export(List<DictionaryEntry> entries)
    {
        var exportable = entries.Where(e => !string.IsNullOrWhiteSpace(e.Translation)).ToList();

        if (exportable.Count == 0)
        {
            MessageBox.Show("Словарь пуст, нечего экспортировать.", "Экранный переводчик");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Anki-колода (*.apkg)|*.apkg",
            FileName = $"anki_{DateTime.Now:yyyy-MM-dd_HH-mm}.apkg",
            InitialDirectory = Directory.GetCurrentDirectory(),
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var cardTypes = new[]
        {
            new AnkiCardType(
                Name: "Card 1",
                Ordinal: 0,
                QuestionFormat: "{{Front}}",
                AnswerFormat: "{{FrontSide}}<hr id=\"answer\">{{Back}}"),
        };

        var noteType = new AnkiNoteType(
            name: "Basic (словарь-переводчик)",
            cardTypes: cardTypes,
            fieldNames: new[] { "Front", "Back" });

        var collection = new AnkiCollection();
        var noteTypeId = collection.CreateNoteType(noteType);
        var deckId = collection.CreateDeck(DeckName);

        // Anki.NET не умеет вкладывать медиа в .apkg сам (пишет пустой манифест),
        // поэтому картинки добавляем руками ниже, распаковав уже написанный архив.
        var mediaFiles = new List<(string SourcePath, string DisplayName)>();

        foreach (var entry in exportable)
        {
            string? mediaFileName = null;
            if (!string.IsNullOrWhiteSpace(entry.ScreenshotPath) && File.Exists(entry.ScreenshotPath))
            {
                var ext = Path.GetExtension(entry.ScreenshotPath);
                mediaFileName = $"screenshot_{mediaFiles.Count}{ext}";
                mediaFiles.Add((entry.ScreenshotPath, mediaFileName));
            }

            collection.CreateNote(deckId, noteTypeId, BuildFront(entry), BuildBack(entry, mediaFileName));
        }

        AnkiFileWriter.WriteToFileAsync(dialog.FileName, collection).GetAwaiter().GetResult();

        if (mediaFiles.Count > 0)
        {
            EmbedMedia(dialog.FileName, mediaFiles);
        }

        MessageBox.Show($"Экспортировано {exportable.Count} карточек в файл {dialog.FileName}.", "Экранный переводчик");
    }

    private static void EmbedMedia(string apkgPath, List<(string SourcePath, string DisplayName)> mediaFiles)
    {
        using var archive = ZipFile.Open(apkgPath, ZipArchiveMode.Update);

        archive.GetEntry("media")?.Delete();

        var manifest = new Dictionary<string, string>();

        for (var i = 0; i < mediaFiles.Count; i++)
        {
            var (sourcePath, displayName) = mediaFiles[i];
            var key = i.ToString();
            manifest[key] = displayName;

            var entry = archive.CreateEntry(key);
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(sourcePath);
            fileStream.CopyTo(entryStream);
        }

        var mediaEntry = archive.CreateEntry("media");
        using var writer = new StreamWriter(mediaEntry.Open());
        writer.Write(JsonSerializer.Serialize(manifest));
    }

    private static string BuildFront(DictionaryEntry entry)
    {
        var translation = WebUtility.HtmlEncode(entry.Translation);

        return string.IsNullOrWhiteSpace(entry.Hint)
            ? translation
            : $"{translation} ({WebUtility.HtmlEncode(entry.Hint)})";
    }

    private static string BuildBack(DictionaryEntry entry, string? mediaFileName)
    {
        var parts = new List<string>();

        var headLine = BuildHeadLine(entry);
        if (!string.IsNullOrWhiteSpace(headLine))
        {
            parts.Add(headLine);
        }

        var exampleHtml = BuildExampleHtml(entry);
        if (!string.IsNullOrWhiteSpace(exampleHtml))
        {
            parts.Add(exampleHtml);
        }

        if (!string.IsNullOrWhiteSpace(entry.ContextTranslation))
        {
            parts.Add(WebUtility.HtmlEncode(entry.ContextTranslation));
        }

        if (mediaFileName is not null)
        {
            parts.Add($"<img src=\"{WebUtility.HtmlEncode(mediaFileName)}\">");
        }

        return string.Join("<br><br>", parts);
    }

    private static string BuildHeadLine(DictionaryEntry entry)
    {
        var pieces = new List<string> { WebUtility.HtmlEncode(entry.Word.Trim()) };

        if (!string.IsNullOrWhiteSpace(entry.Transcription))
        {
            pieces.Add(WebUtility.HtmlEncode(entry.Transcription));
        }

        var head = string.Join(" ", pieces);

        return string.IsNullOrWhiteSpace(entry.PartOfSpeech)
            ? head
            : $"{head} — {WebUtility.HtmlEncode(entry.PartOfSpeech)}";
    }

    private static string BuildExampleHtml(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Sentence))
        {
            return "";
        }

        var sentence = ExtractSingleSentence(entry.Sentence, entry.Word);
        return BoldWord(sentence, entry.Word);
    }

    // Режем по границам предложений (. ! ?) и берём то, где встречается целевое слово.
    // Если не получилось однозначно найти — возвращаем текст как есть, не ломаемся.
    private static string ExtractSingleSentence(string text, string word)
    {
        var cleanedWord = CleanWord(word);
        if (cleanedWord == "")
        {
            return text;
        }

        var sentences = Regex.Split(text.Trim(), @"(?<=[.!?])\s+");
        var wordPattern = new Regex($@"\b{Regex.Escape(cleanedWord)}\b", RegexOptions.IgnoreCase);

        foreach (var sentence in sentences)
        {
            if (wordPattern.IsMatch(sentence))
            {
                return sentence.Trim();
            }
        }

        return text;
    }

    private static string BoldWord(string sentence, string word)
    {
        var encoded = WebUtility.HtmlEncode(sentence);
        var cleanedWord = CleanWord(word);

        if (cleanedWord == "")
        {
            return encoded;
        }

        var pattern = new Regex($@"\b{Regex.Escape(cleanedWord)}\b", RegexOptions.IgnoreCase);
        return pattern.Replace(encoded, m => $"<b>{m.Value}</b>", 1);
    }

    private static string CleanWord(string word) =>
        Regex.Replace(word.Trim(), @"^[^\p{L}]+|[^\p{L}]+$", "");
}
