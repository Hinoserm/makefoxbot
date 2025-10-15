#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace makefoxsrv.llm.functions
{
    internal class ImageLoraInfo
    {
        private static readonly object _idfLock = new();
        private static Dictionary<string, double>? _idf; // token -> idf weight
        private static int _idfDocCount;

        [LLMFunction("Helps you find a LORA to meet the user's request.")]
        public static async Task<List<(string modelName, string description)>> SearchLORAs(
            FoxTelegram t,
            FoxUser user,
            [LLMParam("A short list of keywords to help the search engine. Can be left null for an extensive search.")] List<string>? keywords
        )
        {
            var allLoras = FoxLORAs.GetAllLORAs();

            if (allLoras is null || allLoras.Count == 0)
                throw new Exception("No LORAs available");

            EnsureIdfBuilt(allLoras);

            var normalizedKeywords = (keywords ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToList();

            // If no keywords, return 10 random
            if (normalizedKeywords.Count == 0)
                throw new Exception("No keywords provided");

            var results = new List<(string modelName, string description)>();
            var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kw in normalizedKeywords)
            {
                var scored = new List<(double Score, FoxLORAs.LoraInfo Info)>();
                foreach (var lora in allLoras)
                {
                    double score = MatchScore(lora, kw);
                    if (score > 0)
                        scored.Add((score, lora));
                }

                var top = scored
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.Info.Name ?? s.Info.Filename)
                    .Take(5)
                    .Select(s => (
                        loraName: s.Info.Filename ?? "[unnamed]",
                        description: BuildDescription(s.Info),
                        key: s.Info.Filename ?? s.Info.Hash ?? (s.Info.Name ?? "")
                    ));

                foreach (var x in top)
                {
                    if (x.key != null && globalSeen.Add(x.key))
                        results.Add((x.loraName, x.description));
                }
            }

            return results;
        }

        // Builds IDF cache
        private static void EnsureIdfBuilt(IReadOnlyCollection<FoxLORAs.LoraInfo> all)
        {
            if (_idf != null && _idfDocCount == all.Count) return;

            lock (_idfLock)
            {
                if (_idf != null && _idfDocCount == all.Count) return;

                var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in all)
                {
                    var docTokens = CollectDocTokens(l);
                    foreach (var tok in docTokens)
                        df[tok] = (df.TryGetValue(tok, out var c) ? c + 1 : 1);
                }

                int N = Math.Max(1, all.Count);
                _idf = df.ToDictionary(
                    kv => kv.Key,
                    kv => Math.Log((N + 1.0) / (kv.Value + 1.0)) + 1.0,
                    StringComparer.OrdinalIgnoreCase);
                _idfDocCount = all.Count;
            }
        }

        private static HashSet<string> CollectDocTokens(FoxLORAs.LoraInfo l)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void add(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                foreach (var tok in TokenizeWords(s))
                    set.Add(tok);
            }

            add(l.Filename);
            add(l.Name);
            add(l.Alias);
            add(l.BaseModel);
            add(l.Description);
            if (l.TriggerWords != null)
                foreach (var tw in l.TriggerWords)
                    add(tw);

            return set;
        }

        private static IEnumerable<string> TokenizeWords(string input)
        {
            foreach (Match m in Regex.Matches(input, @"[\p{L}\p{N}]+"))
                yield return m.Value.ToLowerInvariant();
        }

        private static double MatchScore(FoxLORAs.LoraInfo info, string keyword)
        {
            var kwTokens = TokenizeWords(keyword).ToArray();
            if (kwTokens.Length == 0) return 0;

            double score = 0;

            // Field weights
            const double W_FILENAME = 5.0;
            const double W_NAME = 4.5;
            const double W_ALIAS = 4.5;
            const double W_TRIGGER = 4.0;
            const double W_BASE = 1.5;
            const double W_DESC = 1.0;

            double SumTokenHits(string? field, double w)
            {
                if (string.IsNullOrWhiteSpace(field)) return 0;
                var fieldTokens = new HashSet<string>(TokenizeWords(field));
                double s = 0;
                foreach (var t in kwTokens)
                {
                    double idf = (_idf != null && _idf.TryGetValue(t, out var val)) ? val : 1.0;
                    if (fieldTokens.Contains(t))
                        s += w * idf;
                }
                // small phrase bonus if all tokens exist
                if (kwTokens.All(fieldTokens.Contains))
                    s += 0.5 * w;
                return s;
            }

            score += SumTokenHits(info.Filename, W_FILENAME);
            score += SumTokenHits(info.Name, W_NAME);
            score += SumTokenHits(info.Alias, W_ALIAS);
            score += SumTokenHits(info.BaseModel, W_BASE);
            score += SumTokenHits(info.Description, W_DESC);

            if (info.TriggerWords != null)
            {
                foreach (var trig in info.TriggerWords)
                    score += SumTokenHits(trig, W_TRIGGER);
            }

            return score;
        }

        private static string BuildDescription(FoxLORAs.LoraInfo info)
        {
            string desc = info.Description ?? "No description provided.";
            if (info.Name is not null)
                desc += $" Friendly name: {info.Name}";

            if (info.TriggerWords != null && info.TriggerWords.Count > 0)
                desc += $" Trigger words: {string.Join(", ", info.TriggerWords.Take(5))}.";

            if (info.CivitaiUrl != null)
                desc += $" [Civitai]({info.CivitaiUrl})";

            return desc.Trim();
        }
    }
}
