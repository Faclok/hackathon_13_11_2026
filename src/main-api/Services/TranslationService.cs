using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

/// <summary>Reads and exports translations accepted by community consensus.</summary>
public sealed class TranslationService(AppDbContext db)
{
    /// <summary>Returns confirmed translations and parses their JSONB source metadata.</summary>
    public async Task<object> List()
    {
        var rows = await (from d in db.Translations.AsNoTracking()
            join f in db.Fragments.AsNoTracking() on d.FragmentId equals f.Id
            join s in db.Sources.AsNoTracking() on f.SourceId equals s.Id
            where d.Status == "confirmed"
            orderby f.TextBa
            select new { fragment_id = d.FragmentId, text_ba = d.TextBa, lang_pair = d.LangPair,
                translation = d.Translation, confirmed = d.Confirmed, status = d.Status, source = d.Source,
                source_title = s.Title, dataset = s.Dataset, original_id = s.OriginalId,
                source_url = s.SourceUrl, attribution = s.Attribution, license = s.License }).ToListAsync();
        return rows.Select(row => new { row.fragment_id, row.text_ba, row.lang_pair,
            row.translation, row.confirmed, confirmations = row.confirmed, row.status,
            source = JsonNode.Parse(row.source), row.source_title, row.dataset, row.original_id,
            row.source_url, row.attribution, row.license }).ToList();
    }

    /// <summary>Exports confirmed translations with CSV quoting and a UTF-8 BOM.</summary>
    public async Task<byte[]> ExportCsv()
    {
        var rows = await (from d in db.Translations.AsNoTracking()
            join f in db.Fragments.AsNoTracking() on d.FragmentId equals f.Id
            join s in db.Sources.AsNoTracking() on f.SourceId equals s.Id
            where d.Status == "confirmed"
            orderby f.TextBa
            select new { fragment_id = d.FragmentId, text_ba = d.TextBa, lang_pair = d.LangPair,
                translation = d.Translation, source = d.Source, confirmed = d.Confirmed, status = d.Status,
                dataset = s.Dataset, original_id = s.OriginalId, source_url = s.SourceUrl }).ToListAsync();
        var names = new[] { "fragment_id", "text_ba", "lang_pair", "translation", "source", "confirmed", "status", "dataset", "original_id", "source_url" };
        var csv = new StringBuilder(string.Join(',', names) + "\r\n");
        foreach (var row in rows)
        {
            var values = new object?[] { row.fragment_id, row.text_ba, row.lang_pair, row.translation, row.source,
                row.confirmed, row.status, row.dataset, row.original_id, row.source_url };
            csv.AppendLine(string.Join(',', values.Select(value => "\"" + (value?.ToString() ?? "").Replace("\"", "\"\"") + "\"")));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
}
