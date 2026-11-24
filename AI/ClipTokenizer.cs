using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SandcastleRpgMaker.AI;

/// <summary>
/// Stable Diffusion 1.5's word reader: the CLIP byte-pair tokenizer (tokenizer/vocab.json + merges.txt), as the
/// openai/clip-vit-large-patch14 tokenizer does it - lower case, words split by the CLIP pattern, each word's bytes
/// merged by rank, "&lt;/w&gt;" ending a word. 77 ids: start, up to 75 words' pieces, end, then end again as padding.
/// </summary>
public sealed class ClipTokenizer
{
    public const int Length = 77;
    public const int StartId = 49406;
    public const int EndId = 49407;

    private static readonly Regex Pattern = new(@"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<(string, string), int> _ranks = new();
    private readonly Dictionary<string, string[]> _cache = new();
    private readonly char[] _byteToChar = new char[256];

    public ClipTokenizer(string tokenizerFolder)
    {
        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(tokenizerFolder, "vocab.json")))
                 ?? throw new InvalidDataException("tokenizer/vocab.json is empty");
        int rank = 0;
        foreach (var line in File.ReadLines(Path.Combine(tokenizerFolder, "merges.txt")))
        {
            if (line.StartsWith("#version") || line.Length == 0) continue;
            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            _ranks.TryAdd((line[..space], line[(space + 1)..]), rank++);
        }
        // GPT-2's bytes_to_unicode: printable bytes stand for themselves, the rest are moved up past 255.
        var printable = new List<int>();
        for (int b = '!'; b <= '~'; b++) printable.Add(b);
        for (int b = 0xA1; b <= 0xAC; b++) printable.Add(b);
        for (int b = 0xAE; b <= 0xFF; b++) printable.Add(b);
        int extra = 0;
        for (int b = 0; b < 256; b++) _byteToChar[b] = printable.Contains(b) ? (char)b : (char)(256 + extra++);
    }

    /// <summary>The 77 ids for a prompt (longer prompts are cut at 75 pieces, as Stable Diffusion does).</summary>
    public int[] Encode(string text)
    {
        var ids = new List<int> { StartId };
        string clean = Regex.Replace(text ?? "", @"\s+", " ").Trim().ToLowerInvariant();
        foreach (Match m in Pattern.Matches(clean))
        {
            var mapped = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(m.Value)) mapped.Append(_byteToChar[b]);
            foreach (var piece in Bpe(mapped.ToString()))
                if (_vocab.TryGetValue(piece, out int id)) ids.Add(id);
        }
        if (ids.Count > Length - 1) ids.RemoveRange(Length - 1, ids.Count - (Length - 1));
        ids.Add(EndId);
        while (ids.Count < Length) ids.Add(EndId);
        return ids.ToArray();
    }

    private string[] Bpe(string token)
    {
        if (_cache.TryGetValue(token, out var cached)) return cached;
        var word = token.Select(c => c.ToString()).ToList();
        word[^1] += "</w>";
        while (word.Count > 1)
        {
            int best = -1, bestRank = int.MaxValue;
            for (int i = 0; i < word.Count - 1; i++)
                if (_ranks.TryGetValue((word[i], word[i + 1]), out int r) && r < bestRank) { bestRank = r; best = i; }
            if (best < 0) break;
            string first = word[best], second = word[best + 1];
            var merged = new List<string>(word.Count);
            for (int i = 0; i < word.Count; )
            {
                if (i < word.Count - 1 && word[i] == first && word[i + 1] == second) { merged.Add(first + second); i += 2; }
                else merged.Add(word[i++]);
            }
            word = merged;
        }
        return _cache[token] = word.ToArray();
    }
}
